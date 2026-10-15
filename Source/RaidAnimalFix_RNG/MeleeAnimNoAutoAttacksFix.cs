using System;
using System.Reflection;
using HarmonyLib;

namespace RaidAnimalFix;

[HarmonyPatch]
public static class MeleeAnimNoAutoAttacksFix
{
	private static MethodBase TargetMethod()
	{
		Type type = RafTypes.Find("AM.Processing.MapPawnProcessor");
		if (type == null)
		{
			return null;
		}
		return AccessTools.Method(type, "Tick");
	}

	private static bool Prefix()
	{
		if (!MpHelper.IsMultiplayer())
		{
			return true;
		}
		MeleeAnimGuard.LogOnce();
		return false;
	}
}
