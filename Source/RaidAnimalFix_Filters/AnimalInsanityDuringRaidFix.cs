using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RaidAnimalFix;

[HarmonyPatch]
public static class AnimalInsanityDuringRaidFix
{
	private static MethodBase TargetMethod()
	{
		Type type = AccessTools.TypeByName("RimWorld.IncidentWorker_AnimalInsanityMass");
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
				Log.Message("[RaidAnimalFix] AnimalInsanityFix: blocked during combat on tile " + map.Tile);
				return false;
			}
		}
		catch
		{
		}
		return true;
	}
}
