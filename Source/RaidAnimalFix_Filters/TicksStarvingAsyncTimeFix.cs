using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RaidAnimalFix;

[HarmonyPatch(typeof(Need_Food))]
[HarmonyPatch("TicksStarving", MethodType.Getter)]
public static class TicksStarvingAsyncTimeFix
{
	private static FieldInfo _pawnField;

	[HarmonyPostfix]
	public static void Postfix(Need_Food __instance, ref int __result)
	{
		try
		{
			if (!MpHelper.IsMultiplayer() || !UiOnly.InInterface())
			{
				return;
			}
			if (_pawnField == null)
			{
				_pawnField = AccessTools.Field(typeof(Need), "pawn");
			}
			if (_pawnField == null || !(_pawnField.GetValue(__instance) is Pawn pawn))
			{
				return;
			}
			int ticks;
			if (pawn.Map != null)
			{
				if (!MpMapTime.TryGetMapTicks(pawn.Map, out ticks))
				{
					return;
				}
			}
			else if (!MpMapTime.TryGetWorldTicks(out ticks))
			{
				return;
			}
			__result = Math.Max(0, ticks - __instance.lastNonStarvingTick);
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("TicksStarvingAsyncTimeFix", ex);
		}
	}
}
