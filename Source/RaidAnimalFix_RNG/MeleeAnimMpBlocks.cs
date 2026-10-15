using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RaidAnimalFix;

// Melee Animation: всё, что меняет работы пешек / создаёт вещи прямо из UI
// или из per-client настроек, в MP выключаем. MP не синхронизирует пункты
// float-меню и гизмо сторонних модов — StartJob выполнялся только у кликнувшего.

/// <summary>Пункты меню Melee Animation (казнь, лассо, навыки) — StartJob локально.</summary>
[HarmonyPatch]
public static class MeleeAnimNoFloatMenuFix
{
	private static MethodBase TargetMethod()
	{
		Type type = RafTypes.Find("AM.Patches.Patch_FloatMenuMakerMap_GetOptions");
		return (type == null) ? null : AccessTools.Method(type, "Postfix");
	}

	private static bool Prefix()
	{
		return !MpHelper.IsMultiplayer();
	}
}

/// <summary>Гизмо дуэльной площадки — TryStartDuel запускает работы обеим пешкам локально.</summary>
[HarmonyPatch]
public static class MeleeAnimNoDuelSpotFix
{
	private static MethodBase TargetMethod()
	{
		Type type = RafTypes.Find("AM.Buildings.Building_DuelSpot");
		return (type == null) ? null : AccessTools.Method(type, "TryStartDuel");
	}

	private static bool Prefix()
	{
		if (!MpHelper.IsMultiplayer())
		{
			return true;
		}
		MeleeAnimGuard.LogOnce();
		return false;
	}
}

/// <summary>
/// Лассо при генерации пешки: Rand.Chance(настройка клиента) + ThingMaker
/// (номер вещи) + Wear. Разные настройки → разный поток Rand и ID.
/// </summary>
[HarmonyPatch]
public static class MeleeAnimNoLassoSpawnFix
{
	private static MethodBase TargetMethod()
	{
		Type type = RafTypes.Find("AM.Patches.Patch_PawnGenerator_GeneratePawn");
		return (type == null) ? null : AccessTools.Method(type, "Postfix");
	}

	private static bool Prefix()
	{
		return !MpHelper.IsMultiplayer();
	}
}

/// <summary>
/// Дружеские дуэли: кулдаун из настроек клиента, анимация в MP заблокирована,
/// работа рвётся и повторяется бесконечно. Не выдаём работу вовсе.
/// </summary>
[HarmonyPatch]
public static class MeleeAnimNoFriendlyDuelFix
{
	private static MethodBase TargetMethod()
	{
		Type type = RafTypes.Find("AM.AutoDuel.JoyGiver_FriendlyDuel");
		return (type == null) ? null : AccessTools.Method(type, "TryGiveJob", new Type[1] { typeof(Pawn) });
	}

	private static bool Prefix(ref Verse.AI.Job __result)
	{
		if (!MpHelper.IsMultiplayer())
		{
			return true;
		}
		__result = null;
		return false;
	}
}

[HarmonyPatch]
public static class MeleeAnimNoSpectateDuelFix
{
	private static MethodBase TargetMethod()
	{
		Type type = RafTypes.Find("AM.AutoDuel.JoyGiver_SpectateFriendlyDuel");
		return (type == null) ? null : AccessTools.Method(type, "TryGiveJob", new Type[1] { typeof(Pawn) });
	}

	private static bool Prefix(ref Verse.AI.Job __result)
	{
		if (!MpHelper.IsMultiplayer())
		{
			return true;
		}
		__result = null;
		return false;
	}
}

/// <summary>
/// NotifyPawnDidMeleeAttack зовётся из Verb_MeleeAttack.TryCastShot (тик),
/// но только если у клиента включён AnimateAtIdle, и тратит до ~21 Rand.
/// Изолируем Rand, как остальные idle-методы.
/// </summary>
[HarmonyPatch]
public static class IdleControllerMeleeAttackFix
{
	private static MethodBase TargetMethod()
	{
		Type type = RafTypes.Find("AM.Idle.IdleControllerComp");
		return (type == null) ? null : AccessTools.Method(type, "NotifyPawnDidMeleeAttack");
	}

	private static void Prefix(ref bool __state)
	{
		RngIsolation.Begin(ref __state);
	}

	private static void Finalizer(bool __state)
	{
		RngIsolation.End(__state);
	}
}

[HarmonyPatch]
public static class IdleControllerMeleeDodgeFix
{
	private static MethodBase TargetMethod()
	{
		Type type = RafTypes.Find("AM.Idle.IdleControllerComp");
		return (type == null) ? null : AccessTools.Method(type, "OnMeleeDodge");
	}

	private static void Prefix(ref bool __state)
	{
		RngIsolation.Begin(ref __state);
	}

	private static void Finalizer(bool __state)
	{
		RngIsolation.End(__state);
	}
}
