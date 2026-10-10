using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RaidAnimalFix;

public static class MapTradeRequestFulfill
{
	private static bool _syncTried;

	private static bool _syncRegistered;

	public static void EnsureSyncRegistered()
	{
		if (_syncTried)
		{
			return;
		}
		_syncTried = true;
		try
		{
			Type type = AccessTools.TypeByName("Multiplayer.API.MP");
			if (type == null)
			{
				Log.Message("[RaidAnimalFix] Multiplayer API not found — map trade-request fulfill disabled in MP");
				return;
			}
			FieldInfo field = type.GetField("enabled", BindingFlags.Static | BindingFlags.Public);
			if (field == null || !(bool)field.GetValue(null))
			{
				Log.Message("[RaidAnimalFix] Multiplayer API present but not enabled");
				return;
			}
			Type type2 = AccessTools.TypeByName("Multiplayer.API.SyncType");
			if (!(type2 == null))
			{
				MethodInfo method = type.GetMethod("RegisterSyncMethod", BindingFlags.Static | BindingFlags.Public, null, new Type[2]
				{
					typeof(MethodInfo),
					type2.MakeArrayType()
				}, null);
				if (method == null)
				{
					Log.Warning("[RaidAnimalFix] MP.RegisterSyncMethod(MethodInfo, SyncType[]) not found");
					return;
				}
				MethodInfo methodInfo = AccessTools.Method(typeof(MapTradeRequestFulfill), "FulfillFromMap");
				method.Invoke(null, new object[2] { methodInfo, null });
				_syncRegistered = true;
				Log.Message("[RaidAnimalFix] MP sync method registered: MapTradeRequestFulfill.FulfillFromMap");
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] MP sync registration failed: " + ex);
		}
	}

	public static bool CanIssue()
	{
		if (MpHelper.IsMultiplayer())
		{
			return _syncRegistered;
		}
		return true;
	}

	public static bool PlayerCanGive(Thing t)
	{
		if (t == null)
		{
			return false;
		}
		if (t.GetRotStage() != 0)
		{
			return false;
		}
		if (t is Apparel apparel && apparel.WornByCorpse)
		{
			return false;
		}
		CompQuality compQuality = t.TryGetComp<CompQuality>();
		if (compQuality != null)
		{
			return (int)compQuality.Quality >= 2;
		}
		return true;
	}

	public static Settlement RequestSettlementOf(Map map, Faction me, out TradeRequestComp comp)
	{
		comp = null;
		if (map == null)
		{
			return null;
		}
		if (!(map.Parent is Settlement settlement) || settlement.Faction == null || settlement.Faction == me)
		{
			return null;
		}
		if (!MpContext.IsRealPlayerFaction(settlement.Faction))
		{
			return null;
		}
		comp = TradeRequestHelper.ActiveRequest(settlement);
		if (comp == null)
		{
			return null;
		}
		return settlement;
	}

	public static void CollectCandidates(Map map, Faction me, ThingDef def, List<Thing> outThings)
	{
		if (map == null || def == null)
		{
			return;
		}
		IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
		for (int i = 0; i < allPawnsSpawned.Count; i++)
		{
			Pawn pawn = allPawnsSpawned[i];
			if (pawn == null || pawn.Faction != me)
			{
				continue;
			}
			if (pawn.carryTracker != null)
			{
				Thing carriedThing = pawn.carryTracker.CarriedThing;
				if (carriedThing != null && carriedThing.def == def && PlayerCanGive(carriedThing))
				{
					outThings.Add(carriedThing);
				}
			}
			if (pawn.inventory == null)
			{
				continue;
			}
			ThingOwner innerContainer = pawn.inventory.innerContainer;
			for (int j = 0; j < innerContainer.Count; j++)
			{
				Thing thing = innerContainer[j];
				if (thing != null && thing.def == def && PlayerCanGive(thing))
				{
					outThings.Add(thing);
				}
			}
		}
		List<Thing> list = map.listerThings.ThingsOfDef(def);
		for (int k = 0; k < list.Count; k++)
		{
			Thing thing2 = list[k];
			if (thing2 != null && thing2.Spawned && !thing2.Destroyed && !thing2.IsInAnyStorage() && !thing2.Position.Fogged(map) && PlayerCanGive(thing2) && !outThings.Contains(thing2))
			{
				outThings.Add(thing2);
			}
		}
	}

