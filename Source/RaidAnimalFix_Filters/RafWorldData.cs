using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RaidAnimalFix;

public class RafWorldData : WorldComponent
{
	private Dictionary<int, int> questOwners = new Dictionary<int, int>();

	// thingID похищенной пешки → мировой тик, до которого её выкуп уже требуют.
	// Синхронная замена проверке Find.LetterStack (стек писем в MP у каждого свой).
	private Dictionary<int, int> ransomDemandedUntil = new Dictionary<int, int>();

	// Мировой тик последнего письма «Sightstealer обнаружен» (ваниль: static float реального времени).
	private int sightstealerLetterTick = -99999;

	public RafWorldData(World world)
		: base(world)
	{
	}

	public static RafWorldData Get()
	{
		World world = Find.World;
		if (world == null)
		{
			return null;
		}
		try
		{
			return world.GetComponent<RafWorldData>();
		}
		catch
		{
			return null;
		}
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Collections.Look(ref questOwners, "rafQuestOwners", LookMode.Value, LookMode.Value);
		Scribe_Collections.Look(ref ransomDemandedUntil, "rafRansomDemandedUntil", LookMode.Value, LookMode.Value);
		Scribe_Values.Look(ref sightstealerLetterTick, "rafSightstealerLetterTick", -99999);
		if (Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			if (questOwners == null)
			{
				questOwners = new Dictionary<int, int>();
			}
			if (ransomDemandedUntil == null)
			{
				ransomDemandedUntil = new Dictionary<int, int>();
			}
		}
	}

	public static void RecordQuestOwner(Quest q, Faction owner)
	{
		if (q != null && owner != null)
		{
			RafWorldData rafWorldData = Get();
			if (rafWorldData != null && !rafWorldData.questOwners.ContainsKey(q.id))
			{
				rafWorldData.questOwners[q.id] = owner.loadID;
			}
		}
	}

	public static void RecordRansomDemand(Pawn kidnapped, int untilWorldTick)
	{
		RafWorldData rafWorldData = Get();
		if (rafWorldData != null && kidnapped != null)
		{
			rafWorldData.ransomDemandedUntil[kidnapped.thingIDNumber] = untilWorldTick;
		}
	}

	public static bool RansomDemandActive(Pawn kidnapped, int nowWorldTick)
	{
		RafWorldData rafWorldData = Get();
		int value;
		return rafWorldData != null && kidnapped != null && rafWorldData.ransomDemandedUntil.TryGetValue(kidnapped.thingIDNumber, out value) && value > nowWorldTick;
	}

	public static int SightstealerLetterTick
	{
		get
		{
			RafWorldData rafWorldData = Get();
			return (rafWorldData == null) ? (-99999) : rafWorldData.sightstealerLetterTick;
		}
		set
		{
			RafWorldData rafWorldData = Get();
			if (rafWorldData != null)
			{
				rafWorldData.sightstealerLetterTick = value;
			}
		}
	}

	public static bool IsForeignQuest(Quest q, Faction me)
	{
		if (q == null || me == null)
		{
			return false;
		}
		RafWorldData rafWorldData = Get();
		if (rafWorldData == null)
		{
			return false;
		}
		if (!rafWorldData.questOwners.TryGetValue(q.id, out var value))
		{
			return false;
		}
		return value != me.loadID;
	}
}
