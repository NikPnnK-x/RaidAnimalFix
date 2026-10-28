using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RaidAnimalFix;

/// <summary>
/// Очки угрозы рейда считаются по Map.PlayerPawnsForStoryteller =
/// PawnsInFaction(Faction.OfPlayer): в тике карты хозяина это только его пешки.
/// Отряд гостя (10 бойцов) на карте хозяина рейдом не учитывается, и рейд
/// выходит смехотворным. Добавляем свободных колонистов других реальных игроков
/// на карте, принадлежащей реальному игроку, когда считает сам хозяин
/// (OfPlayer == владелец карты — это и есть тик карты / инцидент хозяина).
/// Порядок — порядок списка пешек карты (синхронный).
/// </summary>
[HarmonyPatch(typeof(Map), "PlayerPawnsForStoryteller", MethodType.Getter)]
public static class GuestThreatPawns
{
	/// <summary>Доля учитываемых гостей (1.0 — все). Константа кода, не настройка клиента.</summary>
	public const float GuestThreatFactor = 1f;

	private static void Postfix(Map __instance, ref IEnumerable<Pawn> __result)
	{
		if (!RelationsUtil.InMultiplayer || GuestThreatFactor <= 0f)
		{
			return;
		}
		try
		{
			List<Pawn> guests = GuestColonists(__instance);
			if (guests == null || guests.Count == 0)
			{
				return;
			}
			List<Pawn> list = new List<Pawn>(__result);
			list.AddRange(guests);
			__result = list;
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("GuestThreatPawns", ex);
		}
	}

	/// <summary>
	/// Свободные колонисты чужих реальных игроков на карте владельца (с учётом
	/// GuestThreatFactor). null — не наш случай (не MP-карта игрока или считает не владелец).
	/// </summary>
	public static List<Pawn> GuestColonists(Map map)
	{
		Faction owner = map?.ParentFaction;
		if (owner == null || !owner.IsPlayer || Faction.OfPlayer != owner || !MpContext.IsRealPlayerFaction(owner))
		{
			return null;
		}
		List<Pawn> result = null;
		IReadOnlyList<Pawn> spawned = map.mapPawns.AllPawnsSpawned;
		for (int i = 0; i < spawned.Count; i++)
		{
			Pawn p = spawned[i];
			Faction f = p.Faction;
			if (f != null && f != owner && f.IsPlayer && p.IsFreeColonist && !p.Dead && MpContext.IsRealPlayerFaction(f))
			{
				if (result == null)
				{
					result = new List<Pawn>();
				}
				result.Add(p);
			}
		}
		if (result != null && GuestThreatFactor < 1f)
		{
			int keep = (int)Math.Round(result.Count * GuestThreatFactor);
			if (keep < result.Count)
			{
				result.RemoveRange(keep, result.Count - keep);
			}
		}
		return result;
	}
}

/// <summary>
/// Богатство для угрозы: на домашней карте — wealthWatcher (считает пешки
/// фракции контекста), иначе — снаряжение колонистов OfPlayer. Снаряжение
/// гостей (и их стоимость как пешек) добавляем с тем же множителем.
/// </summary>
[HarmonyPatch(typeof(Map), "PlayerWealthForStoryteller", MethodType.Getter)]
public static class GuestThreatWealth
{
	private static void Postfix(Map __instance, ref float __result)
	{
		if (!RelationsUtil.InMultiplayer || GuestThreatPawns.GuestThreatFactor <= 0f)
		{
			return;
		}
		try
		{
			if (__instance.TreatAsPlayerHomeForThreatPoints && Find.Storyteller.difficulty.fixedWealthMode)
			{
				return;
			}
			List<Pawn> guests = GuestThreatPawns.GuestColonists(__instance);
			if (guests == null)
			{
				return;
			}
			float add = 0f;
			for (int i = 0; i < guests.Count; i++)
			{
				add += WealthWatcher.GetEquipmentApparelAndInventoryWealth(guests[i]);
				if (__instance.TreatAsPlayerHomeForThreatPoints)
				{
					add += guests[i].MarketValue;
				}
			}
			__result += add;
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("GuestThreatWealth", ex);
		}
	}
}

/// <summary>
/// Рейд на карту, где стоят колонисты другого игрока: письмо о рейде получает
/// только хозяин (фильтр писем по фракции контекста), а гость узнаёт о бое,
/// когда его пешек уже бьют. Показываем гостям локальное сообщение.
///
/// 3.0.946: решение «создавать ли сообщение» было локальным (LocalFaction !=
/// владельца карты), а конструктор Message расходует GetNextMessageID из
/// синхронного состояния — в тике сторителлера клиент гостя тратил ID, который
/// хост не тратил вовсе, и игра рассинхронизировалась («Trace hashes don't
/// match», лишний трейс GetNextMessageID у гостя). Теперь условие синхронное:
/// перебираем фракции реальных игроков в порядке AllFactionsListForReading, и
/// для каждой с колонистами на карте создаём одно сообщение — их число у всех
/// клиентов одинаково, потому что определяется только составом мира. Кому
/// показывать, решают фильтры отображения (MessageFilterFix2 / SilenceMessages
/// форка) по целям сообщения — уже после расхода ID.
/// </summary>
[HarmonyPatch(typeof(IncidentWorker_Raid), "TryExecuteWorker")]
public static class GuestRaidNotice
{
	private static void Postfix(IncidentParms parms, bool __result)
	{
		if (!__result || parms == null || !(parms.target is Map map) || !RelationsUtil.InMultiplayer)
		{
			return;
		}
		try
		{
			Faction owner = map.ParentFaction;
			string raider = parms.faction?.Name ?? "?";
			string place = map.Parent?.LabelCap ?? map.ToString();
			List<Faction> factions = Find.FactionManager.AllFactionsListForReading;
			for (int i = 0; i < factions.Count; i++)
			{
				Faction f = factions[i];
				if (f == null || f == owner || !MpContext.IsRealPlayerFaction(f) || !NotificationFilter.MapHasColonistsOf(map, f))
				{
					continue;
				}
				Pawn mine = null;
				IReadOnlyList<Pawn> spawned = map.mapPawns.AllPawnsSpawned;
				for (int j = 0; j < spawned.Count; j++)
				{
					if (spawned[j].Faction == f && spawned[j].IsFreeColonist)
					{
						mine = spawned[j];
						break;
					}
				}
				LookTargets targets = (mine != null) ? new LookTargets(mine) : LookTargets.Invalid;
				Messages.Message(new Message("[RaidAnimalFix] " + "RAF_Relations_MsgRaidOnHostMap".Translate(raider, place), MessageTypeDefOf.ThreatBig, targets), historical: false);
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("GuestRaidNotice", ex);
		}
	}
}
