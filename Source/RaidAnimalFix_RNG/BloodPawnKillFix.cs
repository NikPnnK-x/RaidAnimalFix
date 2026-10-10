using System.Reflection;
using HarmonyLib;

namespace RaidAnimalFix;

[HarmonyPatch]
public static class BloodPawnKillFix
{
	private static MethodBase TargetMethod()
	{
		return BloodAnimGuard.Entry("BloodAnimations.Pawn_Kill", "Kill");
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
