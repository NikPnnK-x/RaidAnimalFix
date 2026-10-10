using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RaidAnimalFix;

/// <summary>
/// Дележ предметной награды общего квеста. Выполняется в симуляции — внутри
/// Notify_QuestSignalReceived части награды, у всех клиентов одинаково:
/// предметы перебираются по thingIDNumber, количества считаются целыми
/// числами, участники — в порядке вступления, карты — в порядке Find.Maps.
/// Доля владельца остаётся в списке части и доставляется ванильным путём
/// (его письмо, его точка сброса); доли участников отделяются SplitOff и
/// сбрасываются капсулами на их домашние карты.
/// Делятся только части, порождённые наградой (входят в
/// QuestPart_Choice.Choice.questParts) — служебные капсулы квеста
/// (беженцы, квестовые предметы) не трогаются. Пешки не делятся.
/// Благосклонность/почёт/прочие нематериальные награды остаются владельцу.
/// </summary>
internal static class SharedQuestRewards
{
	private class Share
	{
		public Faction faction;

		public Map map;

		public List<Thing> things = new List<Thing>();
	}

	public static bool IsRewardPart(Quest quest, QuestPart part)
	{
		if (quest == null || part == null)
		{
			return false;
		}
		List<QuestPart> parts = quest.PartsListForReading;
		for (int i = 0; i < parts.Count; i++)
		{
			if (!(parts[i] is QuestPart_Choice choice) || choice.choices == null)
			{
				continue;
			}
			for (int j = 0; j < choice.choices.Count; j++)
			{
				if (choice.choices[j]?.questParts != null && choice.choices[j].questParts.Contains(part))
				{
					return true;
				}
			}
		}
		return false;
	}

