using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RaidAnimalFix;

[HarmonyPatch("GetGizmos")]
[HarmonyPatch(typeof(Settlement))]
public static class SettlementReformGizmoFix
{
	private sealed class Ctx
	{
		public bool hideTutorial;

		public Gizmo reform;

		public Gizmo selectCaravan;
	}

	private static readonly Dictionary<int, float> _lastDiagTime = new Dictionary<int, float>();

	private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Settlement __instance)
	{
		Ctx ctx = null;
		try
		{
			ctx = Analyze(__instance);
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("SettlementReformGizmoFix", ex);
		}
		string tutorialLabel = null;
		if (ctx != null && ctx.hideTutorial)
		{
			try
			{
				tutorialLabel = "CommandFormCaravan".Translate();
			}
			catch
			{
			}
		}
		foreach (Gizmo g in __result)
		{
			if (tutorialLabel == null || !(g is Command command) || !(command.defaultLabel == tutorialLabel))
			{
				yield return g;
			}
		}
		if (ctx != null)
		{
			if (ctx.reform != null)
			{
				yield return ctx.reform;
			}
			if (ctx.selectCaravan != null)
			{
				yield return ctx.selectCaravan;
			}
		}
	}

	private static Ctx Analyze(Settlement settlement)
	{
		if (!MpHelper.IsMultiplayer())
		{
			return null;
		}
		if (settlement == null)
		{
			return null;
		}
		Faction faction = CaravanLeaveHelper.Me();
		Faction faction2 = settlement.Faction;
		if (faction == null || faction2 == null || faction2 == faction || !faction2.IsPlayer)
		{
			return null;
		}
		Ctx ctx = new Ctx();
		ctx.hideTutorial = true;
		int num = 0;
		bool flag = false;
		if (settlement.HasMap)
		{
			Map map = settlement.Map;
			num = CaravanLeaveHelper.CountOurColonists(map, faction);
			if (num > 0)
			{
				flag = CaravanLeaveHelper.VanillaReformAvailable(map);
				if (!flag)
				{
					ctx.reform = CaravanLeaveHelper.MakeReformGizmo(map, faction);
				}
			}
		}
		Caravan caravan = null;
		List<Caravan> caravans = Find.WorldObjects.Caravans;
		for (int i = 0; i < caravans.Count; i++)
		{
			Caravan caravan2 = caravans[i];
			if (caravan2 != null && caravan2.Faction == faction && caravan2.Tile == settlement.Tile)
			{
				caravan = caravan2;
				break;
			}
		}
		if (caravan != null)
		{
			Caravan sel = caravan;
			Command_Action command_Action = new Command_Action();
			command_Action.defaultLabel = "RAF_SelectCaravan".Translate();
			command_Action.defaultDesc = "[RaidAnimalFix] " + "RAF_SelectCaravanDesc".Translate(sel.Label);
			command_Action.icon = FormCaravanComp.FormCaravanCommand;
			command_Action.action = delegate
			{
				Find.WorldSelector.ClearSelection();
				Find.WorldSelector.Select(sel);
			};
			ctx.selectCaravan = command_Action;
		}
		Diag(settlement, faction, num, flag, caravan);
		return ctx;
	}

	private static void Diag(Settlement s, Faction me, int ours, bool vanilla, Caravan ourCaravan)
	{
		try
		{
			float realtimeSinceStartup = Time.realtimeSinceStartup;
			if (_lastDiagTime.TryGetValue(s.ID, out var value) && realtimeSinceStartup - value < 10f)
			{
				return;
			}
			_lastDiagTime[s.ID] = realtimeSinceStartup;
			string text = "n/a";
			string text2 = "none";
			if (s.HasMap)
			{
				Map map = s.Map;
				IAttackTarget threat;
				bool flag = GenHostility.AnyHostileActiveThreatTo(map, me, out threat);
				IAttackTarget threat2;
				bool flag2 = GenHostility.AnyHostileActiveThreatTo(map, me, out threat2, countDormantPawnsAsHostile: true, canBeFogged: true);
				text = "awakeVisible=" + flag + ((threat != null && threat.Thing != null) ? ("(" + threat.Thing.LabelShort + ")") : "") + " inclDormantFogged=" + flag2 + ((threat2 != null && threat2.Thing != null) ? ("(" + threat2.Thing.LabelShort + ")") : "");
				Faction foreignCaravanSessionFaction = MpSessionHelper.GetForeignCaravanSessionFaction(map, me);
				if (foreignCaravanSessionFaction != null)
				{
					text2 = foreignCaravanSessionFaction.Name;
				}
			}
			Log.Message("[RaidAnimalFix] Settlement '" + s.Label + "' owner=" + ((s.Faction != null) ? s.Faction.Name : "null") + " hasMap=" + s.HasMap + " ourColonists=" + ours + " vanillaReform=" + vanilla + " threats[" + text + "] foreignCaravanSession=" + text2 + " ourCaravanHere=" + (ourCaravan != null));
		}
		catch
		{
		}
	}
}
