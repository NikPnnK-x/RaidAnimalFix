using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace RaidAnimalFix;

[HarmonyPatch("SlavesOfColonySpawned", MethodType.Getter)]
[HarmonyPatch(typeof(MapPawns))]
public static class SlavesOfColonySpawnedUiFix
{
	[HarmonyPostfix]
	public static void Postfix(ref List<Pawn> __result)
	{
		try
		{
			__result = UiOnly.FilterSlaves(__result);
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("SlavesOfColonySpawnedUiFix", ex);
		}
	}
}
