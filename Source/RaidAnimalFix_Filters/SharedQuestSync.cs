using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RaidAnimalFix;

/// <summary>
/// Общие помощники общих квестов. Всё, что вызывается из симуляции, читает
/// только синхронное состояние (квесты, фракции, карты в порядке Find.Maps).
/// RealPlayerFaction читается лишь в UI-функциях (помечены).
/// </summary>
internal static class SharedQuestUtil
{
	/// <summary>Варианты доли участника в окне квеста; 0 = поровну.</summary>
	public static readonly int[] ShareOptions = new int[6] { 0, 10, 20, 25, 33, 50 };

	public static Quest QuestById(int id)
	{
		QuestManager qm = Find.QuestManager;
		if (qm == null)
		{
			return null;
		}
		List<Quest> list = qm.QuestsListForReading;
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i] != null && list[i].id == id)
			{
				return list[i];
			}
		}
		return null;
	}

	public static Faction FactionById(int loadId)
	{
		FactionManager fm = Find.FactionManager;
		if (fm == null || loadId < 0)
		{
			return null;
		}
		List<Faction> list = fm.AllFactionsListForReading;
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i].loadID == loadId)
			{
				return list[i];
			}
		}
		return null;
	}

	/// <summary>
	/// Владелец квеста: фракция принявшей пешки, иначе — как
	/// MP FactionExtensions.TryGetPlayerFaction (фракция поселения игрока среди
	/// целей квеста). Детерминировано — годится и для проверки в sync-команде.
	/// </summary>
	public static Faction OwnerOf(Quest quest)
	{
		if (quest == null)
		{
			return null;
		}
		Faction f = quest.AccepterPawn?.Faction;
		if (MpContext.IsRealPlayerFaction(f))
		{
			return f;
		}
		try
		{
			foreach (GlobalTargetInfo t in quest.QuestLookTargets)
			{
				if (t.HasWorldObject && t.WorldObject is Settlement s && MpContext.IsRealPlayerFaction(s.Faction))
				{
					return s.Faction;
				}
			}
		}
		catch
		{
		}
		return null;
	}

	/// <summary>Запись общего квеста для самого квеста или его родителя (подквесты).</summary>
	public static RafSharedQuestData.Entry EntryFor(Quest quest)
	{
		if (quest == null)
		{
			return null;
		}
		RafSharedQuestData data = RafSharedQuestData.Get();
		if (data == null)
		{
			return null;
		}
		return data.Find(quest.id) ?? ((quest.parent != null) ? data.Find(quest.parent.id) : null);
	}

	private static bool _relationsResolved;

	private static MethodInfo _areAllied;

	/// <summary>
	/// Дипломатия (RaidAnimalFix.RelationsApi.AreAllied) — если фича есть в
	/// сборке, приглашать можно только союзников; иначе — любую фракцию игрока.
	/// Состояние союзов синхронное, поэтому проверка допустима и в симуляции.
	/// </summary>
	public static bool CanInvite(Faction owner, Faction invitee)
	{
		if (owner == null || invitee == null || owner == invitee)
		{
			return false;
		}
		if (!MpContext.IsRealPlayerFaction(owner) || !MpContext.IsRealPlayerFaction(invitee) || invitee.defeated)
		{
			return false;
		}
		if (!_relationsResolved)
		{
			_relationsResolved = true;
			try
			{
				Type t = AccessTools.TypeByName("RaidAnimalFix.RelationsApi");
				_areAllied = (t == null) ? null : AccessTools.Method(t, "AreAllied", new Type[2]
				{
					typeof(Faction),
					typeof(Faction)
				});
			}
			catch
			{
				_areAllied = null;
			}
		}
		if (_areAllied == null)
		{
			return true;
		}
		try
		{
			return (bool)_areAllied.Invoke(null, new object[2] { owner, invitee });
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("SharedQuests.AreAllied", ex);
			return false;
		}
	}

	public static bool DiplomacyPresent
	{
		get
		{
			CanInvite(null, null);
			return _areAllied != null;
		}
	}

	/// <summary>Фракции, которых владелец может пригласить (порядок AllFactionsListForReading).</summary>
	public static List<Faction> Candidates(Faction owner, RafSharedQuestData.Entry entry)
	{
		List<Faction> result = new List<Faction>();
		List<Faction> all = Find.FactionManager.AllFactionsListForReading;
		for (int i = 0; i < all.Count; i++)
		{
			Faction f = all[i];
			if (f == owner || (entry != null && (entry.IsParticipant(f) || entry.IsInvited(f))))
			{
				continue;
			}
			if (CanInvite(owner, f))
			{
				result.Add(f);
			}
		}
		return result;
	}

	/// <summary>
	/// Домашняя карта фракции: первая в Find.Maps карта поселения этой фракции,
	/// иначе первая любая её карта. Порядок Find.Maps одинаков у всех.
	/// </summary>
	public static Map HomeMapOf(Faction f)
	{
		if (f == null)
		{
			return null;
		}
		List<Map> maps = Find.Maps;
		for (int i = 0; i < maps.Count; i++)
		{
			if (maps[i].ParentFaction == f && maps[i].Parent is Settlement)
			{
				return maps[i];
			}
		}
		for (int j = 0; j < maps.Count; j++)
		{
			if (maps[j].ParentFaction == f)
			{
				return maps[j];
			}
		}
		return null;
	}

	/// <summary>Действующая доля одного участника в процентах (целое) при n участниках.</summary>
	public static int EffectivePercent(RafSharedQuestData.Entry entry, int n)
	{
		if (n <= 0)
		{
			return 0;
		}
		if (entry.sharePercent <= 0)
		{
			return 100 / (n + 1);
		}
		return Math.Min(entry.sharePercent, 100 / n);
	}

	public static string ShareLabel(int sharePercent, int participantCount)
	{
		if (sharePercent <= 0)
		{
			return "RAF_SharedQuests_ShareEqual".Translate(100 / (Math.Max(participantCount, 1) + 1));
		}
		return "RAF_SharedQuests_SharePercent".Translate(sharePercent);
	}

	public static string FactionNames(List<int> ids)
	{
		List<string> names = new List<string>();
		for (int i = 0; i < ids.Count; i++)
		{
			Faction f = FactionById(ids[i]);
			if (f != null)
			{
				names.Add(f.Name);
			}
		}
		return names.Count == 0 ? "-" : string.Join(", ", names);
	}

	// ─────────── Локальные уведомления (только UI) ───────────

	/// <summary>
	/// Очередь сообщений для локального игрока. Sync-команда выполняется в
	/// контексте фракции отправителя, и MessageFilterFix2 спрятал бы
	/// сообщение для приглашённого; поэтому текст ставится в очередь и
	/// показывается в UIRootUpdate, где контекст — свой. На симуляцию не влияет.
	/// </summary>
	internal static readonly List<string> PendingNotices = new List<string>();

	/// <summary>
	/// Показать сообщение, если локальный игрок играет за target.
	/// Чтение RealPlayerFaction здесь — только для выбора, КОМУ показать
	/// (UI), состояние игры от этого не зависит.
	/// </summary>
	public static void NotifyLocal(Faction target, string text)
	{
		if (target == null || text.NullOrEmpty())
		{
			return;
		}
		try
		{
			if (MpHelper.GetCurrentPlayerFaction() == target)
			{
				PendingNotices.Add(text);
			}
		}
		catch
		{
		}
	}
}

