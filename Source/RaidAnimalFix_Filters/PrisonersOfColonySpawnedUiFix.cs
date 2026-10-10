using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace RaidAnimalFix;

[HarmonyPatch("PrisonersOfColonySpawned", MethodType.Getter)]
[HarmonyPatch(typeof(MapPawns))]
public static class PrisonersOfColonySpawnedUiFix
{
	[HarmonyPostfix]
	public static void Postfix(ref List<Pawn> __result)
	{
		try
		{
			__result = UiOnly.FilterPrisoners(__result);
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("PrisonersOfColonySpawnedUiFix", ex);
		}
	}
}
