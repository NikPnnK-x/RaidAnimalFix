using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RaidAnimalFix;

/// <summary>
/// Письма общего квеста участникам. Письма квеста создаются в контексте
/// владельца, и MP (LetterStackReceiveOnlyMyFaction) / LetterFilterFix2
/// убирают их у всех остальных. Стек писем у каждого клиента свой, поэтому
/// возврат письма в стек участника — чисто UI.
/// Связь письмо→квест — ChoiceLetter.quest (так шлют QuestPart_Letter,
/// QuestPart_DropPods и большинство частей 1.6). Возвращаются только
/// StandardLetter: у писем с выбором (ChoiceLetter_*) кнопки меняют игру,
/// участник не должен их нажимать за владельца. Возвращаются только письма,
/// созданные в контексте владельца (или наблюдателя — тик мира), чтобы
/// письмо «ваша доля прибыла» одного участника не попадало другому.
/// </summary>
[HarmonyPatch(typeof(LetterStack), "ReceiveLetter", new Type[]
{
	typeof(Letter),
	typeof(string),
	typeof(int),
	typeof(bool)
})]
public static class SharedQuestLetterDelivery
{
	[HarmonyPriority(Priority.Last)]
	private static void Postfix(LetterStack __instance, Letter let, int delayTicks, bool playSound)
	{
		if (delayTicks > 0 || __instance == null || !(let is ChoiceLetter choice) || choice.quest == null || !MpHelper.IsMultiplayer())
		{
			return;
		}
		if (let.GetType() != typeof(StandardLetter))
		{
			return;
		}
		try
		{
			RafSharedQuestData.Entry entry = SharedQuestUtil.EntryFor(choice.quest);
			if (entry == null || entry.participants.Count == 0)
			{
				return;
			}
			List<Letter> letters = __instance.LettersListForReading;
			if (letters.Contains(let) || !let.CanShowInLetterStack)
			{
				return;
			}
			Faction me = MpHelper.GetCurrentPlayerFaction();
			if (me == null || MpContext.IsSpectator(me) || !entry.IsParticipant(me))
			{
				return;
			}
			Faction ctx = Faction.OfPlayer;
			if (ctx != null && ctx.loadID != entry.ownerId && !MpContext.IsSpectator(ctx))
			{
				return;
			}
			letters.Add(let);
			if (!playSound && !let.def.arriveSound.NullOrUndefined())
			{
				let.def.arriveSound.PlayOneShotOnCamera();
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("SharedQuestLetterDelivery", ex);
		}
	}
}

/// <summary>
/// Показывает приглашённым и участникам чужой квест во вкладке, даже если в
/// MP включено «скрывать квесты других игроков» (MainTabWindow_QuestsShouldListNowPatch).
/// Повторяет ванильные условия вкладок, чтобы не вытащить квест не в ту вкладку.
/// </summary>
[HarmonyPatch(typeof(MainTabWindow_Quests), "ShouldListNow")]
public static class SharedQuestListVisibility
{
	private static readonly AccessTools.FieldRef<MainTabWindow_Quests, bool> ShowAllRef = AccessTools.FieldRefAccess<MainTabWindow_Quests, bool>("showAll");

	private static System.Reflection.FieldInfo _curTab;

