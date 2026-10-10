using System.Reflection;
using HarmonyLib;

namespace RaidAnimalFix;

[HarmonyPatch]
public static class BloodDropFilthDamageFix
{
	private static MethodBase TargetMethod()
	{
		return BloodAnimGuard.Entry("BloodAnimations.GenLeaving_DropFilthDueToDamage", "DropFilthDueToDamage");
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
