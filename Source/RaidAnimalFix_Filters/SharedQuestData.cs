using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RaidAnimalFix;

/// <summary>
/// Сохраняемое состояние общих квестов. Квест в MP «принадлежит» тому, кто
/// его принял, и союзники, которые помогают, не получают ни писем, ни награды.
/// Здесь хранится, кого владелец пригласил и кто согласился участвовать.
/// Всё по loadID фракций и id квеста (ссылки на объекты в List, без словарей
/// с объектными ключами) — порядок участников = порядок вступления, одинаковый
/// у всех клиентов, на нём построен детерминированный дележ награды.
/// Меняется только из [SyncMethod] (SharedQuestSync).
/// </summary>
public class RafSharedQuestData : WorldComponent
{
	/// <summary>Одна запись на общий квест.</summary>
	public class Entry : IExposable
	{
		public int questId = -1;

		/// <summary>loadID фракции-владельца (зафиксирован при первом приглашении).</summary>
		public int ownerId = -1;

		/// <summary>loadID вступивших фракций в порядке вступления.</summary>
		public List<int> participants = new List<int>();

		/// <summary>loadID приглашённых, но ещё не ответивших.</summary>
		public List<int> invited = new List<int>();

		/// <summary>
		/// Доля КАЖДОГО участника в процентах; 0 = поровну между владельцем и
		/// участниками. Целое — дележ считается целочисленно, без float.
		/// </summary>
		public int sharePercent;

		public void ExposeData()
		{
			Scribe_Values.Look(ref questId, "questId", -1);
			Scribe_Values.Look(ref ownerId, "ownerId", -1);
			Scribe_Collections.Look(ref participants, "participants", LookMode.Value);
			Scribe_Collections.Look(ref invited, "invited", LookMode.Value);
			Scribe_Values.Look(ref sharePercent, "sharePercent", 0);
			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				if (participants == null)
				{
					participants = new List<int>();
				}
				if (invited == null)
				{
					invited = new List<int>();
				}
			}
		}

		public bool IsParticipant(Faction f)
		{
			return f != null && participants.Contains(f.loadID);
		}

		public bool IsInvited(Faction f)
		{
			return f != null && invited.Contains(f.loadID);
		}
	}

	private List<Entry> entries = new List<Entry>();

	public RafSharedQuestData(World world)
		: base(world)
	{
	}

	public static RafSharedQuestData Get()
	{
		World world = Verse.Find.World;
		if (world == null)
		{
			return null;
		}
		try
		{
			return world.GetComponent<RafSharedQuestData>();
		}
		catch
		{
			return null;
		}
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Collections.Look(ref entries, "rafSharedQuests", LookMode.Deep);
		if (Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			if (entries == null)
			{
				entries = new List<Entry>();
			}
			entries.RemoveAll((Entry e) => e == null);
		}
	}

	public List<Entry> EntriesForReading => entries;

	public Entry Find(int questId)
	{
		for (int i = 0; i < entries.Count; i++)
		{
			if (entries[i].questId == questId)
			{
				return entries[i];
			}
		}
		return null;
	}

	public Entry GetOrCreate(int questId, int ownerId)
	{
		Entry entry = Find(questId);
		if (entry == null)
		{
			entry = new Entry
			{
				questId = questId,
				ownerId = ownerId
			};
			entries.Add(entry);
		}
		return entry;
	}

	/// <summary>
	/// Убирает записи квестов, которых больше нет в QuestManager, и пустые записи
	/// (без участников, приглашённых и без настроенной доли).
	/// Вызывается только из синхронных команд — у всех в один момент.
	/// </summary>
	public void Prune()
	{
		QuestManager qm = Verse.Find.QuestManager;
		if (qm == null)
		{
			return;
		}
		for (int i = entries.Count - 1; i >= 0; i--)
		{
			Entry e = entries[i];
			if (SharedQuestUtil.QuestById(e.questId) == null || (e.participants.Count == 0 && e.invited.Count == 0 && e.sharePercent == 0))
			{
				entries.RemoveAt(i);
			}
		}
	}
}
