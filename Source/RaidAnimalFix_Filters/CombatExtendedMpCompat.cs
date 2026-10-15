using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RaidAnimalFix;

// COMBAT EXTENDED (CETeam.CombatExtended) + Multiplayer.
// У CE есть встроенный модуль совместимости с MP, а отдельный мод «Combat Extended —
// Multiplayer Desync Fixes» (CEMP.DesyncFix) закрывает остаток. Если его нет, закрываем
// то же самое сами (оценка 3 в таблице совместимости MP):
//  1. BlackSmokeTracker считает дым в Parallel.ForEach, а внутри порядок соседних клеток
//     выбирается через Rand (InRandomOrder) — генератор RimWorld не потокобезопасен,
//     плюс клетки читают плотность соседей, пока те её меняют. → в MP последовательно.
//  2. Настройки CE (боеприпасы, подавление, осколки, гильзы-мусор, охота в ближнем бою,
//     «оппортунистическая» перезарядка…) читает симуляция. → сверка настроек
//     (MpFeatures) и блокировка окна настроек во время MP-игры.
// Если CEMP.DesyncFix стоит — он делает это сам (и синхронизирует настройки на лету),
// мы не вмешиваемся: двойной патч дыма тикал бы его дважды.

internal static class CeMp
{
	public static bool Present => AccessTools.TypeByName("CombatExtended.Controller") != null;

	public static bool FixPackLoaded
	{
		get
		{
			try
			{
				return ModLister.GetActiveModWithIdentifier("CEMP.DesyncFix", ignorePostfix: true) != null;
			}
			catch
			{
				return false;
			}
		}
	}
}

/// <summary>1. Дым CE в MP — последовательно, в фиксированном порядке.</summary>
[HarmonyPatch]
public static class CeSmokeSequentialMp
{
	private static FieldInfo _smoke;

	private static FastInvokeHandler _parallelTick;

	private static FastInvokeHandler _spread;

	private static bool Prepare()
	{
		return CeMp.Present && !CeMp.FixPackLoaded;
	}

	private static MethodBase TargetMethod()
	{
		Type tracker = AccessTools.TypeByName("CombatExtended.BlackSmokeTracker");
		Type smoke = AccessTools.TypeByName("CombatExtended.Smoke");
		if (tracker == null || smoke == null)
		{
			return null;
		}
		_smoke = AccessTools.Field(tracker, "_smoke");
		MethodInfo tick = AccessTools.Method(smoke, "ParallelTick");
		MethodInfo spread = AccessTools.Method(smoke, "DoSpreadToAdjacentCells");
		if (_smoke == null || tick == null || spread == null)
		{
			return null;
		}
		_parallelTick = MethodInvoker.GetHandler(tick);
		_spread = MethodInvoker.GetHandler(spread);
		return AccessTools.Method(tracker, "MapComponentTick");
	}

	private static bool Prefix(MapComponent __instance)
	{
		if (!MpHelper.IsMultiplayer() || !(_smoke.GetValue(__instance) is IList list))
		{
			return true;
		}
		for (int i = 0; i < list.Count; i++)
		{
			_parallelTick(list[i]);
		}
		for (int j = 0; j < list.Count; j++)
		{
			_spread(list[j]);
		}
		return false;
	}
}

/// <summary>2. Окно настроек CE во время MP-игры заблокировано (если нет CEMP.DesyncFix).</summary>
[HarmonyPatch]
public static class CeSettingsLockMp
{
	private static bool Prepare()
	{
		return CeMp.Present && !CeMp.FixPackLoaded;
	}

	private static MethodBase TargetMethod()
	{
		Type t = AccessTools.TypeByName("CombatExtended.Controller");
		return (t == null) ? null : AccessTools.DeclaredMethod(t, "DoSettingsWindowContents", new Type[1] { typeof(Rect) });
	}

	private static bool Prefix(Rect inRect)
	{
		if (Current.ProgramState != ProgramState.Playing || !MpHelper.IsMultiplayer())
		{
			return true;
		}
		GameFont font = Text.Font;
		Text.Font = GameFont.Small;
		Widgets.Label(inRect, "[Desync Fix] " + "RAF_SimSettingsLocked".Translate("Combat Extended").Resolve());
		Text.Font = font;
		return false;
	}
}
