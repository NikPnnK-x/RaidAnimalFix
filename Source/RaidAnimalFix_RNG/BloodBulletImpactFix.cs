using System.Reflection;
using HarmonyLib;

namespace RaidAnimalFix;

[HarmonyPatch]
public static class BloodBulletImpactFix
{
	private static MethodBase TargetMethod()
	{
		return BloodAnimGuard.Entry("BloodAnimations.Bullet_Impact", "Impact");
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
