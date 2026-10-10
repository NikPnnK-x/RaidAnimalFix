using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RaidAnimalFix;

[HarmonyPatch("GetGizmos")]
[HarmonyPatch(typeof(Caravan))]
public static class CaravanTradeRequestGizmoFix
{
	private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Caravan __instance)
	{
		string fulfillLabel = null;
		try
		{
			fulfillLabel = "CommandFulfillTradeOffer".Translate();
		}
		catch
		{
		}
		bool hasVanilla = false;
		foreach (Gizmo g in __result)
		{
			if (g is Command c && fulfillLabel != null && c.defaultLabel == fulfillLabel)
			{
				hasVanilla = true;
			}
			yield return g;
		}
		if (!hasVanilla)
		{
			Gizmo extra = null;
			try
			{
				extra = TryMake(__instance, fulfillLabel);
			}
			catch (Exception ex)
			{
				CaravanLeaveHelper.LogErrorThrottled("CaravanTradeRequestGizmoFix", ex);
			}
			if (extra != null)
			{
				yield return extra;
			}
		}
	}

	private static Gizmo TryMake(Caravan caravan, string label)
	{
		if (!MpHelper.IsMultiplayer())
		{
			return null;
		}
		if (caravan == null || !caravan.Spawned)
		{
			return null;
		}
		if (caravan.pather != null && caravan.pather.Moving)
		{
			return null;
		}
		Faction faction = CaravanLeaveHelper.Me();
		if (caravan.Faction != faction)
		{
			return null;
		}
		Settlement settlement = TradeRequestHelper.OtherPlayersSettlementAt(caravan.Tile, faction);
		if (settlement == null)
		{
			return null;
		}
		TradeRequestComp comp = TradeRequestHelper.ActiveRequest(settlement);
		if (comp == null)
		{
			return null;
		}
		string req = TradeRequestHelper.RequestLabel(comp);
		Command_Action command_Action = new Command_Action();
		command_Action.defaultLabel = label ?? ((string)"RAF_FulfillTradeRequest".Translate());
		command_Action.defaultDesc = "CommandFulfillTradeOfferDesc".Translate() + "\n\n[RaidAnimalFix] " + "RAF_SettlementAsks".Translate(settlement.Faction.Name, req);
		command_Action.icon = TradeRequestHelper.Icon();
		command_Action.action = delegate
		{
			if (!comp.ActiveRequest)
			{
				Messages.Message("RAF_RequestNoLongerActive".Translate(), MessageTypeDefOf.RejectInput, historical: false);
			}
			else if (!TradeRequestHelper.HasGoods(caravan, comp))
			{
				Messages.Message("CommandFulfillTradeOfferFailInsufficient".Translate(req), MessageTypeDefOf.RejectInput, historical: false);
			}
			else
			{
				TaggedString text = "CommandFulfillTradeOfferConfirm".Translate(GenLabel.ThingLabel(comp.requestThingDef, null, comp.requestCount));
				Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(text, delegate
				{
					TradeRequestHelper.Fulfill(comp, caravan);
				}));
			}
		};
		if (!TradeRequestHelper.HasGoods(caravan, comp))
		{
			command_Action.Disable("CommandFulfillTradeOfferFailInsufficient".Translate(req));
		}
		return command_Action;
	}
}
