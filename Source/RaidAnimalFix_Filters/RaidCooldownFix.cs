using HarmonyLib;
using RimWorld;
using Verse;

namespace RaidAnimalFix;

[HarmonyPatch("TryExecuteWorker")]
[HarmonyPatch(typeof(IncidentWorker_Raid))]
public static class RaidCooldownFix
{
	private static bool Prefix(IncidentParms parms)
	{
		if (!MpHelper.IsMultiplayer())
		{
			return true;
		}
		try
		{
			Map map = ((parms != null) ? (parms.target as Map) : null);
			if (map == null)
			{
				return true;
			}
			if (!CombatCooldownRules.RaidAllowed(map))
			{
				Log.Message(string.Concat("[RaidAnimalFix] RaidCooldown: blocked raid on tile ", map.Tile, " (big threat < 10s ago)"));
				return false;
			}
		}
		catch
		{
		}
		return true;
	}
}
