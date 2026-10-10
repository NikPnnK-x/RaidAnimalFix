using System;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RaidAnimalFix;

/// <summary>
/// Ваниль меняет репутацию между фракциями игроков навсегда: захват/убийство пешки
/// (Notify_MemberCaptured → GoodwillToMakeHostile), раздевание (-40), нападение на
/// поселение (AffectRelationsOnAttacked). Естественный дрейф игроков пропускает —
/// вражда остаётся навсегда. В MP отношения игроков задаёт только дипломатия мода:
/// ванильное изменение между двумя реальными игроками отбрасываем и пишем в лог.
/// Свой путь (RelationsUtil.ApplyVanilla) пропускаем по applyDepth.
/// Решение зависит только от фракций-аргументов — одинаково у всех клиентов.
/// </summary>
[HarmonyPatch(typeof(Faction), "TryAffectGoodwillWith")]
public static class RelationsBlockGoodwillChange
{
	private static int _lastLogTick = -99999;

	private static bool Prefix(Faction __instance, Faction other, int goodwillChange, HistoryEventDef reason, ref bool __result)
	{
		if (!RelationsGuard.ShouldBlock(__instance, other))
		{
			return true;
		}
		__result = false;
		RelationsGuard.LogThrottled(ref _lastLogTick, "TryAffectGoodwillWith " + __instance.Name + " → " + other.Name + " (" + goodwillChange + ", " + (reason?.defName ?? "no reason") + ") заблокировано: отношения игроков задаёт дипломатия");
		return false;
	}
}

/// <summary>
/// SetRelationDirect для фракций с goodwill ваниль сама отвергает, но моды/квесты
/// могут звать его и для игроков. Блокируем вне нашего пути.
/// </summary>
[HarmonyPatch(typeof(Faction), "SetRelationDirect")]
public static class RelationsBlockSetRelationDirect
{
	private static int _lastLogTick = -99999;

	private static bool Prefix(Faction __instance, Faction other, FactionRelationKind kind)
	{
		if (!RelationsGuard.ShouldBlock(__instance, other))
		{
			return true;
		}
		RelationsGuard.LogThrottled(ref _lastLogTick, "SetRelationDirect " + __instance.Name + " → " + other.Name + " = " + kind + " заблокировано: отношения игроков задаёт дипломатия");
		return false;
	}
}

/// <summary>
/// Нападение на поселение другого игрока: ваниль делает его врагом навсегда.
/// Сам вызов TryAffectGoodwillWith мы уже блокируем; здесь лишь не даём
/// дописывать в письмо «отношения изменились», которых не было.
/// </summary>
[HarmonyPatch(typeof(SettlementUtility), "AffectRelationsOnAttacked")]
public static class RelationsAttackSettlementNoChange
{
	private static bool Prefix(MapParent mapParent)
	{
		if (mapParent == null || !RelationsGuard.ShouldBlock(Faction.OfPlayer, mapParent.Faction))
		{
			return true;
		}
		Log.Message("[RaidAnimalFix] Relations: нападение " + Faction.OfPlayer.Name + " на " + mapParent.Label + " (" + mapParent.Faction.Name + ") не меняет отношения — статус задаёт дипломатия");
		return false;
	}
}

internal static class RelationsGuard
{
	public static bool ShouldBlock(Faction a, Faction b)
	{
		if (RelationsUtil.applyDepth > 0 || a == null || b == null || a == b)
		{
			return false;
		}
		if (!a.IsPlayer || !b.IsPlayer || !RelationsUtil.InMultiplayer)
		{
			return false;
		}
		return MpContext.IsRealPlayerFaction(a) && MpContext.IsRealPlayerFaction(b);
	}

	public static void LogThrottled(ref int last, string text)
	{
		int now = (Find.TickManager != null) ? Find.TickManager.TicksGame : 0;
		if (now - last >= 250 || now < last)
		{
			last = now;
			Log.Message("[RaidAnimalFix] Relations: " + text);
		}
	}
}
