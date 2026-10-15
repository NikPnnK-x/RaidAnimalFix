using System;
using System.Reflection;
using HarmonyLib;

namespace RaidAnimalFix;

[HarmonyPatch]
public static class MeleeAnimNoGrappleFix
{
	private static MethodBase TargetMethod()
	{
		Type type = RafTypes.Find("AM.Grappling.JobDriver_GrapplePawn");
		if (type == null)
		{
			return null;
		}
		return AccessTools.Method(type, "GiveJob");
	}

	private static bool Prefix(ref bool __result)
	{
		if (!MpHelper.IsMultiplayer())
		{
			return true;
		}
		MeleeAnimGuard.LogOnce();
		__result = false;
		return false;
	}
}
