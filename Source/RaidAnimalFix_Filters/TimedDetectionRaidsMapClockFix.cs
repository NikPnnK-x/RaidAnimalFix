using HarmonyLib;
using RimWorld.Planet;

namespace RaidAnimalFix;

[HarmonyPatch("CompTickInterval")]
[HarmonyPatch(typeof(TimedDetectionRaids))]
public static class TimedDetectionRaidsMapClockFix
{
	[HarmonyPrefix]
	public static bool Prefix(TimedDetectionRaids __instance)
	{
		try
		{
			if (!MpHelper.IsMultiplayer())
			{
				return true;
			}
			if (!(__instance.parent is MapParent mapParent) || !mapParent.HasMap)
			{
				return true;
			}
			return MpContext.TickingMap() == mapParent.Map;
		}
		catch
		{
			return true;
		}
	}
}
