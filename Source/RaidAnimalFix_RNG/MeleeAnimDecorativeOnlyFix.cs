using System;
using System.Reflection;
using HarmonyLib;

namespace RaidAnimalFix;

[HarmonyPatch]
public static class MeleeAnimDecorativeOnlyFix
{
	private static MethodBase TargetMethod()
	{
		Type type = RafTypes.Find("AM.AnimRenderer");
		if (type == null)
		{
			return null;
		}
		return AccessTools.Method(type, "Register");
	}

	private static bool Prefix(object __instance, ref bool __result)
	{
		if (!MpHelper.IsMultiplayer())
		{
			return true;
		}
		if (!MeleeAnimGuard.ChangesWorldState(__instance))
		{
			return true;
		}
		MeleeAnimGuard.LogOnce();
		__result = false;
		return false;
	}
}
