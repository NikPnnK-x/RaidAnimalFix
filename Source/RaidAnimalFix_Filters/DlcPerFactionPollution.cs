using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RaidAnimalFix;

/// <summary>
/// Biotech: CompDissolutionEffect_Goodwill копит события загрязнения в
/// статическом списке и в WorldUpdate штрафует Faction.OfPlayer. В MP список
/// разбирается в тике мира (наблюдатель) — штраф не получает никто.
/// Кроме того, ванильный World.WorldUpdate (кадр, вне тика) тоже разбирает
/// список — в MP это момент кадра, разный у клиентов, и Faction.OfPlayer
/// локального игрока: в MP разрешаем разбор только в тике мира.
///
/// Структура события приватная, поле фракции в неё не добавить, поэтому
/// ведём параллельный список loadID «загрязнителя». Он заполняется ровно там,
/// где ваниль добавляет событие (постфикс по росту счётчика), и очищается
/// там же, где ваниль очищает свой (WorldUpdate). Любое расхождение длины
/// (точка входа, события до загрузки мода) — все события «без фракции»
/// (обрабатываются как в ванили), так что состояние всегда функция синхронного
/// ванильного списка.
/// </summary>
internal static class DlcPfPollution
{
	private static readonly FieldInfo PendingField = AccessTools.Field(typeof(CompDissolutionEffect_Goodwill), "pendingGoodwillEvents");

	/// <summary>loadID фракции-загрязнителя для каждого события ванильного списка (-1 = неизвестно).</summary>
	public static readonly List<int> FactionIds = new List<int>();

	/// <summary>Фракция для AddWorldDissolutionEvent, выставленная вызывающим кодом (вещь/карта).</summary>
	public static Faction worldOverride;

	public static bool processing;

	public static IList Pending => PendingField?.GetValue(null) as IList;

	public static int PendingCount
	{
		get
		{
			IList p = Pending;
			return p?.Count ?? (-1);
		}
	}

	public static void Record(int before, Faction f)
	{
		IList p = Pending;
		if (p == null || before < 0)
		{
			return;
		}
		if (FactionIds.Count != before)
		{
			FactionIds.Clear();
			for (int i = 0; i < before; i++)
			{
				FactionIds.Add(-1);
			}
		}
		int id = DlcPf.IsReal(f) ? f.loadID : (-1);
		for (int j = before; j < p.Count; j++)
		{
			FactionIds.Add(id);
		}
	}

	/// <summary>Фракция игрока, которой принадлежит вещь (пешка/караван/объект мира по цепочке владельцев).</summary>
	public static Faction FactionOfThing(Thing thing)
	{
		if (thing == null)
		{
			return null;
		}
		if (DlcPf.IsReal(thing.Faction))
		{
			return thing.Faction;
		}
		IThingHolder holder = thing.ParentHolder;
		for (int i = 0; i < 16 && holder != null; i++)
		{
			if (holder is Pawn pawn && DlcPf.IsReal(pawn.Faction))
			{
				return pawn.Faction;
			}
			if (holder is WorldObject wo && DlcPf.IsReal(wo.Faction))
			{
				return wo.Faction;
			}
			if (holder is Map map)
			{
				return DlcPf.PlayerFactionForMap(map);
			}
			holder = holder.ParentHolder;
		}
		return null;
	}
}

/// <summary>Событие на карте (не дом игрока): загрязнитель — контекст, иначе владелец/гости карты.</summary>
[HarmonyPatch(typeof(CompDissolutionEffect_Goodwill), "DoDissolutionEffectMap")]
public static class DlcPfPollutionMapEvent
{
	private static void Prefix(out int __state)
	{
		__state = -1;
		if (ModsConfig.BiotechActive && DlcPf.Active)
		{
			__state = DlcPfPollution.PendingCount;
		}
	}

	private static void Postfix(CompDissolutionEffect_Goodwill __instance, int __state)
	{
		try
		{
			if (__state < 0 || DlcPfPollution.PendingCount <= __state)
			{
				return;
			}
			Faction f = DlcPf.ContextFaction() ?? DlcPf.PlayerFactionForMap(__instance.parent?.MapHeld);
			DlcPfPollution.Record(__state, f);
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("DlcPfPollutionMapEvent", ex);
		}
	}
}

