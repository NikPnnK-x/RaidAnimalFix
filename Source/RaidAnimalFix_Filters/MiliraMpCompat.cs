using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using UnityEngine;
using Verse;

namespace RaidAnimalFix;

// MILIRA RACE (Ancot.MiliraRace) + Multiplayer — без сторонних MP-патчей.
//  1. Приказы из прицела (полёт, рывки, прыжок милир) — у MP синхронны только типы из
//     сборки RimWorld. → OrderForceTarget всех прицелов Milira синхронны.
//  2. Кнопки и меню, меняющие игру (режим полёта, резонанс, отмена загрузки крепости,
//     «снять всё» в таблице снаряжения, передача падшего ангела, цвет и причёска милиан)
//     выполнялись только у нажавшего. → синхронны (лямбды ищутся по IL, без номеров).
//  3. Галочки таблицы милиан (автоодевание, показ в списке рабочих) и «ручные приоритеты»
//     на вкладке работ милиан. → синхронны (приоритеты — тем же полем, что и у ваниллы).
//  4. Компонент сюжета Милиры раз в N тиков шлёт рейды/события на Find.CurrentMap — карту,
//     на которую смотрит клиент; генератор милиан берёт богатство той же карты. → в MP
//     одна и та же карта у всех (контекст MP, иначе база игрока по номеру и дню).
//  5. MiliraFleckMaker тратит Rand только у тех, кто видит клетку (см. ModVisualRandIsolationMp).
//  6. pawnInColony (падший ангел в колонии, нужен квестам) не сохраняется — у вошедшего
//     игрока его нет, у хоста есть. → сохраняется.
//  7. Настройки Milira читает симуляция (сложность, сюжет, кластеры). → сверка и блокировка.

internal static class MiliraMp
{
	public static Type Control => AccessTools.TypeByName("Milira.MiliraGameComponent_OverallControl");

	public static bool Present => Control != null;

	/// <summary>Если стоит отдельный MP-патч Milira, наш модуль не дублирует его.</summary>
	public static bool Enabled => Present && !ModCompatGuards.Active(ModCompatGuards.MiliraForeign);
}

/// <summary>Регистрация синхронизации Milira (из MpApiRegistry, порядок фиксирован).</summary>
internal static class MiliraMpSync
{
	private const string Tag = "Milira";

	public static void Register()
	{
		if (!MiliraMp.Enabled)
		{
			return;
		}
		int n = MpTargetingSync.RegisterDeclared(MiliraMp.Control.Assembly, Tag);
		n += MpLambdaSync.Register("Milira.CompFlightControl", "CompGetGizmosExtra", new string[2] { "switchOn", "onlyForMove" }, new string[2] { "Notify_JobStarted", "ForceLand" }, debugOnly: false, Tag, SyncContext.MapSelected);
		n += MpLambdaSync.Register("Milira.CompSwitchResonate", "GetGizmos", new string[2] { "resonateNum", "resonateClass" }, new string[1] { "SetClassHediffToInitial" }, debugOnly: false, Tag);
		n += MpLambdaSync.Register("Milira.CompThingContainer_Milian", "CompGetGizmosExtra", null, new string[1] { "CancelLoad" }, debugOnly: false, Tag);
		n += MpLambdaSync.Register("Milira.PawnColumnWorker_EquipmentMilian", "GetEquipmentFloatMenu", null, new string[1] { "StopAll" }, debugOnly: false, Tag);
		n += MpLambdaSync.Register("Milira.WorldObjectCompMiliraSettlement", "GetCaravanGizmos", null, new string[2] { "RemovePawn", "SetRelation" }, debugOnly: false, Tag);
		n += MpLambdaSync.Register("Milira.CompMilianHairSwitch", "CompGetGizmosExtra", new string[1] { "colorOverride" }, null, debugOnly: false, Tag);
		n += MpLambdaSync.Register("Milira.PawnColumnWorker_MilianHairColor", "DoCell", new string[1] { "colorOverride" }, null, debugOnly: false, Tag);
		n += (MpLambdaSync.Method("Milira.CompMilianHairSwitch:ChangeGraphic", debugOnly: false, Tag) ? 1 : 0);
		n += MpLambdaSync.Register("Milira.CompSpawner_MiliraFeather", "CompGetGizmosExtra", null, new string[2] { "ResetCountdown", "TryDoSpawn" }, debugOnly: true, Tag);
		n += (MpLambdaSync.Method("Milira.CompDelayedPawnSpawnOnWakeup:Spawn", debugOnly: true, Tag) ? 1 : 0);
		Log.Message("[RaidAnimalFix] Milira: " + n + " действий синхронизировано");
	}
}