	/// <summary>
	/// Отделяет доли участников от items (и от thingDefs, если не null) и
	/// доставляет их. Возвращает true, если что-то было отделено.
	/// </summary>
	public static bool SplitAndDeliver(Quest quest, QuestPart part, List<Thing> items, List<ThingDefCountClass> thingDefs)
	{
		RafSharedQuestData.Entry entry = SharedQuestUtil.EntryFor(quest);
		if (entry == null || entry.participants.Count == 0 || items == null)
		{
			return false;
		}
		if (!IsRewardPart(quest, part))
		{
			return false;
		}
		Faction owner = SharedQuestUtil.FactionById(entry.ownerId);
		List<Share> shares = new List<Share>();
		for (int i = 0; i < entry.participants.Count; i++)
		{
			Faction f = SharedQuestUtil.FactionById(entry.participants[i]);
			if (f == null || f == owner || f.defeated)
			{
				continue;
			}
			Map map = SharedQuestUtil.HomeMapOf(f);
			if (map == null)
			{
				continue;
			}
			shares.Add(new Share
			{
				faction = f,
				map = map
			});
		}
		int n = shares.Count;
		if (n == 0)
		{
			return false;
		}
		bool equal = entry.sharePercent <= 0;
		int pct = SharedQuestUtil.EffectivePercent(entry, n);
		List<Thing> sorted = new List<Thing>();
		for (int k = 0; k < items.Count; k++)
		{
			Thing t = items[k];
			if (t != null && !(t is Pawn) && !t.Destroyed && !t.Spawned)
			{
				sorted.Add(t);
			}
		}
		sorted.Sort((Thing a, Thing b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
		int singleIndex = 0;
		for (int m = 0; m < sorted.Count; m++)
		{
			Thing thing = sorted[m];
			int count = thing.stackCount;
			if (count <= 1)
			{
				// Неделимые предметы при равном дележе раздаются по кругу:
				// 0-й — владельцу, 1-й — первому участнику, …
				if (equal)
				{
					int r = singleIndex % (n + 1);
					singleIndex++;
					if (r > 0)
					{
						items.Remove(thing);
						shares[r - 1].things.Add(thing);
					}
				}
				continue;
			}
			int amount = equal ? (count / (n + 1)) : (count * pct / 100);
			if (amount <= 0)
			{
				continue;
			}
			for (int s = 0; s < n; s++)
			{
				if (thing.stackCount <= 0 || items.IndexOf(thing) < 0)
				{
					break;
				}
				if (amount >= thing.stackCount)
				{
					items.Remove(thing);
					shares[s].things.Add(thing);
					break;
				}
				shares[s].things.Add(thing.SplitOff(amount));
			}
		}
		if (thingDefs != null)
		{
			for (int d = 0; d < thingDefs.Count; d++)
			{
				ThingDefCountClass tdc = thingDefs[d];
				if (tdc?.thingDef == null || tdc.count <= 1)
				{
					continue;
				}
				int count2 = tdc.count;
				int amount2 = equal ? (count2 / (n + 1)) : (count2 * pct / 100);
				if (amount2 <= 0)
				{
					continue;
				}
				for (int s2 = 0; s2 < n && tdc.count > amount2; s2++)
				{
					Thing made = ThingMaker.MakeThing(tdc.thingDef, GenStuff.RandomStuffByCommonalityFor(tdc.thingDef));
					made.stackCount = amount2;
					shares[s2].things.Add(made);
					tdc.count -= amount2;
				}
			}
		}
		bool any = false;
		for (int q = 0; q < shares.Count; q++)
		{
			if (shares[q].things.Count > 0)
			{
				Deliver(quest, owner, shares[q]);
				any = true;
			}
		}
		return any;
	}

	/// <summary>
	/// Сброс капсулами на домашнюю карту участника. На время вызова контекст
	/// фракции переключается на участника (как KidnapOwnerContextFix): данные
	/// карты MP (haulables и т. п.) и письмо относятся к нему, MP оставит
	/// письмо только у его игроков.
	/// </summary>
	private static void Deliver(Quest quest, Faction owner, Share share)
	{
		bool pushed = false;
		try
		{
			if (MpHelper.IsMultiplayer() && MpSim.CanRestoreContext && share.faction != Faction.OfPlayer)
			{
				MpSim.PushFaction(share.map, share.faction);
				pushed = true;
			}
			Map map = share.map;
			IntVec3 spot = DropCellFinder.TradeDropSpot(map);
			string label = GenLabel.ThingsLabel(share.things);
			DropPodUtility.DropThingsNear(spot, map, share.things, 110, canInstaDropDuringInit: false, leaveSlag: false, canRoofPunch: true, forbid: false);
			Find.LetterStack.ReceiveLetter("RAF_SharedQuests_RewardLetterLabel".Translate(), "RAF_SharedQuests_RewardLetterText".Translate(quest.name, owner?.Name ?? "?", label), LetterDefOf.PositiveEvent, new TargetInfo(spot, map), null, quest);
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("SharedQuests.Deliver", ex);
		}
		finally
		{
			if (pushed)
			{
				try
				{
					MpSim.PopFaction(share.map);
				}
				catch (Exception ex2)
				{
					CaravanLeaveHelper.LogErrorThrottled("SharedQuests.Deliver.pop", ex2);
				}
			}
		}
	}
}

/// <summary>
/// Награда капсулами (Reward_Items без каравана). Дележ — до ванили и только
/// если ваниль действительно доставит (та же проверка карты, что в
/// QuestPart_DropPods.Notify_QuestSignalReceived), иначе участники получили
/// бы долю, а владелец — ничего.
/// </summary>
[HarmonyPatch(typeof(QuestPart_DropPods), "Notify_QuestSignalReceived")]
public static class SharedQuestDropPodsSplit
{
	private static readonly AccessTools.FieldRef<QuestPart_DropPods, List<Thing>> ItemsRef = AccessTools.FieldRefAccess<QuestPart_DropPods, List<Thing>>("items");

	private static void Prefix(QuestPart_DropPods __instance, Signal signal)
	{
		try
		{
			if (__instance?.quest == null || signal.tag != __instance.inSignal || SharedQuestUtil.EntryFor(__instance.quest) == null)
			{
				return;
			}
			if (!WillDeliver(__instance))
			{
				return;
			}
			SharedQuestRewards.SplitAndDeliver(__instance.quest, __instance, ItemsRef(__instance), __instance.thingDefs);
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("SharedQuestDropPodsSplit", ex);
		}
	}

	private static bool WillDeliver(QuestPart_DropPods part)
	{
		MapParent mp = part.mapParent;
		if (mp != null && mp.HasMap && part.quest.IsParentSuitableForQuest(mp))
		{
			return true;
		}
		mp = part.quest.TryFindNewSuitablePlayerMapParentForRetarget(!part.canRetargetAnyMap);
		return mp != null && mp.HasMap;
	}
}

/// <summary>
/// Награда караваном (Reward_Items с giveToCaravan). Владельческая доля
/// уходит в караван ванилью; доли участников — капсулами домой.
/// Дележ только если ваниль найдёт караван (поле или аргумент CARAVAN).
/// </summary>
[HarmonyPatch(typeof(QuestPart_GiveToCaravan), "Notify_QuestSignalReceived")]
public static class SharedQuestGiveToCaravanSplit
{
	private static readonly AccessTools.FieldRef<QuestPart_GiveToCaravan, List<Thing>> ItemsRef = AccessTools.FieldRefAccess<QuestPart_GiveToCaravan, List<Thing>>("items");

	private static void Prefix(QuestPart_GiveToCaravan __instance, Signal signal)
	{
		try
		{
			if (__instance?.quest == null || signal.tag != __instance.inSignal || SharedQuestUtil.EntryFor(__instance.quest) == null)
			{
				return;
			}
			Caravan caravan = __instance.caravan;
			if (caravan == null)
			{
				signal.args.TryGetArg("CARAVAN", out caravan);
			}
			if (caravan == null)
			{
				return;
			}
			SharedQuestRewards.SplitAndDeliver(__instance.quest, __instance, ItemsRef(__instance), null);
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("SharedQuestGiveToCaravanSplit", ex);
		}
	}
}
