using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RaidAnimalFix;

[HarmonyPatch("IncidentTargetTags")]
[HarmonyPatch(typeof(Map))]
public static class ForeignStorytellerTargetFix
{
	private static readonly Dictionary<int, int> _lastLogTick = new Dictionary<int, int>();

	private static IEnumerable<IncidentTargetTagDef> Postfix(IEnumerable<IncidentTargetTagDef> __result, Map __instance)
	{
		bool foreign = false;
		try
		{
			foreign = IsForeignForCurrentFaction(__instance);
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("ForeignStorytellerTargetFix", ex);
		}
		if (!foreign)
		{
			foreach (IncidentTargetTagDef item in __result)
			{
				yield return item;
			}
			yield break;
		}
		bool hadAny = false;
		foreach (IncidentTargetTagDef item2 in __result)
		{
			_ = item2;
			hadAny = true;
		}
		if (hadAny)
		{
			LogOnce(__instance);
		}
	}

	public static bool IsForeignForCurrentFaction(Map map)
	{
		if (!MpHelper.IsMultiplayer())
		{
			return false;
		}
		if (map == null)
		{
			return false;
		}
		Faction ofPlayer = Faction.OfPlayer;
		// Контекст наблюдателя (мировой тик) — не рассказчик игрока; не режем
		// цели, иначе пропадают Map_RaidBeacon и прочие теги всех карт.
		if (ofPlayer == null || !MpContext.IsRealPlayerFaction(ofPlayer))
		{
			return false;
		}
		Faction parentFaction = map.ParentFaction;
		if (MpContext.IsRealPlayerFaction(parentFaction))
		{
			return parentFaction != ofPlayer;
		}
		if (NotificationFilter.MapHasColonistsOf(map, ofPlayer))
		{
			return false;
		}
		if (map.mapPawns == null)
		{
			return false;
		}
		IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
		for (int i = 0; i < allPawnsSpawned.Count; i++)
		{
			Pawn pawn = allPawnsSpawned[i];
			if (pawn != null && pawn.Faction != null && pawn.Faction != ofPlayer && pawn.RaceProps != null && pawn.RaceProps.Humanlike && MpContext.IsRealPlayerFaction(pawn.Faction))
			{
				return true;
			}
		}
		return false;
	}

	private static void LogOnce(Map map)
	{
		try
		{
			int ticksGame = Find.TickManager.TicksGame;
			if (!_lastLogTick.TryGetValue(map.uniqueID, out var value) || ticksGame - value >= 60000)
			{
				_lastLogTick[map.uniqueID] = ticksGame;
				Faction ofPlayer = Faction.OfPlayer;
				Log.Message(string.Concat("[RaidAnimalFix] StorytellerTargets: map tile ", map.Tile, " (owner ", (map.ParentFaction != null) ? map.ParentFaction.Name : "none", ") is not an incident target for storyteller of '", (ofPlayer != null) ? ofPlayer.Name : "?", "'"));
			}
		}
		catch
		{
		}
	}
}
