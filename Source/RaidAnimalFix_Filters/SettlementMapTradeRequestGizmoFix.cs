using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RaidAnimalFix;

[HarmonyPatch("GetGizmos")]
[HarmonyPatch(typeof(Settlement))]
public static class SettlementMapTradeRequestGizmoFix
{
	private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Settlement __instance)
	{
		foreach (Gizmo item in __result)
		{
			yield return item;
		}
		Gizmo extra = null;
		try
		{
			if (MpHelper.IsMultiplayer() && __instance != null && __instance.HasMap)
			{
				Faction me = CaravanLeaveHelper.Me();
				TradeRequestComp comp;
				Settlement settlement = MapTradeRequestFulfill.RequestSettlementOf(__instance.Map, me, out comp);
				if (settlement != null && CaravanLeaveHelper.CountOurColonists(__instance.Map, me) > 0)
				{
					extra = MapTradeRequestFulfill.MakeGizmo(settlement, comp, __instance.Map, me);
				}
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("SettlementMapTradeRequestGizmoFix", ex);
		}
		if (extra != null)
		{
			yield return extra;
		}
	}
}
