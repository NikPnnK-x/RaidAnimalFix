using HarmonyLib;
using Verse;

namespace RaidAnimalFix;

[HarmonyPatch(typeof(TickManager))]
[HarmonyPatch("ResetSettlementTicks")]
public static class ResetSettlementTicksMpFix
{
	private static bool Prefix()
	{
		if (!MpHelper.IsMultiplayer())
		{
			return true;
		}
		Log.Message("[RaidAnimalFix] ResetSettlementTicks skipped in multiplayer (shared counter)");
		return false;
	}
}
