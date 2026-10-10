using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RaidAnimalFix;

/// <summary>
/// Владелец монолита для маршрутизации событий Anomaly в MP multifaction.
/// GameComponent_Anomaly тикает в тике мира (Faction.OfPlayer = наблюдатель),
/// и всё, что он ставит в Find.Storyteller.incidentQueue, попадает в очередь
/// рассказчика наблюдателя — её никто не тикает (FactionRepeater пропускает
/// наблюдателя): события не происходят, очередь растёт в сейве бесконечно.
/// </summary>
internal static class DlcPfAnomaly
{
	public static readonly AccessTools.FieldRef<GameComponent_Anomaly, int> FireLevelIncidentTick = AccessTools.FieldRefAccess<GameComponent_Anomaly, int>("fireLevelIncidentTick");

	public static readonly AccessTools.FieldRef<GameComponent_Anomaly, MonolithLevelDef> LevelDefRef = AccessTools.FieldRefAccess<GameComponent_Anomaly, MonolithLevelDef>("levelDef");

	public static bool Applies()
	{
		return ModsConfig.AnomalyActive && DlcPf.Active && DlcPf.InSpectatorContext() && MpSim.CanRestoreContext;
	}

	/// <summary>Карта монолита, если монолит заспавнен.</summary>
	public static Map MonolithMap(GameComponent_Anomaly anomaly)
	{
		Building_VoidMonolith monolith = anomaly?.monolith;
		return (monolith != null && monolith.Spawned) ? monolith.Map : null;
	}

	/// <summary>
	/// Владелец: фракция карты монолита (если реальный игрок), иначе реальная
	/// фракция игрока с наименьшим loadID. Активацию монолита делает пешка в
	/// контексте своей карты, т.е. её владелец — отдельно «кто активировал» не
	/// храним.
	/// </summary>
	public static Faction Owner(GameComponent_Anomaly anomaly)
	{
		Map map = MonolithMap(anomaly);
		Faction f = map?.ParentFaction;
		if (DlcPf.IsReal(f))
		{
			return f;
		}
		return DlcPf.FirstRealPlayerFaction();
	}

	/// <summary>
	/// Цель события: карта монолита, если она принадлежит владельцу, иначе
	/// любая домашняя карта владельца (вызывать в контексте владельца).
	/// </summary>
	public static Map TargetFor(GameComponent_Anomaly anomaly, Faction owner)
	{
		Map map = MonolithMap(anomaly);
		if (map != null && map.ParentFaction == owner)
		{
			return map;
		}
		return Find.AnyPlayerHomeMap;
	}

	/// <summary>
	/// Тик срабатывания в часах карты-цели: очередь с картой-целью тикает в тике
	/// этой карты (IncidentQueueTimeBaseFix сравнивает FireTick с часами карты).
	/// </summary>
	public static int FireTickNow(Map target)
	{
		if (target != null && MpMapTime.TryGetMapTicks(target, out var ticks))
		{
			return ticks;
		}
		return Find.TickManager.TicksGame;
	}
}

/// <summary>
/// TrySpawnHarbingerTrees из тика мира: ставим HarbingerTreeSpawn в очередь
/// рассказчика владельца монолита с целью = его карта (контекст владельца
/// переключает Find.Storyteller на его рассказчика).
/// </summary>
[HarmonyPatch(typeof(GameComponent_Anomaly), "TrySpawnHarbingerTrees")]
public static class DlcPfHarbingerTreeRouting
{
	private static bool Prefix(GameComponent_Anomaly __instance)
	{
		try
		{
			if (!DlcPfAnomaly.Applies())
			{
				return true;
			}
			Faction owner = DlcPfAnomaly.Owner(__instance);
			if (owner == null || !DlcPf.PushWorld(owner))
			{
				return true;
			}
			try
			{
				Map target = DlcPfAnomaly.TargetFor(__instance, owner);
				if (target != null)
				{
					IncidentParms parms = new IncidentParms
					{
						target = target,
						forced = true
					};
					Find.Storyteller.incidentQueue.Add(IncidentDefOf.HarbingerTreeSpawn, DlcPfAnomaly.FireTickNow(target), parms);
				}
			}
			finally
			{
				DlcPf.PopWorld();
			}
			return false;
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("DlcPfHarbingerTreeRouting", ex);
			return true;
		}
	}
}

/// <summary>
/// GameComponent_Anomaly.GameComponentTick, два места, завязанных на «игрока»:
/// 1) события уровня монолита (incidentsOnReached) — ставим их сами в очередь
///    рассказчика владельца с целью = карта монолита и обнуляем
///    fireLevelIncidentTick, чтобы ваниль не поставила их наблюдателю;
/// 2) «Disrupted» +50 репутации — начисляем владельцу (в его контексте), а
///    ванильную ветку на этот тик отключаем, временно сдвинув
///    metalHellClosedTick (других читателей в тике нет), и возвращаем его в
///    финализаторе.
/// Письма (серая пелена, возврат из пустоты) не трогаем — ими занимается
/// другая фича.
/// </summary>
[HarmonyPatch(typeof(GameComponent_Anomaly), "GameComponentTick")]
public static class DlcPfAnomalyTickRouting
{
	private const int Shifted = int.MinValue / 2;