	public static int CountAvailable(Map map, Faction me, TradeRequestComp comp)
	{
		List<Thing> list = new List<Thing>();
		CollectCandidates(map, me, comp.requestThingDef, list);
		int num = 0;
		for (int i = 0; i < list.Count; i++)
		{
			num += list[i].stackCount;
		}
		return num;
	}

	private static Pawn AnchorPawn(Map map, Faction me)
	{
		IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
		Pawn pawn = null;
		for (int i = 0; i < allPawnsSpawned.Count; i++)
		{
			Pawn pawn2 = allPawnsSpawned[i];
			if (pawn2 != null && pawn2.Faction == me && !pawn2.Dead)
			{
				if (pawn2.RaceProps.Humanlike)
				{
					return pawn2;
				}
				if (pawn == null)
				{
					pawn = pawn2;
				}
			}
		}
		return pawn;
	}

	private static List<QuestPart_GiveToCaravan> RewardParts(Settlement s)
	{
		List<QuestPart_GiveToCaravan> list = new List<QuestPart_GiveToCaravan>();
		if (s.questTags == null || s.questTags.Count == 0)
		{
			return list;
		}
		List<Quest> questsListForReading = Find.QuestManager.QuestsListForReading;
		for (int i = 0; i < questsListForReading.Count; i++)
		{
			Quest quest = questsListForReading[i];
			if (quest == null || quest.State != QuestState.Ongoing)
			{
				continue;
			}
			List<QuestPart> partsListForReading = quest.PartsListForReading;
			for (int j = 0; j < partsListForReading.Count; j++)
			{
				if (!(partsListForReading[j] is QuestPart_GiveToCaravan questPart_GiveToCaravan) || questPart_GiveToCaravan.inSignal == null)
				{
					continue;
				}
				for (int k = 0; k < s.questTags.Count; k++)
				{
					if (questPart_GiveToCaravan.inSignal == s.questTags[k] + ".TradeRequestFulfilled")
					{
						list.Add(questPart_GiveToCaravan);
						break;
					}
				}
			}
		}
		return list;
	}

	private static List<Thing> TakeRewards(List<QuestPart_GiveToCaravan> parts)
	{
		List<Thing> list = new List<Thing>();
		for (int i = 0; i < parts.Count; i++)
		{
			QuestPart_GiveToCaravan questPart_GiveToCaravan = parts[i];
			foreach (Thing thing in questPart_GiveToCaravan.Things)
			{
				if (thing != null && !thing.Destroyed)
				{
					list.Add(thing);
				}
			}
			questPart_GiveToCaravan.Things = null;
		}
		return list;
	}

