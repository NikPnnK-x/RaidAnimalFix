using HarmonyLib;
using Verse.AI;

namespace RaidAnimalFix;

[HarmonyPatch(typeof(Pawn_PathFollower))]
[HarmonyPatch("PatherFailed")]
public static class PatherFailReasonProbe
{
	[HarmonyPrefix]
	public static void Prefix(Pawn_PathFollower __instance)
	{
		PatherFailLog.Note(__instance);
	}
}
