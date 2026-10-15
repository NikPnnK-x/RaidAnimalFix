using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RaidAnimalFix;

// HUMANOID ALIEN RACES (erdelf.HumanoidAlienRaces) + Multiplayer Compatibility.
// Стилист-станцию для рас HAR синхронизирует модуль Multiplayer Compatibility (AlienRace):
// после «Принять» цвета каналов, варианты и цвета частей тела уходят командой. Но он ищет
// AlienComp.OverwriteColorChannel(string, Color, Color), а в нынешнем HAR параметры
// Color? — метод не находится, и команда у всех падает на первом же канале: внешность
// инопланетян со станции не применяется. Подставляем в модуль актуальную перегрузку
// (MethodInfo.Invoke сам упаковывает Color в Color?).

internal static class HarMpCompatFix
{
	private static bool _logged;

	public static void Apply()
	{
		try
		{
			Type module = AccessTools.TypeByName("Multiplayer.Compat.AlienRace");
			Type comp = AccessTools.TypeByName("AlienRace.AlienPartGenerator+AlienComp");
			FieldInfo field = (module == null) ? null : AccessTools.Field(module, "overwriteColorChannelMethod");
			if (comp == null || field == null || !field.IsStatic || field.GetValue(null) != null)
			{
				return;
			}
			MethodInfo current = AccessTools.Method(comp, "OverwriteColorChannel", new Type[3]
			{
				typeof(string),
				typeof(Color?),
				typeof(Color?)
			});
			if (current == null)
			{
				return;
			}
			field.SetValue(null, current);
			if (!_logged)
			{
				_logged = true;
				Log.Message("[RaidAnimalFix] HAR: Multiplayer Compatibility styling-station sync repaired (OverwriteColorChannel with Color?)");
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] HAR fix failed: " + ex.Message);
		}
	}
}

/// <summary>Если модуль MPCompat ещё не отработал — чиним сразу после его LatePatch.</summary>
[HarmonyPatch]
public static class HarMpCompatLatePatchFix
{
	private static MethodBase TargetMethod()
	{
		return AccessTools.Method("Multiplayer.Compat.AlienRace:LatePatch");
	}

	private static void Postfix()
	{
		HarMpCompatFix.Apply();
	}
}