/// <summary>Выводит накопленные уведомления общих квестов уже вне симуляции.</summary>
[HarmonyPatch(typeof(UIRoot_Play), "UIRootUpdate")]
public static class SharedQuestNoticePump
{
	private static void Postfix()
	{
		if (SharedQuestUtil.PendingNotices.Count == 0)
		{
			return;
		}
		try
		{
			for (int i = 0; i < SharedQuestUtil.PendingNotices.Count; i++)
			{
				Messages.Message(SharedQuestUtil.PendingNotices[i], MessageTypeDefOf.NeutralEvent, historical: false);
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("SharedQuestNoticePump", ex);
		}
		SharedQuestUtil.PendingNotices.Clear();
	}
}

/// <summary>
/// Синхронные команды общих квестов. Вызываются из окна квестов; MP
/// перехватывает вызов в интерфейсе и выполняет у всех. Квест передаётся
/// по id (int), фракции — объектами (MP их сериализует). Каждая команда
/// заново проверяет права по синхронному состоянию: клиент мог прислать
/// устаревшее нажатие.
/// </summary>
public static class SharedQuestSync
{
	[SyncMethod]
	public static void InviteToQuest(int questId, Faction owner, Faction invitee)
	{
		try
		{
			RafSharedQuestData data = RafSharedQuestData.Get();
			Quest quest = SharedQuestUtil.QuestById(questId);
			if (data == null || quest == null || owner == null || invitee == null || quest.State != QuestState.Ongoing)
			{
				return;
			}
			data.Prune();
			RafSharedQuestData.Entry entry = data.Find(questId);
			Faction realOwner = (entry != null) ? SharedQuestUtil.FactionById(entry.ownerId) : SharedQuestUtil.OwnerOf(quest);
			if (realOwner != owner || !SharedQuestUtil.CanInvite(owner, invitee))
			{
				return;
			}
			entry = data.GetOrCreate(questId, owner.loadID);
			if (entry.IsParticipant(invitee) || entry.IsInvited(invitee))
			{
				return;
			}
			entry.invited.Add(invitee.loadID);
			SharedQuestUtil.NotifyLocal(invitee, "RAF_SharedQuests_MsgInvited".Translate(owner.Name, quest.name));
			SharedQuestUtil.NotifyLocal(owner, "RAF_SharedQuests_MsgInviteSent".Translate(invitee.Name, quest.name));
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("SharedQuests.Invite", ex);
		}
	}