	[HarmonyPriority(Priority.Last)]
	private static void Postfix(MainTabWindow_Quests __instance, Quest quest, ref bool __result)
	{
		if (__result || quest == null || !MpHelper.IsMultiplayer())
		{
			return;
		}
		try
		{
			RafSharedQuestData data = RafSharedQuestData.Get();
			RafSharedQuestData.Entry entry = data?.Find(quest.id);
			if (entry == null)
			{
				return;
			}
			Faction me = MpHelper.GetCurrentPlayerFaction();
			if (!entry.IsParticipant(me) && !entry.IsInvited(me))
			{
				return;
			}
			if (quest.hidden && !ShowAllRef(__instance))
			{
				return;
			}
			if (_curTab == null)
			{
				_curTab = AccessTools.Field(typeof(MainTabWindow_Quests), "curTab");
			}
			int tab = Convert.ToInt32(_curTab.GetValue(__instance));
			switch (tab)
			{
			case 0:
				__result = quest.State == QuestState.NotYetAccepted && !quest.dismissed && !quest.hiddenInUI;
				break;
			case 1:
				__result = quest.State == QuestState.Ongoing && !quest.dismissed && !quest.hiddenInUI;
				break;
			case 2:
				__result = !quest.hiddenInUI && (quest.Historical || quest.dismissed);
				break;
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("SharedQuestListVisibility", ex);
		}
	}
}

/// <summary>
/// Блок «общий квест» в правой панели вкладки квестов: под кнопкой принятия
/// и сроками (DoOutcomeInfo вызывается сразу после DoRightAlignedInfo, внутри
/// прокрутки, curY уже ниже правых надписей). Владельцу — «Пригласить
/// союзника» и выбор доли; приглашённому — «Вступить / Отказаться»;
/// участнику — «Выйти». Любое изменение — только через SharedQuestSync.
/// </summary>
[HarmonyPatch(typeof(MainTabWindow_Quests), "DoOutcomeInfo")]
public static class SharedQuestTabUi
{
	private static readonly AccessTools.FieldRef<MainTabWindow_Quests, Quest> SelectedRef = AccessTools.FieldRefAccess<MainTabWindow_Quests, Quest>("selected");

	private const float ButtonHeight = 30f;

