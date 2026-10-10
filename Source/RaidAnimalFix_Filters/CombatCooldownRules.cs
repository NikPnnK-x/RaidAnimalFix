using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RaidAnimalFix;

internal static class CombatCooldownRules
{
	private const int RAID_MIN_GAP = 600;

	private const int ANIMALS_MIN_GAP = 1800;

	// Геттер StoryState.LastThreatBigTick сбрасывает поле и пишет ошибку, если
	// значение «в будущем» больше чем на 1000 тиков — при смешанных часах
	// (мир/карта) это меняет состояние. Читаем поле напрямую.
	private static readonly FieldInfo LastThreatBigTickField = AccessTools.Field(typeof(StoryState), "lastThreatBigTick");

	private static int TicksSinceBigThreat(Map map)
	{
		StoryState storyState = map.StoryState;
		if (storyState == null)
		{
			return int.MaxValue;
		}
		int lastThreatBigTick;
		try
		{
			lastThreatBigTick = (LastThreatBigTickField != null) ? ((int)LastThreatBigTickField.GetValue(storyState)) : storyState.LastThreatBigTick;
		}
		catch
		{
			return int.MaxValue;
		}
		if (lastThreatBigTick <= 0)
		{
			return int.MaxValue;
		}
		int now;
		if (!MpMapTime.TryGetMapTicks(map, out now))
		{
			now = Find.TickManager.TicksGame;
		}
		int num = now - lastThreatBigTick;
		// Метка могла быть поставлена по мировым часам — отрицательная разница
		// значит «давно/неизвестно», не блокируем.
		return (num < 0) ? int.MaxValue : num;
	}

	public static bool RaidAllowed(Map map)
	{
		return TicksSinceBigThreat(map) >= RAID_MIN_GAP;
	}

	public static bool AnimalAttackAllowed(Map map)
	{
		if (TicksSinceBigThreat(map) < ANIMALS_MIN_GAP)
		{
			return false;
		}
		// Угроза именно хозяину карты, а не фракции текущего рассказчика.
		Faction owner = map.ParentFaction;
		if (owner != null && owner.IsPlayer)
		{
			if (GenHostility.AnyHostileActiveThreatTo(map, owner))
			{
				return false;
			}
		}
		else if (GenHostility.AnyHostileActiveThreatToPlayer(map))
		{
			return false;
		}
		return true;
	}
}
