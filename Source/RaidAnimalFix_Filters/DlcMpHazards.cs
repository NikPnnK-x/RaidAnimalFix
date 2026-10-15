using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RaidAnimalFix;

/// <summary>
/// Odyssey: WorldComponent_LocationGenerator.WorldComponentTick назначает тик
/// пересоздания локаций по planetLayer.GetHashCode() — PlanetLayer его не
/// переопределяет, это хэш объекта, у каждого процесса свой. Локации на
/// орбите/слоях пересоздаются на разных тиках → мировой Rand и набор
/// объектов расходятся без всякого перезахода. Берём LayerID.
/// </summary>
[HarmonyPatch]
public static class LocationGeneratorLayerHashFix
{
	private static readonly MethodInfo ObjectGetHashCode = AccessTools.Method(typeof(object), "GetHashCode");

	private static MethodBase TargetMethod()
	{
		Type type = RafTypes.Find("RimWorld.Planet.WorldComponent_LocationGenerator");
		return (type == null) ? null : AccessTools.Method(type, "WorldComponentTick");
	}

	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
	{
		MethodInfo helper = AccessTools.Method(typeof(LocationGeneratorLayerHashFix), "LayerHash");
		int num = 0;
		foreach (CodeInstruction inst in insts)
		{
			if (inst.Calls(ObjectGetHashCode))
			{
				inst.opcode = OpCodes.Call;
				inst.operand = helper;
				num++;
			}
			yield return inst;
		}
		if (num != 1)
		{
			Log.Warning("[RaidAnimalFix] LocationGeneratorLayerHash: ожидалась 1 замена GetHashCode, получено " + num);
		}
	}

	public static int LayerHash(object layer)
	{
		if (layer is PlanetLayer planetLayer)
		{
			return Gen.HashCombineInt(planetLayer.LayerID, 0x4C4F43);
		}
		return layer?.GetHashCode() ?? 0;
	}
}

/// <summary>
/// MP (MultiplayerAsyncQuest.TryGetQuestMap) решает, на часах какой карты
/// тикает квест, по Map.IsPlayerHome — а это сравнение с Faction.OfPlayer
/// текущего контекста. Кэш квестов перестраивается в Game.FinalizeInit при
/// каждой точке входа (SaveAndReload у всех), и каждый клиент строит его в
/// контексте СВОЕЙ фракции: квест на карте игрока Б у Б тикает по часам
/// карты, у остальных — по мировым. Проверяем «карта принадлежит какому-то
/// игроку» — одинаково для всех.
/// </summary>
[HarmonyPatch]
public static class MpQuestMapFactionAgnosticFix
{
	private static readonly MethodInfo IsPlayerHome = AccessTools.PropertyGetter(typeof(Map), "IsPlayerHome");

	private static MethodBase TargetMethod()
	{
		Type type = RafTypes.Find("Multiplayer.Client.Comp.MultiplayerAsyncQuest");
		return (type == null) ? null : AccessTools.Method(type, "TryGetQuestMap");
	}

	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
	{
		MethodInfo helper = AccessTools.Method(typeof(MpQuestMapFactionAgnosticFix), "IsAnyPlayersColony");
		int num = 0;
		foreach (CodeInstruction inst in insts)
		{
			if (inst.Calls(IsPlayerHome))
			{
				inst.opcode = OpCodes.Call;
				inst.operand = helper;
				num++;
			}
			yield return inst;
		}
		if (num == 0)
		{
			Log.Warning("[RaidAnimalFix] MpQuestMap: Map.IsPlayerHome в TryGetQuestMap не найден — версия Multiplayer изменилась");
		}
	}

	public static bool IsAnyPlayersColony(Map map)
	{
		Faction faction = map?.ParentFaction;
		if (faction != null && faction.IsPlayer)
		{
			return !MpContext.IsSpectator(faction);
		}
		return false;
	}
}

/// <summary>
/// GravshipUtility кэширует двигатель карты по одному TicksGame. Кэш
/// заполняет и UI (полоса колонистов, тревоги → IsPlayerHome) в часах карты
/// под камерой; карта/мир с тем же значением TicksGame в симуляции берёт
/// этот кэш только у этого клиента. Сбрасываем кэш при смене контекста.
/// </summary>
[HarmonyPatch]
public static class GravEngineCacheContextFix
{
	private static FieldInfo _tickField;

	private static long _key = long.MinValue;

	private static MethodBase TargetMethod()
	{
		_tickField = AccessTools.Field(typeof(GravshipUtility), "lastCachedEngineTick");
		return (_tickField == null) ? null : AccessTools.Method(typeof(GravshipUtility), "GetPlayerGravEngine_NewTemp", new Type[1] { typeof(Map) });
	}

	private static void Prefix()
	{
		if (!MpHelper.IsMultiplayer())
		{
			return;
		}
		bool flag = MpSim.InSim() || MpSim.simEventDepth > 0;
		Map map = MpSim.MapContext();
		long num = ((long)Find.TickManager.TicksGame << 20) ^ ((long)(Faction.OfPlayer?.loadID ?? (-1)) << 4) ^ ((long)(((map?.uniqueID ?? (-1)) + 1) << 40)) ^ (flag ? 1 : 0);
		if (num != _key)
		{
			_key = num;
			try
			{
				_tickField.SetValue(null, -1);
			}
			catch
			{
			}
		}
	}
}

