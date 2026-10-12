using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RaidAnimalFix;

/// <summary>
/// Если Harmony упал на одной из целей патч-класса, уже наложенные части и
/// битая замена (невалидный IL) остаются в Harmony. Трассировщик рассинхрона
/// MP перебирает все замены и падает на битой посреди тика — у каждого
/// клиента в свой момент → рассинхрон (Desync-69). После ошибки снимаем всё,
/// что этот класс успел наложить: Harmony пересоберёт методы без наших патчей.
/// </summary>
public static class PatchFailureCleanup
{
	public static void UnpatchClass(Harmony harmony, Type patchClass)
	{
		if (harmony == null || patchClass == null)
		{
			return;
		}
		int num = 0;
		try
		{
			foreach (MethodBase item in Harmony.GetAllPatchedMethods().ToList())
			{
				HarmonyLib.Patches patchInfo = Harmony.GetPatchInfo(item);
				if (patchInfo == null)
				{
					continue;
				}
				foreach (Patch item2 in patchInfo.Prefixes.Concat(patchInfo.Postfixes).Concat(patchInfo.Transpilers).Concat(patchInfo.Finalizers).ToList())
				{
					if (item2.owner == harmony.Id && item2.PatchMethod?.DeclaringType == patchClass)
					{
						try
						{
							harmony.Unpatch(item, item2.PatchMethod);
							num++;
						}
						catch (Exception ex)
						{
							Log.Warning("[RaidAnimalFix] cleanup unpatch " + item.DeclaringType?.Name + "." + item.Name + ": " + ex.Message);
						}
					}
				}
			}
		}
		catch (Exception ex2)
		{
			Log.Warning("[RaidAnimalFix] cleanup after failed patch " + patchClass.Name + ": " + ex2.Message);
		}
		if (num > 0)
		{
			Log.Warning("[RaidAnimalFix] " + patchClass.Name + ": снято " + num + " частично наложенных патчей после ошибки");
		}
	}
}
