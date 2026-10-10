using System;
using HarmonyLib;
using RimWorld.Planet;

namespace RaidAnimalFix;

[HarmonyPatch("CompTickInterval")]
[HarmonyPatch(typeof(TimedDetectionRaids))]
public static class SiteRaidCountdownMapTimeFix
{
	[ThreadStatic]
	internal static bool drivenByMapTick;

	[HarmonyPrefix]
	public static bool Prefix(TimedDetectionRaids __instance)
	{
		try
		{
			if (!MpHelper.IsMultiplayer())
			{
				return true;
			}
			if (drivenByMapTick)
			{
				return true;
			}
			MapParent mapParent = ((__instance == null) ? null : (__instance.parent as MapParent));
			if (mapParent == null || !mapParent.HasMap)
			{
				return true;
			}
			return false;
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("SiteRaidCountdownMapTimeFix", ex);
			return true;
		}
	}
}
