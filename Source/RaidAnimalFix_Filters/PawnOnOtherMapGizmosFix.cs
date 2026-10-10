using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RaidAnimalFix;

[HarmonyPatch("GetGizmos")]
[HarmonyPatch(typeof(Pawn))]
public static class PawnOnOtherMapGizmosFix
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
			extra = TryMake(__instance);
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("PawnOnOtherMapGizmosFix", ex);
		}
		if (extra != null)
		{
			yield return extra;
		}
	}

	private static Gizmo TryMake(Pawn pawn)
	{
		if (!MpHelper.IsMultiplayer())
		{
			return null;
		}
		if (pawn == null || !pawn.Spawned || pawn.Map == null)
		{
			return null;
		}
		Faction me = CaravanLeaveHelper.Me();
		if (!CaravanLeaveHelper.IsOurFreeColonist(pawn, me))
		{
			return null;
		}
		if (!CaravanLeaveHelper.IsOtherPlayersMap(pawn.Map, me))
		{
			return null;
		}
		return CaravanLeaveHelper.MakeReformGizmo(pawn.Map, me);
	}
}
