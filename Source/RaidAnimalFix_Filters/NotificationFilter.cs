using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RaidAnimalFix;

internal static class NotificationFilter
{
	internal enum Verdict
	{
		Unknown,
		Ours,
		Foreign
	}

	public static bool ShouldHide(LookTargets targets, out string reason)
	{
		reason = null;
		Faction currentPlayerFaction = MpHelper.GetCurrentPlayerFaction();
		if (currentPlayerFaction == null)
		{
			return false;
		}
		switch (Classify(targets, currentPlayerFaction))
		{
		case Verdict.Ours:
			return false;
		case Verdict.Foreign:
			reason = "targets belong to another player";
			return true;
		default:
		{
			Faction ofPlayer = Faction.OfPlayer;
			if (ForeignFactionContext(ofPlayer, currentPlayerFaction))
			{
				reason = "created in context of faction '" + ofPlayer.Name + "'";
				return true;
			}
			return false;
		}
		}
	}

	public static Verdict Classify(LookTargets lookTargets, Faction me)
	{
		if (lookTargets == null)
		{
			return Verdict.Unknown;
		}
		List<GlobalTargetInfo> targets = lookTargets.targets;
		if (targets == null || targets.Count == 0)
		{
			return Verdict.Unknown;
		}
		bool flag = false;
		bool flag2 = false;
		for (int i = 0; i < targets.Count; i++)
		{
			switch (ClassifyTarget(targets[i], me))
			{
			case Verdict.Ours:
				return Verdict.Ours;
			case Verdict.Foreign:
				flag = true;
				break;
			default:
				flag2 = true;
				break;
			}
		}
		if (flag && !flag2)
		{
			return Verdict.Foreign;
		}
		return Verdict.Unknown;
	}

	private static Verdict ClassifyTarget(GlobalTargetInfo t, Faction me)
	{
		if (t.HasThing)
		{
			Thing thing = t.Thing;
			if (thing == null)
			{
				return Verdict.Unknown;
			}
			if (IsOurs(thing, me))
			{
				return Verdict.Ours;
			}
			Map mapHeld = thing.MapHeld;
			if (mapHeld != null)
			{
				return ClassifyMap(mapHeld, me);
			}
			if (thing is Pawn thing2)
			{
				Caravan caravan = thing2.GetCaravan();
				if (caravan != null)
				{
					return ClassifyFaction(caravan.Faction, me);
				}
			}
			return ClassifyFaction(thing.Faction, me);
		}
		if (t.HasWorldObject)
		{
			WorldObject worldObject = t.WorldObject;
			if (worldObject == null)
			{
				return Verdict.Unknown;
			}
			if (worldObject.Faction == me)
			{
				return Verdict.Ours;
			}
			if (worldObject is MapParent mapParent && mapParent.HasMap)
			{
				return ClassifyMap(mapParent.Map, me);
			}
			if (MpContext.IsRealPlayerFaction(worldObject.Faction))
			{
				return Verdict.Foreign;
			}
			return ClassifyTile(worldObject.Tile, me);
		}
		if (t.IsValid)
		{
			if (t.Map != null)
			{
				return ClassifyMap(t.Map, me);
			}
			return ClassifyTile(t.Tile, me);
		}
		return Verdict.Unknown;
	}

	public static Verdict ClassifyMap(Map map, Faction me)
	{
		if (map == null)
		{
			return Verdict.Unknown;
		}
		if (MapHasColonistsOf(map, me))
		{
			return Verdict.Ours;
		}
		Faction parentFaction = map.ParentFaction;
		if (parentFaction == me)
		{
			return Verdict.Ours;
		}
		if (MpContext.IsRealPlayerFaction(parentFaction))
		{
			return Verdict.Foreign;
		}
		if (MapHasOtherPlayerColonists(map, me))
		{
			return Verdict.Foreign;
		}
		return Verdict.Unknown;
	}

