using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RaidAnimalFix;

/// <summary>
/// IdeoManager.IdeoManagerTick идёт из тика мира, где Faction.OfPlayer —
/// наблюдатель без идеологий. Поэтому в MP multifaction:
///  - RitualObligationTrigger_Date (mustBePlayerIdeo) не видит идеологию в
///    Faction.OfPlayer.ideos — праздники по датам не наступают никогда;
///  - Precept_RoleSingle.RecacheActivity шлёт письма только при
///    Faction.OfPlayer.ideos.Has(ideo) — письма о ролях теряются;
///  - Ideo.RecacheColonistBelieverCount считает свободных колонистов
///    Faction.OfPlayer (наблюдателя = 0) — роли могут деактивироваться.
/// Решение: Ideo.IdeoTick идеологии, принадлежащей фракции игрока, выполняем в
/// контексте этой фракции (Faction.OfPlayer + данные мира + данные на всех
/// картах, как MP делает в мировом контексте). Это ровно то, что происходит в
/// одиночной игре. Каждая идеология тикает один раз (как в ванили), поэтому
/// двойных обязательств нет: при общей идеологии у нескольких фракций берём
/// фракцию с наименьшим loadID. Идеологии NPC тикают как раньше.
/// </summary>
[HarmonyPatch(typeof(Ideo), "IdeoTick")]
public static class DlcPfIdeoTickOwnerContext
{
	private static void Prefix(Ideo __instance, out bool __state)
	{
		__state = false;
		try
		{
			if (!ModsConfig.IdeologyActive || !DlcPf.Active || !DlcPf.InSpectatorContext())
			{
				return;
			}
			Faction owner = OwnerOf(__instance);
			if (owner != null)
			{
				__state = DlcPf.PushWorld(owner);
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("DlcPfIdeoTickOwnerContext", ex);
		}
	}

	private static void Finalizer(bool __state)
	{
		if (__state)
		{
			DlcPf.PopWorld();
		}
	}

	/// <summary>Реальная фракция игрока с этой идеологией (наименьший loadID).</summary>
	private static Faction OwnerOf(Ideo ideo)
	{
		Faction best = null;
		List<Faction> all = Find.FactionManager.AllFactionsListForReading;
		for (int i = 0; i < all.Count; i++)
		{
			Faction f = all[i];
			if (f.ideos != null && (best == null || f.loadID < best.loadID) && DlcPf.IsReal(f) && f.ideos.Has(ideo))
			{
				best = f;
			}
		}
		return best;
	}
}
