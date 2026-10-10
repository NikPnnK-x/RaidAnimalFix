using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RaidAnimalFix;

internal static class CaravanLeaveHelper
{
	private static int _lastErrorTick = -999999;

	public static Faction Me()
	{
		Faction faction = null;
		try
		{
			faction = MpHelper.GetCurrentPlayerFaction();
		}
		catch
		{
		}
		if (faction == null)
		{
			faction = Faction.OfPlayer;
		}
		return faction;
	}

	public static bool IsOtherPlayersMap(Map map, Faction me)
	{
		if (map == null || map.Parent == null || me == null)
		{
			return false;
		}
		Faction faction = map.Parent.Faction;
		if (faction == null || faction == me)
		{
			return false;
		}
		return faction.IsPlayer;
	}

	public static bool IsOurFreeColonist(Pawn p, Faction me)
	{
		if (p != null && p.Spawned && !p.Dead && p.Faction == me && p.HostFaction == null && p.RaceProps != null)
		{
			return p.RaceProps.Humanlike;
		}
		return false;
	}

	public static int CountOurColonists(Map map, Faction me)
	{
		if (map == null || map.mapPawns == null)
		{
			return 0;
		}
		int num = 0;
		IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
		for (int i = 0; i < allPawnsSpawned.Count; i++)
		{
			if (IsOurFreeColonist(allPawnsSpawned[i], me))
			{
				num++;
			}
		}
		return num;
	}

	public static bool VanillaReformAvailable(Map map)
	{
		try
		{
			if (map == null || map.Parent == null)
			{
				return false;
			}
			return map.Parent.GetComponent<FormCaravanComp>()?.CanReformNow() ?? false;
		}
		catch
		{
			return false;
		}
	}

	public static Gizmo MakeReformGizmo(Map map, Faction me)
	{
		Command_Action command_Action = new Command_Action();
		command_Action.defaultLabel = "CommandReformCaravan".Translate();
		command_Action.defaultDesc = "CommandReformCaravanDesc".Translate() + "\n\n[RaidAnimalFix] " + "RAF_ReformCaravanDesc".Translate();
		command_Action.icon = FormCaravanComp.FormCaravanCommand;
		command_Action.action = delegate
		{
			OpenReformDialog(map);
		};
		if (GenHostility.AnyHostileActiveThreatTo(map, me, out var threat))
		{
			string text = ((threat != null && threat.Thing != null) ? threat.Thing.LabelShortCap : "?");
			command_Action.Disable("CommandReformCaravanFailHostilePawns".Translate() + " (" + text + ")");
		}
		return command_Action;
	}

	public static void OpenReformDialog(Map map)
	{
		try
		{
			if (map != null)
			{
				Faction me = Me();
				Faction foreignCaravanSessionFaction = MpSessionHelper.GetForeignCaravanSessionFaction(map, me);
				if (foreignCaravanSessionFaction != null)
				{
					Messages.Message("[RaidAnimalFix] " + "RAF_ForeignCaravanSession".Translate(foreignCaravanSessionFaction.Name), MessageTypeDefOf.RejectInput, historical: false);
				}
				else
				{
					Find.WindowStack.Add(new Dialog_FormCaravan(map, reform: true));
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error("[RaidAnimalFix] OpenReformDialog: " + ex);
		}
	}

	public static void LogErrorThrottled(string where, Exception ex)
	{
		int num = ((Find.TickManager != null) ? Find.TickManager.TicksGame : 0);
		if (num - _lastErrorTick >= 600)
		{
			_lastErrorTick = num;
			Log.Warning("[RaidAnimalFix] " + where + ": " + ex.Message);
		}
	}
}
