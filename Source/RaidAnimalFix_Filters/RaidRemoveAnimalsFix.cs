using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RaidAnimalFix;

[HarmonyPatch(typeof(IncidentWorker_Raid))]
[HarmonyPatch("TryExecuteWorker")]
public static class RaidRemoveAnimalsFix
{
	private static Dictionary<Map, HashSet<int>> _pawnsBeforeRaid = new Dictionary<Map, HashSet<int>>();

	private static void Prefix(IncidentParms parms)
	{
		if (!MpHelper.IsMultiplayer() || parms == null)
		{
			return;
		}
		try
		{
			if (!(parms.target is Map map))
			{
				return;
			}
			HashSet<int> hashSet = new HashSet<int>();
			if (map.mapPawns != null && map.mapPawns.AllPawnsSpawned != null)
			{
				foreach (Pawn item in map.mapPawns.AllPawnsSpawned)
				{
					if (item != null)
					{
						hashSet.Add(item.thingIDNumber);
					}
				}
			}
			_pawnsBeforeRaid[map] = hashSet;
		}
		catch
		{
		}
	}

	// Если рейд отменён (RaidCooldownFix) или упал — снимок не должен висеть.
	private static void Finalizer(IncidentParms parms)
	{
		if (parms != null && parms.target is Map key)
		{
			_pawnsBeforeRaid.Remove(key);
		}
	}

	private static void Postfix(IncidentParms parms, ref bool __result)
	{
		if (!MpHelper.IsMultiplayer() || !__result || parms == null)
		{
			return;
		}
		try
		{
			if (!(parms.target is Map map))
			{
				return;
			}
			HashSet<int> value = null;
			if (!_pawnsBeforeRaid.TryGetValue(map, out value))
			{
				value = new HashSet<int>();
			}
			Dictionary<Faction, int> dictionary = new Dictionary<Faction, int>();
			List<Pawn> list = new List<Pawn>();
			if (map.mapPawns != null && map.mapPawns.AllPawnsSpawned != null)
			{
				foreach (Pawn item in map.mapPawns.AllPawnsSpawned)
				{
					if (item != null && !value.Contains(item.thingIDNumber) && item.Faction != null && !item.Faction.IsPlayer)
					{
						list.Add(item);
						if (item.RaceProps != null && item.RaceProps.Humanlike)
						{
							dictionary.TryGetValue(item.Faction, out var value2);
							dictionary[item.Faction] = value2 + 1;
						}
					}
				}
			}
			Faction faction = parms.faction;
			if (faction == null)
			{
				int num = 0;
				foreach (KeyValuePair<Faction, int> item2 in dictionary)
				{
					if (item2.Value > num)
					{
						num = item2.Value;
						faction = item2.Key;
					}
				}
			}
			int num2 = 0;
			if (faction != null)
			{
				foreach (Pawn item3 in list)
				{
					// Только то, что реально «примешалось» к рейду: дикие/чужие
					// животные или враждебные рейдерам. Квестовые пешки и союзники
					// рейдеров (смешанные рейды по задумке) не трогаем.
					if (item3 != null && !item3.Destroyed && item3.Faction != faction && !QuestUtility.IsReservedByQuestOrQuestBeingGenerated(item3) && (item3.Faction == null || item3.RaceProps.Animal || item3.Faction.HostileTo(faction)))
					{
						item3.Destroy(DestroyMode.Vanish);
						num2++;
					}
				}
			}
			if (num2 > 0)
			{
				Log.Message(string.Concat("[RaidAnimalFix] RaidCleanup: removed ", num2, " NEW pawns from mixed raid on tile ", map.Tile, " (main faction: ", (faction != null) ? faction.def.defName : "unknown", ")"));
			}
			_pawnsBeforeRaid.Remove(map);
		}
		catch
		{
		}
	}
}