	private static void Postfix(MainTabWindow_Quests __instance, Rect innerRect, ref float curY)
	{
		if (!MpHelper.IsMultiplayer())
		{
			return;
		}
		try
		{
			Quest quest = SelectedRef(__instance);
			Faction me = MpHelper.GetCurrentPlayerFaction();
			if (quest == null || me == null || MpContext.IsSpectator(me))
			{
				return;
			}
			RafSharedQuestData.Entry entry = RafSharedQuestData.Get()?.Find(quest.id);
			Faction owner = (entry != null) ? SharedQuestUtil.FactionById(entry.ownerId) : SharedQuestUtil.OwnerOf(quest);
			bool ongoing = quest.State == QuestState.Ongoing;
			Text.Font = GameFont.Small;
			if (owner == me)
			{
				if (ongoing)
				{
					DrawOwner(innerRect, ref curY, quest, me, entry);
				}
				else if (entry != null && entry.participants.Count > 0)
				{
					Line(innerRect, ref curY, "RAF_SharedQuests_Participants".Translate(SharedQuestUtil.FactionNames(entry.participants)));
				}
			}
			else if (entry != null && (entry.IsInvited(me) || entry.IsParticipant(me)))
			{
				DrawMember(innerRect, ref curY, quest, me, owner, entry, ongoing);
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("SharedQuestTabUi", ex);
		}
		finally
		{
			GUI.color = Color.white;
			Text.Anchor = TextAnchor.UpperLeft;
		}
	}

	private static void DrawOwner(Rect innerRect, ref float curY, Quest quest, Faction me, RafSharedQuestData.Entry entry)
	{
		curY += 6f;
		int participants = entry?.participants.Count ?? 0;
		Rect inviteRect = new Rect(innerRect.x, curY, 170f, ButtonHeight);
		if (Widgets.ButtonText(inviteRect, "RAF_SharedQuests_InviteAlly".Translate()))
		{
			List<FloatMenuOption> options = new List<FloatMenuOption>();
			List<Faction> candidates = SharedQuestUtil.Candidates(me, entry);
			int questId = quest.id;
			for (int i = 0; i < candidates.Count; i++)
			{
				Faction f = candidates[i];
				options.Add(new FloatMenuOption(f.Name, delegate
				{
					SharedQuestSync.InviteToQuest(questId, me, f);
				}));
			}
			if (options.Count == 0)
			{
				string key = SharedQuestUtil.DiplomacyPresent ? "RAF_SharedQuests_NoAllies" : "RAF_SharedQuests_NoCandidates";
				options.Add(new FloatMenuOption(key.Translate(), null));
			}
			Find.WindowStack.Add(new FloatMenu(options));
		}
		TooltipHandler.TipRegion(inviteRect, "RAF_SharedQuests_InviteAllyDesc".Translate());
		Rect shareRect = new Rect(inviteRect.xMax + 10f, curY, 230f, ButtonHeight);
		int current = entry?.sharePercent ?? 0;
		if (Widgets.ButtonText(shareRect, "RAF_SharedQuests_Share".Translate(SharedQuestUtil.ShareLabel(current, participants))))
		{
			List<FloatMenuOption> options2 = new List<FloatMenuOption>();
			int questId2 = quest.id;
			for (int j = 0; j < SharedQuestUtil.ShareOptions.Length; j++)
			{
				int pct = SharedQuestUtil.ShareOptions[j];
				options2.Add(new FloatMenuOption(SharedQuestUtil.ShareLabel(pct, participants), delegate
				{
					SharedQuestSync.SetSharePercent(questId2, me, pct);
				}));
			}
			Find.WindowStack.Add(new FloatMenu(options2));
		}
		TooltipHandler.TipRegion(shareRect, "RAF_SharedQuests_ShareDesc".Translate());
		curY += ButtonHeight + 4f;
		if (entry == null)
		{
			return;
		}
		if (entry.participants.Count > 0 || entry.invited.Count > 0)
		{
			Rect kickRect = new Rect(innerRect.x, curY, 170f, ButtonHeight);
			if (Widgets.ButtonText(kickRect, "RAF_SharedQuests_Remove".Translate()))
			{
				List<FloatMenuOption> options3 = new List<FloatMenuOption>();
				int questId3 = quest.id;
				List<int> ids = new List<int>(entry.participants);
				ids.AddRange(entry.invited);
				for (int k = 0; k < ids.Count; k++)
				{
					Faction f2 = SharedQuestUtil.FactionById(ids[k]);
					if (f2 != null)
					{
						options3.Add(new FloatMenuOption(f2.Name, delegate
						{
							SharedQuestSync.LeaveQuest(questId3, f2);
						}));
					}
				}
				if (options3.Count > 0)
				{
					Find.WindowStack.Add(new FloatMenu(options3));
				}
			}
			curY += ButtonHeight + 4f;
		}
		if (entry.participants.Count > 0)
		{
			Line(innerRect, ref curY, "RAF_SharedQuests_Participants".Translate(SharedQuestUtil.FactionNames(entry.participants)));
		}
		if (entry.invited.Count > 0)
		{
			Line(innerRect, ref curY, "RAF_SharedQuests_Invited".Translate(SharedQuestUtil.FactionNames(entry.invited)));
		}
	}

	private static void DrawMember(Rect innerRect, ref float curY, Quest quest, Faction me, Faction owner, RafSharedQuestData.Entry entry, bool ongoing)
	{
		curY += 6f;
		string ownerName = owner?.Name ?? "?";
		int questId = quest.id;
		if (entry.IsInvited(me))
		{
			Line(innerRect, ref curY, "RAF_SharedQuests_InvitedYou".Translate(ownerName));
			if (!ongoing)
			{
				return;
			}
			Rect joinRect = new Rect(innerRect.x, curY, 140f, ButtonHeight);
			if (Widgets.ButtonText(joinRect, "RAF_SharedQuests_Join".Translate()))
			{
				SharedQuestSync.RespondToInvite(questId, me, accept: true);
			}
			Rect declineRect = new Rect(joinRect.xMax + 10f, curY, 140f, ButtonHeight);
			if (Widgets.ButtonText(declineRect, "RAF_SharedQuests_Decline".Translate()))
			{
				SharedQuestSync.RespondToInvite(questId, me, accept: false);
			}
			curY += ButtonHeight + 4f;
			return;
		}
		int n = entry.participants.Count;
		int pct = SharedQuestUtil.EffectivePercent(entry, n);
		Line(innerRect, ref curY, "RAF_SharedQuests_YouParticipate".Translate(ownerName, pct));
		Line(innerRect, ref curY, "RAF_SharedQuests_Participants".Translate(SharedQuestUtil.FactionNames(entry.participants)));
		if (ongoing)
		{
			Rect leaveRect = new Rect(innerRect.x, curY, 170f, ButtonHeight);
			if (Widgets.ButtonText(leaveRect, "RAF_SharedQuests_Leave".Translate()))
			{
				SharedQuestSync.LeaveQuest(questId, me);
			}
			curY += ButtonHeight + 4f;
		}
	}

	private static void Line(Rect innerRect, ref float curY, string text)
	{
		float h = Text.CalcHeight(text, innerRect.width);
		Widgets.Label(new Rect(innerRect.x, curY, innerRect.width, h), text);
		curY += h;
	}
}
