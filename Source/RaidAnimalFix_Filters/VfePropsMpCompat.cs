using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RaidAnimalFix;

// VANILLA FURNITURE EXPANDED — PROPS AND DECOR (VanillaExpanded.VFEPropsandDecor) + Multiplayer.
// В таблице совместимости MP это записано за Combat Extended: «рассинхрон, когда огонь
// прометиума задевает лужи химтоплива». Огонь уничтожает реквизит, VFE Props возвращает
// за него серебро и отстраивает заново — а здесь всё считается по-разному у игроков:
//  1. Utils.CostCalculator возвращает 0, если у игрока включён режим разработчика
//     (Prefs.DevMode — свой у каждого) → стоимость и возврат серебра расходятся.
//     → в MP режим разработчика на стоимость не влияет.
//  2. Building_SubstractsSilver проверяет и списывает серебро со складов Find.CurrentMap —
//     карты, на которую смотрит клиент, а не карты реквизита. → в MP — карта реквизита.
//  3. Множители стоимости/возврата — static-настройки у каждого свои. → сверка настроек
//     (MpFeatures) и блокировка окна во время MP-игры.

/// <summary>1. Стоимость реквизита не зависит от режима разработчика клиента.</summary>
[HarmonyPatch]
public static class VfePropsCostDevModeMp
{
	private static MethodBase TargetMethod()
	{
		return RafTypes.Method("VFEProps.Utils:CostCalculator");
	}

	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo devMode = AccessTools.PropertyGetter(typeof(Prefs), "DevMode");
		int n = 0;
		foreach (CodeInstruction ci in instructions)
		{
			if (ci.Calls(devMode))
			{
				ci.operand = AccessTools.Method(typeof(VfePropsCostDevModeMp), "DevModeForCost");
				n++;
			}
			yield return ci;
		}
		if (n == 0)
		{
			Log.Warning("[RaidAnimalFix] VFE Props CostCalculator: Prefs.DevMode not found (mod updated?)");
		}
	}

	public static bool DevModeForCost()
	{
		return !MpHelper.IsMultiplayer() && Prefs.DevMode;
	}
}

/// <summary>2. Серебро за реквизит — со складов карты реквизита, а не камеры клиента.</summary>
[HarmonyPatch]
public static class VfePropsSilverMapMp
{
	private static IEnumerable<MethodBase> TargetMethods()
	{
		Type t = RafTypes.Find("VFEProps.Building_SubstractsSilver");
		if (t == null)
		{
			yield break;
		}
		foreach (string name in new string[2] { "CheckSilverInMap", "RemoveSilverFromMap" })
		{
			MethodInfo m = AccessTools.DeclaredMethod(t, name);
			if (m != null && !m.IsStatic)
			{
				yield return m;
			}
		}
	}

	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo currentMap = AccessTools.PropertyGetter(typeof(Find), "CurrentMap");
		foreach (CodeInstruction ci in instructions)
		{
			if (ci.Calls(currentMap))
			{
				// стек: [] → [this] → [MapFor(this)]
				CodeInstruction self = new CodeInstruction(OpCodes.Ldarg_0);
				self.labels.AddRange(ci.labels);
				ci.labels.Clear();
				yield return self;
				ci.operand = AccessTools.Method(typeof(VfePropsSilverMapMp), "MapFor");
			}
			yield return ci;
		}
	}

	public static Map MapFor(Thing building)
	{
		return (MpHelper.IsMultiplayer() && building?.Map != null) ? building.Map : Find.CurrentMap;
	}
}

/// <summary>3. Окно настроек VFE Props во время MP-игры заблокировано.</summary>
[HarmonyPatch]
public static class VfePropsSettingsLockMp
{
	private static MethodBase TargetMethod()
	{
		Type t = RafTypes.Find("VFEProps.VFEProps_Mod");
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
		Widgets.Label(inRect, "[Desync Fix] " + "RAF_SimSettingsLocked".Translate("VFE Props and Decor").Resolve());
		Text.Font = font;
		return false;
	}
}
