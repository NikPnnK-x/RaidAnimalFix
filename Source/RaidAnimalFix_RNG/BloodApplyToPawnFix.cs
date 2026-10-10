using System.Reflection;
using HarmonyLib;

namespace RaidAnimalFix;

[HarmonyPatch]
public static class BloodApplyToPawnFix
{
	private static MethodBase TargetMethod()
	{
		return BloodAnimGuard.Entry("BloodAnimations.DamageWorker_AddInjury_ApplyToPawn", "ApplyToPawn");
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
