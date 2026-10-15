using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RaidAnimalFix;

// VANILLA GRAVSHIP EXPANDED — CHAPTER 2 + Multiplayer.
// Таблица совместимости MP (11.10.2026): «рассинхрон при прибытии вражеского корабля:
// у хоста и клиента разный экипаж». Карту вражеской платформы (вместе с экипажем) мод
// генерирует в АСИНХРОННОМ long event, поставленном из симуляции — в фоновом потоке и
// в момент, разный у каждого клиента. Это закрывает наш LongEventContextFix (события из
// симуляции в MP выполняются синхронно, в контексте фракции и часов, со своим
// детерминированным генератором случайных чисел).
// Здесь — второе место: бафф точности турелей вешается отложенно (ExecuteWhenFinished)
// с проверкой «Faction == Faction.OfPlayer». Если турель появилась внутри long event
// (посадка корабля, генерация карты), проверка выполняется вне симуляции, где «игрок» у
// каждого клиента свой → до 250 тиков (следующего повторного применения) игроки
// стреляют с разной точностью. В MP «игрок» = любая фракция игрока — одинаково у всех.

[HarmonyPatch]
public static class Vge2TurretBuffFactionMp
{
	private static FieldInfo _turret;

	private static MethodInfo _apply;

	private static bool Prepare()
	{
		return RafTypes.Find("VanillaGravshipExpanded2.Building_TurretGun_SpawnSetup_Patch") != null;
	}

	private static MethodBase TargetMethod()
	{
		Type outer = RafTypes.Find("VanillaGravshipExpanded2.Building_TurretGun_SpawnSetup_Patch");
		_apply = RafTypes.Method("VanillaGravshipExpanded2.WarcomputerHelper:ApplyForcedMissRadiusBuffs");
		if (outer == null || _apply == null)
		{
			return null;
		}
		foreach (Type nt in outer.GetNestedTypes(AccessTools.all))
		{
			FieldInfo f = AccessTools.Field(nt, "__instance");
			MethodInfo m = AccessTools.DeclaredMethod(nt, "<Postfix>b__0");
			if (f != null && m != null && typeof(Building_TurretGun).IsAssignableFrom(f.FieldType))
			{
				_turret = f;
				return m;
			}
		}
		return null;
	}

	private static bool Prefix(object __instance)
	{
		if (!MpHelper.IsMultiplayer())
		{
			return true;
		}
		try
		{
			if (_turret.GetValue(__instance) is Building_TurretGun turret && turret.Spawned && RimExodusMp.IsPlayerFaction(turret.Faction))
			{
				_apply.Invoke(null, new object[1] { turret });
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] VGE2 turret buff (MP): " + (ex.InnerException ?? ex).Message);
		}
		return false;
	}
}