	private static Verdict ClassifyTile(int tile, Faction me)
	{
		if (tile < 0)
		{
			return Verdict.Unknown;
		}
		List<Caravan> caravans = Find.WorldObjects.Caravans;
		for (int i = 0; i < caravans.Count; i++)
		{
			if (caravans[i] != null && (int)caravans[i].Tile == tile && caravans[i].Faction == me)
			{
				return Verdict.Ours;
			}
		}
		int tileOwner = LiveTileOwner(tile);
		if (tileOwner < 0)
		{
			return Verdict.Unknown;
		}
		if (tileOwner == me.loadID)
		{
			return Verdict.Ours;
		}
		if (IsPlayerFactionId(tileOwner))
		{
			return Verdict.Foreign;
		}
		return Verdict.Unknown;
	}

	// Живой поиск владельца тайла (раньше — кэш PlayerFactionDetector,
	// который у разных клиентов обновлялся в разные моменты, и решение
	// «скрыть письмо» могло расходиться внутри одной фракции).
	private static int LiveTileOwner(int tile)
	{
		WorldObjectsHolder worldObjects = Find.WorldObjects;
		if (worldObjects == null)
		{
			return -1;
		}
		Settlement settlement = worldObjects.SettlementAt(tile);
		if (settlement != null && settlement.Faction != null)
		{
			return settlement.Faction.loadID;
		}
		List<WorldObject> all = worldObjects.AllWorldObjects;
		for (int i = 0; i < all.Count; i++)
		{
			WorldObject worldObject = all[i];
			if (worldObject != null && worldObject.Faction != null && !(worldObject is Caravan) && (int)worldObject.Tile == tile)
			{
				return worldObject.Faction.loadID;
			}
		}
		return -1;
	}

	private static Verdict ClassifyFaction(Faction f, Faction me)
	{
		if (f == null)
		{
			return Verdict.Unknown;
		}
		if (f == me)
		{
			return Verdict.Ours;
		}
		if (MpContext.IsRealPlayerFaction(f))
		{
			return Verdict.Foreign;
		}
		return Verdict.Unknown;
	}

	private static bool IsOurs(Thing thing, Faction me)
	{
		if (thing.Faction == me)
		{
			return true;
		}
		if (thing is Pawn pawn && pawn.HostFaction == me)
		{
			return true;
		}
		return false;
	}

	public static bool MapHasColonistsOf(Map map, Faction f)
	{
		if (map == null || map.mapPawns == null || f == null)
		{
			return false;
		}
		IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
		for (int i = 0; i < allPawnsSpawned.Count; i++)
		{
			Pawn pawn = allPawnsSpawned[i];
			if (pawn != null && pawn.Faction == f && pawn.RaceProps != null && pawn.RaceProps.Humanlike)
			{
				return true;
			}
		}
		return false;
	}

	private static bool MapHasOtherPlayerColonists(Map map, Faction me)
	{
		if (map == null || map.mapPawns == null)
		{
			return false;
		}
		IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
		for (int i = 0; i < allPawnsSpawned.Count; i++)
		{
			Pawn pawn = allPawnsSpawned[i];
			if (pawn != null && pawn.Faction != null && pawn.Faction != me && pawn.RaceProps != null && pawn.RaceProps.Humanlike && MpContext.IsRealPlayerFaction(pawn.Faction))
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsPlayerFactionId(int loadId)
	{
		if (Find.FactionManager == null)
		{
			return false;
		}
		List<Faction> allFactionsListForReading = Find.FactionManager.AllFactionsListForReading;
		for (int i = 0; i < allFactionsListForReading.Count; i++)
		{
			if (allFactionsListForReading[i].loadID == loadId)
			{
				return MpContext.IsRealPlayerFaction(allFactionsListForReading[i]);
			}
		}
		return false;
	}

	public static bool ForeignFactionContext(Faction ctx, Faction me)
	{
		if (ctx == null || me == null || ctx == me)
		{
			return false;
		}
		if (!ctx.IsPlayer)
		{
			return false;
		}
		if (!MpContext.SpectatorKnown())
		{
			return false;
		}
		if (MpContext.IsSpectator(ctx))
		{
			return false;
		}
		Map map = MpContext.TickingMap();
		if (map != null && MapHasColonistsOf(map, me))
		{
			return false;
		}
		return true;
	}
}