/// <summary>2. Выбор причёски: номер причёски (сохраняется) — тот же у всех.</summary>
[HarmonyPatch]
public static class MiliraHairStyleNumMp
{
	private static AccessTools.FieldRef<object, int> _num;

	private static bool Prepare()
	{
		return MiliraMp.Enabled;
	}

	private static MethodBase TargetMethod()
	{
		Type t = AccessTools.TypeByName("Milira.CompMilianHairSwitch");
		if (t == null || AccessTools.Field(t, "num") == null)
		{
			return null;
		}
		_num = AccessTools.FieldRefAccess<int>(t, "num");
		return AccessTools.DeclaredMethod(t, "ChangeGraphic", new Type[1] { typeof(int) });
	}

	private static void Postfix(object __instance, int index)
	{
		if (MpHelper.IsMultiplayer() && _num != null)
		{
			_num(__instance) = index;
		}
	}
}

/// <summary>3. «Ручные приоритеты» на вкладке работ милиан — тем же синхронным полем MP, что и у ванильной вкладки.</summary>
[HarmonyPatch]
public static class MiliraManualPrioritiesMp
{
	private static FieldInfo _marker;

	private static bool Prepare()
	{
		_marker = AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.SyncMarkers"), "manualPriorities");
		return MiliraMp.Enabled && _marker != null && _marker.IsStatic;
	}

	private static MethodBase TargetMethod()
	{
		return AccessTools.Method("Milira.MainTabWindow_MilianWork:DoManualPrioritiesCheckbox");
	}

	private static void Prefix(out bool __state)
	{
		__state = MpHelper.IsMultiplayer();
		if (__state)
		{
			_marker.SetValue(null, true);
		}
	}

	private static Exception Finalizer(Exception __exception, bool __state)
	{
		if (__state)
		{
			_marker.SetValue(null, false);
		}
		return __exception;
	}
}

/// <summary>4. Рейды/события Милиры и снаряжение милиан — не по карте камеры клиента.</summary>
[HarmonyPatch]
public static class MiliraSimMapMp
{
	private static readonly string[] ControlMethods = new string[5] { "GameComponentTick", "SendChurchFirstInteract", "SendMilianClusterRaid", "SendMiliraRaid", "SolarCrystalMining" };

	private static bool Prepare()
	{
		return MiliraMp.Enabled;
	}

	private static IEnumerable<MethodBase> TargetMethods()
	{
		Type control = MiliraMp.Control;
		if (control != null)
		{
			foreach (string name in ControlMethods)
			{
				MethodInfo m = AccessTools.DeclaredMethod(control, name);
				if (m != null)
				{
					yield return m;
				}
			}
		}
		MethodInfo gen = AccessTools.Method("Milira.Milira_MilianPawnGenerator_Patch:Postfix");
		if (gen != null)
		{
			yield return gen;
		}
	}

	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
	{
		return MpSimMap.Rewrite(instructions, __originalMethod, "Milira");
	}
}

/// <summary>6. pawnInColony сохраняется вместе с остальным состоянием сюжета.</summary>
[HarmonyPatch]
public static class MiliraPawnInColonyScribe
{
	private static AccessTools.FieldRef<object, Pawn> _pawnInColony;

	private static bool Prepare()
	{
		return MiliraMp.Enabled;
	}

	private static MethodBase TargetMethod()
	{
		Type t = MiliraMp.Control;
		if (t == null || AccessTools.Field(t, "pawnInColony") == null)
		{
			return null;
		}
		_pawnInColony = AccessTools.FieldRefAccess<Pawn>(t, "pawnInColony");
		return AccessTools.DeclaredMethod(t, "ExposeData");
	}

	private static void Postfix(object __instance)
	{
		Scribe_References.Look(ref _pawnInColony(__instance), "pawnInColony", saveDestroyedThings: true);
	}
}

/// <summary>7. Окно настроек Milira во время MP-игры заблокировано.</summary>
[HarmonyPatch]
public static class MiliraSettingsLockMp
{
	private static bool Prepare()
	{
		return MiliraMp.Present;
	}

	private static MethodBase TargetMethod()
	{
		Type t = AccessTools.TypeByName("Milira.Mod_MiliraRace");
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
		Widgets.Label(inRect, "[Desync Fix] " + "RAF_SimSettingsLocked".Translate("Milira Race").Resolve());
		Text.Font = font;
		return false;
	}
}
