using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RaidAnimalFix;

[HarmonyPatch("GetGizmos")]
[HarmonyPatch(typeof(Pawn))]
public static class PawnMapTradeRequestGizmoFix
{
	private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
	{
		foreach (Gizmo item in __result)
		{
			yield return item;
		}
		Gizmo extra = null;
		try
		{
			if (MpHelper.IsMultiplayer() && __instance != null && __instance.Spawned && __instance.Map != null)
			{
				Faction me = CaravanLeaveHelper.Me();
				if (CaravanLeaveHelper.IsOurFreeColonist(__instance, me))
				{
					TradeRequestComp comp;
					Settlement settlement = MapTradeRequestFulfill.RequestSettlementOf(__instance.Map, me, out comp);
					if (settlement != null)
					{
						extra = MapTradeRequestFulfill.MakeGizmo(settlement, comp, __instance.Map, me);
					}
				}
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("PawnMapTradeRequestGizmoFix", ex);
		}
		if (extra != null)
		{
			yield return extra;
		}
	}
}