	private static void Prefix(GameComponent_Anomaly __instance, out int __state)
	{
		__state = int.MinValue;
		try
		{
			if (!DlcPfAnomaly.Applies())
			{
				return;
			}
			int now = Find.TickManager.TicksGame;
			ref int fireTick = ref DlcPfAnomaly.FireLevelIncidentTick(__instance);
			MonolithLevelDef levelDef = DlcPfAnomaly.LevelDefRef(__instance);
			if (fireTick > 0 && now > fireTick)
			{
				if (QueueLevelIncidents(__instance, levelDef))
				{
					fireTick = -99999;
				}
			}
			if (now == __instance.metalHellClosedTick + 60 && levelDef == MonolithLevelDefOf.Disrupted)
			{
				if (DisruptedGoodwill(__instance))
				{
					__state = __instance.metalHellClosedTick;
					__instance.metalHellClosedTick = Shifted;
				}
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("DlcPfAnomalyTickRouting", ex);
		}
	}

	private static void Finalizer(GameComponent_Anomaly __instance, int __state)
	{
		if (__state != int.MinValue && __instance.metalHellClosedTick == Shifted)
		{
			__instance.metalHellClosedTick = __state;
		}
	}

	private static bool QueueLevelIncidents(GameComponent_Anomaly anomaly, MonolithLevelDef levelDef)
	{
		Faction owner = DlcPfAnomaly.Owner(anomaly);
		if (owner == null || !DlcPf.PushWorld(owner))
		{
			return false;
		}
		try
		{
			List<IncidentDef> incidents = levelDef?.incidentsOnReached;
			Map target = DlcPfAnomaly.TargetFor(anomaly, owner);
			if (incidents != null && target != null)
			{
				int fireTick = DlcPfAnomaly.FireTickNow(target);
				for (int i = 0; i < incidents.Count; i++)
				{
					IncidentParms parms = new IncidentParms
					{
						target = target,
						points = StorytellerUtility.DefaultThreatPointsNow(target),
						forced = true
					};
					Find.Storyteller.incidentQueue.Add(incidents[i], fireTick, parms);
				}
			}
		}
		finally
		{
			DlcPf.PopWorld();
		}
		return true;
	}

	private static bool DisruptedGoodwill(GameComponent_Anomaly anomaly)
	{
		Faction owner = DlcPfAnomaly.Owner(anomaly);
		if (owner == null || !DlcPf.PushWorld(owner))
		{
			return false;
		}
		try
		{
			foreach (Faction item in Find.FactionManager.AllFactionsVisible)
			{
				// Другие фракции игроков (и наблюдателя) исключаем: репутация между игроками не меняется.
				if (item != Faction.OfPlayer && !item.IsPlayer && item.def.humanlikeFaction && !item.def.permanentEnemy)
				{
					item.TryAffectGoodwillWith(Faction.OfPlayer, 50, canSendMessage: true, canSendHostilityLetter: true, HistoryEventDefOf.DestroyedVoidMonolith);
				}
			}
		}
		finally
		{
			DlcPf.PopWorld();
		}
		return true;
	}
}

/// <summary>
/// Notify_LevelChanged, вызванный из тика мира (квест/сигнал), ставит
/// VoidCuriosity в очередь наблюдателя и генерирует квест в его контексте.
/// Выполняем его в контексте владельца монолита. Из тика карты/команды
/// (активация пешкой) контекст уже правильный — не трогаем.
/// </summary>
[HarmonyPatch(typeof(GameComponent_Anomaly), "Notify_LevelChanged")]
public static class DlcPfAnomalyLevelChangedContext
{
	private static void Prefix(GameComponent_Anomaly __instance, out bool __state)
	{
		__state = false;
		try
		{
			if (!DlcPfAnomaly.Applies())
			{
				return;
			}
			Faction owner = DlcPfAnomaly.Owner(__instance);
			if (owner != null)
			{
				__state = DlcPf.PushWorld(owner);
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("DlcPfAnomalyLevelChangedContext", ex);
		}
	}

	private static void Finalizer(bool __state)
	{
		if (__state)
		{
			DlcPf.PopWorld();
		}
	}
}

/// <summary>
/// Разовая чистка: из очереди рассказчика наблюдателя удаляем накопившиеся
/// HarbingerTreeSpawn, срок которых уже прошёл (их никто никогда не выполнит;
/// раздувают сейв). Game.FinalizeInit идёт у всех клиентов одинаково в точке
/// входа, решение — только от синхронного состояния.
/// </summary>
[HarmonyPatch(typeof(Game), "FinalizeInit")]
public static class DlcPfSpectatorQueueCleanup
{
	private static readonly FieldInfo QueueListField = AccessTools.Field(typeof(IncidentQueue), "queuedIncidents");

	private static void Postfix()
	{
		try
		{
			if (!ModsConfig.AnomalyActive || !DlcPf.Active || QueueListField == null)
			{
				return;
			}
			Faction spectator = MpContext.Spectator();
			Storyteller st = DlcPf.StorytellerOf(spectator);
			if (st?.incidentQueue == null || !(QueueListField.GetValue(st.incidentQueue) is List<QueuedIncident> list))
			{
				return;
			}
			int now = Find.TickManager.TicksGame;
			int removed = list.RemoveAll((QueuedIncident q) => q?.FiringIncident != null && q.FiringIncident.def == IncidentDefOf.HarbingerTreeSpawn && q.FireTick < now);
			if (removed > 0)
			{
				Log.Message("[RaidAnimalFix] DlcPerFaction: removed " + removed + " stale HarbingerTreeSpawn from spectator incident queue");
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("DlcPfSpectatorQueueCleanup", ex);
		}
	}
}