	[SyncMethod]
	public static void RespondToInvite(int questId, Faction invitee, bool accept)
	{
		try
		{
			RafSharedQuestData data = RafSharedQuestData.Get();
			RafSharedQuestData.Entry entry = data?.Find(questId);
			Quest quest = SharedQuestUtil.QuestById(questId);
			if (entry == null || invitee == null || !entry.IsInvited(invitee))
			{
				return;
			}
			entry.invited.Remove(invitee.loadID);
			Faction owner = SharedQuestUtil.FactionById(entry.ownerId);
			bool ok = accept && quest != null && quest.State == QuestState.Ongoing && SharedQuestUtil.CanInvite(owner, invitee);
			if (ok)
			{
				entry.participants.Add(invitee.loadID);
			}
			string questName = quest?.name ?? "?";
			string key = ok ? "RAF_SharedQuests_MsgJoined" : "RAF_SharedQuests_MsgDeclined";
			SharedQuestUtil.NotifyLocal(owner, key.Translate(invitee.Name, questName));
			SharedQuestUtil.NotifyLocal(invitee, key.Translate(invitee.Name, questName));
			data.Prune();
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("SharedQuests.Respond", ex);
		}
	}

	/// <summary>Участник выходит сам, или владелец исключает участника/отзывает приглашение.</summary>
	[SyncMethod]
	public static void LeaveQuest(int questId, Faction member)
	{
		try
		{
			RafSharedQuestData data = RafSharedQuestData.Get();
			RafSharedQuestData.Entry entry = data?.Find(questId);
			if (entry == null || member == null)
			{
				return;
			}
			bool removed = entry.participants.Remove(member.loadID) | entry.invited.Remove(member.loadID);
			if (removed)
			{
				Quest quest = SharedQuestUtil.QuestById(questId);
				string text = "RAF_SharedQuests_MsgLeft".Translate(member.Name, quest?.name ?? "?");
				SharedQuestUtil.NotifyLocal(SharedQuestUtil.FactionById(entry.ownerId), text);
				SharedQuestUtil.NotifyLocal(member, text);
			}
			data.Prune();
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("SharedQuests.Leave", ex);
		}
	}

	[SyncMethod]
	public static void SetSharePercent(int questId, Faction owner, int percent)
	{
		try
		{
			RafSharedQuestData data = RafSharedQuestData.Get();
			Quest quest = SharedQuestUtil.QuestById(questId);
			if (data == null || quest == null || owner == null)
			{
				return;
			}
			RafSharedQuestData.Entry entry = data.Find(questId);
			Faction realOwner = (entry != null) ? SharedQuestUtil.FactionById(entry.ownerId) : SharedQuestUtil.OwnerOf(quest);
			if (realOwner != owner)
			{
				return;
			}
			entry = data.GetOrCreate(questId, owner.loadID);
			entry.sharePercent = Math.Max(0, Math.Min(percent, 100));
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("SharedQuests.Share", ex);
		}
	}
}
