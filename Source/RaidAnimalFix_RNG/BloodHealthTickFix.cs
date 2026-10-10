using System.Reflection;
using HarmonyLib;

namespace RaidAnimalFix;

[HarmonyPatch]
public static class BloodHealthTickFix
{
	private static MethodBase TargetMethod()
	{
		return BloodAnimGuard.Entry("BloodAnimations.Pawn_HealthTracker_HealthTick", "HealthTick");
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