/// <summary>Мировое растворение (караван и т.п.): загрязнитель — владелец вещи.</summary>
[HarmonyPatch(typeof(CompDissolutionEffect_Goodwill), "DoDissolutionEffectWorld")]
public static class DlcPfPollutionWorldEventOwner
{
	private static void Prefix(CompDissolutionEffect_Goodwill __instance, out Faction __state)
	{
		__state = DlcPfPollution.worldOverride;
		try
		{
			if (ModsConfig.BiotechActive && DlcPf.Active)
			{
				Faction f = DlcPfPollution.FactionOfThing(__instance.parent);
				if (f != null)
				{
					DlcPfPollution.worldOverride = f;
				}
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("DlcPfPollutionWorldEventOwner", ex);
		}
	}

	private static void Finalizer(Faction __state)
	{
		DlcPfPollution.worldOverride = __state;
	}
}

/// <summary>Удаление карты с загрязнением сверх исходного: загрязнитель — владелец/гости карты.</summary>
[HarmonyPatch(typeof(PollutionInfo), "MapRemoved")]
public static class DlcPfPollutionMapRemovedOwner
{
	private static void Prefix(PollutionInfo __instance, out Faction __state)
	{
		__state = DlcPfPollution.worldOverride;
		try
		{
			if (ModsConfig.BiotechActive && DlcPf.Active)
			{
				Faction f = DlcPf.PlayerFactionForMap(__instance.map);
				if (f != null)
				{
					DlcPfPollution.worldOverride = f;
				}
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("DlcPfPollutionMapRemovedOwner", ex);
		}
	}

	private static void Finalizer(Faction __state)
	{
		DlcPfPollution.worldOverride = __state;
	}
}

[HarmonyPatch(typeof(CompDissolutionEffect_Goodwill), "AddWorldDissolutionEvent")]
public static class DlcPfPollutionWorldEvent
{
	private static void Prefix(out int __state)
	{
		__state = -1;
		if (ModsConfig.BiotechActive && DlcPf.Active)
		{
			__state = DlcPfPollution.PendingCount;
		}
	}

	private static void Postfix(int __state)
	{
		try
		{
			if (__state < 0 || DlcPfPollution.PendingCount <= __state)
			{
				return;
			}
			Faction f = DlcPfPollution.worldOverride ?? DlcPf.ContextFaction();
			DlcPfPollution.Record(__state, f);
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("DlcPfPollutionWorldEvent", ex);
		}
	}
}

/// <summary>
/// Разбор событий: в MP — только в тике мира (детерминированно). В
/// multifaction события делятся по фракции-загрязнителю (по возрастанию
/// loadID), и ванильный WorldUpdate вызывается для каждой группы в контексте
/// её фракции: штраф репутации, сообщение и ответная месть идут ей. События
/// без фракции обрабатываются как раньше (в текущем контексте).
/// </summary>
[HarmonyPatch(typeof(CompDissolutionEffect_Goodwill), "WorldUpdate")]
public static class DlcPfPollutionGoodwillDrain
{
	private static bool Prefix()
	{
		if (DlcPfPollution.processing)
		{
			return true;
		}
		bool mp;
		try
		{
			mp = MP.IsInMultiplayer;
		}
		catch
		{
			mp = MpHelper.IsMultiplayer();
		}
		if (!mp || !ModsConfig.BiotechActive)
		{
			return true;
		}
		if (!MpContext.TickingWorld())
		{
			// Кадр (World.WorldUpdate) — оставляем события до тика мира.
			return false;
		}
		IList pending = DlcPfPollution.Pending;
		if (!DlcPf.Active || pending == null || pending.Count == 0 || !MpSim.CanRestoreContext)
		{
			DlcPfPollution.FactionIds.Clear();
			return true;
		}
		object[] items = new object[pending.Count];
		for (int i = 0; i < items.Length; i++)
		{
			items[i] = pending[i];
		}
		int[] ids = new int[items.Length];
		bool aligned = DlcPfPollution.FactionIds.Count == items.Length;
		for (int j = 0; j < ids.Length; j++)
		{
			ids[j] = aligned ? DlcPfPollution.FactionIds[j] : (-1);
		}
		List<int> groups = new List<int>();
		for (int k = 0; k < ids.Length; k++)
		{
			if (!groups.Contains(ids[k]))
			{
				groups.Add(ids[k]);
			}
		}
		groups.Sort();
		DlcPfPollution.processing = true;
		try
		{
			foreach (int fid in groups)
			{
				pending.Clear();
				for (int m = 0; m < items.Length; m++)
				{
					if (ids[m] == fid)
					{
						pending.Add(items[m]);
					}
				}
				Faction f = DlcPf.FactionById(fid);
				if (!DlcPf.IsReal(f) || !DlcPf.PushWorld(f))
				{
					RunVanilla();
					continue;
				}
				try
				{
					RunVanilla();
				}
				finally
				{
					DlcPf.PopWorld();
				}
			}
		}
		finally
		{
			DlcPfPollution.processing = false;
			pending.Clear();
			DlcPfPollution.FactionIds.Clear();
		}
		return false;
	}

	private static void RunVanilla()
	{
		try
		{
			CompDissolutionEffect_Goodwill.WorldUpdate();
		}
		catch (Exception ex)
		{
			Log.Error(ex.ToString());
		}
	}
}

/// <summary>
/// Мировое загрязнение тайлов: ванильный World.WorldUpdate (кадр) тоже
/// разбирает список — в MP момент кадра у клиентов разный. Разрешаем разбор
/// только в тике мира (MP вызывает его из AsyncWorldTimeComp.Tick).
/// </summary>
[HarmonyPatch(typeof(CompDissolutionEffect_Pollution), "WorldUpdate")]
public static class DlcPfPollutionWorldDrainTickOnly
{
	private static bool Prefix()
	{
		bool mp;
		try
		{
			mp = MP.IsInMultiplayer;
		}
		catch
		{
			mp = MpHelper.IsMultiplayer();
		}
		if (!mp || !ModsConfig.BiotechActive)
		{
			return true;
		}
		return MpContext.TickingWorld();
	}
}
