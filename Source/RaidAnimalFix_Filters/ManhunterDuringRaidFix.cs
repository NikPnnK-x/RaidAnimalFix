using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RaidAnimalFix;

[HarmonyPatch]
public static class ManhunterDuringRaidFix
{
	private static MethodBase TargetMethod()
	{
		Type type = RafTypes.Find("RimWorld.IncidentWorker_AggressiveAnimals") ?? RafTypes.Find("RimWorld.IncidentWorker_ManhunterPack");
		if (type == null)
		{
			return null;
		}
		return AccessTools.Method(type, "TryExecuteWorker");
	}

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
			if (!CombatCooldownRules.AnimalAttackAllowed(map))
			{
				Log.Message("[RaidAnimalFix] ManhunterFix: blocked manhunters during combat on tile " + map.Tile);
				return false;
			}
		}
		catch
		{
		}
		return true;
	}
}
