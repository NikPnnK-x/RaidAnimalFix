using System;
using System.Reflection;
using HarmonyLib;

namespace RaidAnimalFix;

[HarmonyPatch]
public static class BloodMoteThrowCasingFix
{
	private static MethodBase TargetMethod()
	{
		Type type = AccessTools.TypeByName("BloodAnimations.MoteThrowCasing");
		if (type == null)
		{
			return null;
		}
		return AccessTools.Constructor(type, Type.EmptyTypes);
	}

	private static void Prefix(ref bool __state)
	{
		BloodAnimGuard.Begin(ref __state);
	}

	private static void Finalizer(bool __state)
	{
		BloodAnimGuard.End(__state);
	}
}