	private static void DeliverRewards(List<Thing> things, Map map, Pawn anchor, Faction me)
	{
		if (things.Count == 0)
		{
			return;
		}
		IntVec3 intVec = ((anchor != null && anchor.Spawned) ? anchor.Position : map.Center);
		List<Thing> list = new List<Thing>();
		for (int i = 0; i < things.Count; i++)
		{
			Thing thing = things[i];
			if (thing == null || thing.Destroyed)
			{
				continue;
			}
			if (thing is Pawn pawn)
			{
				if (pawn.Faction != me)
				{
					pawn.SetFaction(me);
				}
				IntVec3 loc = CellFinder.RandomClosewalkCellNear(intVec, map, 4);
				GenSpawn.Spawn(pawn, loc, map);
				list.Add(pawn);
			}
			else
			{
				if (thing.holdingOwner != null)
				{
					thing.holdingOwner.Remove(thing);
				}
				if (GenPlace.TryPlaceThing(thing, intVec, map, ThingPlaceMode.Near, out var lastResultingThing, null, null, default(Rot4)))
				{
					list.Add(lastResultingThing ?? thing);
				}
				else
				{
					Log.Warning("[RaidAnimalFix] could not place quest reward " + thing.LabelCap);
				}
			}
		}
		if (list.Count > 0)
		{
			Messages.Message("RAF_RewardDelivered".Translate(), new LookTargets(list), MessageTypeDefOf.PositiveEvent, historical: false);
		}
		Log.Message("[RaidAnimalFix] Trade request rewards placed: " + list.Count + " / " + things.Count);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void FulfillFromMap(Settlement settlement, Pawn anchor)
	{
		try
		{
			if (settlement == null)
			{
				return;
			}
			TradeRequestComp tradeRequestComp = TradeRequestHelper.ActiveRequest(settlement);
			if (tradeRequestComp == null)
			{
				return;
			}
			Map map = settlement.Map;
			if (map == null)
			{
				return;
			}
			Faction ofPlayer = Faction.OfPlayer;
			if (ofPlayer == null || ofPlayer == settlement.Faction)
			{
				return;
			}
			List<Thing> list = new List<Thing>();
			CollectCandidates(map, ofPlayer, tradeRequestComp.requestThingDef, list);
			int num = 0;
			for (int i = 0; i < list.Count; i++)
			{
				num += list[i].stackCount;
			}
			if (num < tradeRequestComp.requestCount)
			{
				Messages.Message("CommandFulfillTradeOfferFailInsufficient".Translate(TradeRequestHelper.RequestLabel(tradeRequestComp)), MessageTypeDefOf.RejectInput, historical: false);
				return;
			}
			if (anchor == null || anchor.Destroyed || anchor.Map != map || anchor.Faction != ofPlayer)
			{
				anchor = AnchorPawn(map, ofPlayer);
			}
			int num2 = tradeRequestComp.requestCount;
			for (int j = 0; j < list.Count; j++)
			{
				if (num2 <= 0)
				{
					break;
				}
				Thing thing = list[j];
				if (thing != null && !thing.Destroyed)
				{
					int num3 = Math.Min(num2, thing.stackCount);
					Thing thing2 = thing.SplitOff(num3);
					num2 -= num3;
					if (thing2 != null && !thing2.Destroyed)
					{
						thing2.Destroy();
					}
				}
			}
			if (settlement.Faction != null)
			{
				ofPlayer.TryAffectGoodwillWith(settlement.Faction, 12, canSendMessage: true, canSendHostilityLetter: true, HistoryEventDefOf.QuestGoodwillReward);
			}
			List<Thing> things = TakeRewards(RewardParts(settlement));
			if (settlement.questTags != null)
			{
				QuestUtility.SendQuestTargetSignals(settlement.questTags, "TradeRequestFulfilled", settlement.Named("SUBJECT"));
			}
			DeliverRewards(things, map, anchor, ofPlayer);
			tradeRequestComp.Disable();
			Log.Message("[RaidAnimalFix] Trade request fulfilled from map: " + settlement.Label + " by " + ofPlayer.Name);
		}
		catch (Exception ex)
		{
			Log.Error("[RaidAnimalFix] FulfillFromMap: " + ex);
		}
	}

	public static Gizmo MakeGizmo(Settlement s, TradeRequestComp comp, Map map, Faction me)
	{
		string req = TradeRequestHelper.RequestLabel(comp);
		int num = CountAvailable(map, me, comp);
		Command_Action command_Action = new Command_Action();
		string text = null;
		try
		{
			text = "CommandFulfillTradeOffer".Translate();
		}
		catch
		{
		}
		command_Action.defaultLabel = text ?? ((string)"RAF_FulfillTradeRequest".Translate());
		command_Action.defaultDesc = "CommandFulfillTradeOfferDesc".Translate() + "\n\n[RaidAnimalFix] " + "RAF_SettlementAsksMap".Translate(s.Faction.Name, req, num, comp.requestCount);
		command_Action.icon = TradeRequestHelper.Icon();
		command_Action.action = delegate
		{
			if (!comp.ActiveRequest)
			{
				Messages.Message("RAF_RequestNoLongerActive".Translate(), MessageTypeDefOf.RejectInput, historical: false);
			}
			else if (CountAvailable(map, me, comp) < comp.requestCount)
			{
				Messages.Message("CommandFulfillTradeOfferFailInsufficient".Translate(req), MessageTypeDefOf.RejectInput, historical: false);
			}
			else
			{
				Pawn anchor = AnchorPawn(map, me);
				if (anchor == null)
				{
					Messages.Message("RAF_NoPawnsOnMap".Translate(), MessageTypeDefOf.RejectInput, historical: false);
				}
				else
				{
					TaggedString text2 = "CommandFulfillTradeOfferConfirm".Translate(GenLabel.ThingLabel(comp.requestThingDef, null, comp.requestCount));
					Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(text2, delegate
					{
						FulfillFromMap(s, anchor);
					}));
				}
			}
		};
		if (MpHelper.IsMultiplayer())
		{
			command_Action.Disable("[RaidAnimalFix] " + "RAF_MapFulfillDisabledMp".Translate());
		}
		else if (!CanIssue())
		{
			command_Action.Disable("[RaidAnimalFix] " + "RAF_MapFulfillNoApi".Translate());
		}
		else if (num < comp.requestCount)
		{
			command_Action.Disable((string)("CommandFulfillTradeOfferFailInsufficient".Translate(req) + " (") + num + " / " + comp.requestCount + ")");
		}
		return command_Action;
	}
}