/// <summary>
/// Окно торговли MP пересчитывает общую сделку (MpTradeDeal.Recache) из UI
/// каждого, у кого оно открыто. У своих это детерминировано (данные карты их
/// фракции), но игрок ДРУГОЙ фракции пересчитывает её со своими зонами и
/// складами — набор товаров в сделке расходится. Чужим UI-пересчёт запрещён.
/// </summary>
[HarmonyPatch]
public static class MpTradeForeignRecacheFix
{
	private static FieldInfo _session;

	private static PropertyInfo _negotiatorFaction;

	private static MethodBase TargetMethod()
	{
		Type type = RafTypes.Find("Multiplayer.Client.MpTradeDeal");
		if (type == null)
		{
			return null;
		}
		_session = AccessTools.Field(type, "session");
		_negotiatorFaction = (_session == null) ? null : AccessTools.Property(_session.FieldType, "NegotiatorFaction");
		return (_negotiatorFaction == null) ? null : AccessTools.Method(type, "Recache");
	}

	private static bool Prefix(object __instance)
	{
		if (!MpHelper.IsMultiplayer() || MpSim.InSim() || MpSim.simEventDepth > 0)
		{
			return true;
		}
		try
		{
			object value = _session.GetValue(__instance);
			Faction faction = (value == null) ? null : (_negotiatorFaction.GetValue(value, null) as Faction);
			Faction currentPlayerFaction = MpHelper.GetCurrentPlayerFaction();
			return faction == null || currentPlayerFaction == null || faction == currentPlayerFaction;
		}
		catch
		{
			return true;
		}
	}
}

/// <summary>
/// GoodwillSituationManager — один на всех: кэш ситуаций ключуется только
/// второй фракцией, а считается от Faction.OfPlayer текущего контекста. Промах
/// из UI (подсказка вкладки фракций) не только кладёт в общий кэш данные
/// фракции этого клиента, но и зовёт CheckHostilityChanged →
/// Notify_GoodwillSituationsChanged, то есть меняет отношения из интерфейса.
/// В MP: в симуляции — свой кэш на пару (игрок, фракция), сбрасывается вместе
/// с ванильным пересчётом; в UI — расчёт без кэша и без побочных эффектов.
/// </summary>
[HarmonyPatch(typeof(GoodwillSituationManager), "GetSituations")]
public static class GoodwillSituationPerFactionFix
{
	private static readonly MethodInfo RecalcInto = AccessTools.Method(typeof(GoodwillSituationManager), "Recalculate", new Type[2]
	{
		typeof(Faction),
		typeof(List<GoodwillSituationManager.CachedSituation>)
	});

	private static readonly MethodInfo CheckHostility = AccessTools.Method(typeof(GoodwillSituationManager), "CheckHostilityChanged", new Type[2]
	{
		typeof(Faction),
		typeof(bool)
	});

	private static readonly Dictionary<long, List<GoodwillSituationManager.CachedSituation>> SimCache = new Dictionary<long, List<GoodwillSituationManager.CachedSituation>>();

	private static Game _game;

	public static void Clear()
	{
		SimCache.Clear();
	}

	private static bool Prefix(GoodwillSituationManager __instance, Faction other, ref List<GoodwillSituationManager.CachedSituation> __result)
	{
		if (!MpHelper.IsMultiplayer() || other == null || other.IsPlayer || RecalcInto == null || CheckHostility == null)
		{
			return true;
		}
		try
		{
			if (_game != Current.Game)
			{
				_game = Current.Game;
				SimCache.Clear();
			}
			Faction ofPlayer = Faction.OfPlayer;
			long key = ((long)(ofPlayer?.loadID ?? (-1)) << 32) | (uint)other.loadID;
			bool sim = MpSim.InSim() || MpSim.simEventDepth > 0;
			if (sim && SimCache.TryGetValue(key, out var value))
			{
				__result = value;
				return false;
			}
			List<GoodwillSituationManager.CachedSituation> list = new List<GoodwillSituationManager.CachedSituation>();
			RecalcInto.Invoke(__instance, new object[2] { other, list });
			if (sim)
			{
				SimCache[key] = list;
				CheckHostility.Invoke(__instance, new object[2] { other, true });
			}
			__result = list;
			return false;
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("GoodwillSituationPerFactionFix", ex);
			return true;
		}
	}
}

/// <summary>Ванильный периодический пересчёт (и AttackNow) — сигнал сбросить наш кэш.</summary>
[HarmonyPatch(typeof(GoodwillSituationManager), "RecalculateAll")]
public static class GoodwillSituationPerFactionInvalidate
{
	private static void Postfix()
	{
		if (MpHelper.IsMultiplayer())
		{
			GoodwillSituationPerFactionFix.Clear();
		}
	}
}
