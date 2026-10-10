using HarmonyLib;
using Verse;

namespace RaidAnimalFix;

[HarmonyPatch("FinalizeInit")]
[HarmonyPatch(typeof(Game))]
public static class GameInitPatch
{
	private static void Postfix()
	{
		ModInitializer.EnsureDesyncMonitor();
	}
}
