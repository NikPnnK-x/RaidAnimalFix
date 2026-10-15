using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RaidAnimalFix;

internal static class FiltersSafePatcher
{
	public static void PatchAllSafe(Harmony harmony, Assembly asm, string tag)
	{
		Type[] array;
		try
		{
			array = asm.GetTypes();
		}
		catch (ReflectionTypeLoadException ex)
		{
			List<Type> list = new List<Type>();
			if (ex.Types != null)
			{
				Type[] types = ex.Types;
				foreach (Type type in types)
				{
					if (type != null)
					{
						list.Add(type);
					}
				}
			}
			array = list.ToArray();
		}
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		Type[] array2 = array;
		foreach (Type type2 in array2)
		{
			if (type2.GetCustomAttributes(typeof(HarmonyPatch), inherit: true).Length == 0)
			{
				continue;
			}
			MethodInfo method = type2.GetMethod("TargetMethod", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
			if (method != null && typeof(MethodBase).IsAssignableFrom(method.ReturnType))
			{
				object obj = null;
				try
				{
					obj = method.Invoke(null, null);
				}
				catch
				{
				}
				if (obj == null)
				{
					num2++;
					Log.Message("[RaidAnimalFix] " + tag + ": " + type2.Name + " skipped (target method not found)");
					continue;
				}
			}
			else if (!AnyTargetMethods(type2))
			{
				// TargetMethods() без целей (нужного мода нет) — Harmony счёл бы это ошибкой.
				num2++;
				Log.Message("[RaidAnimalFix] " + tag + ": " + type2.Name + " skipped (target method not found)");
				continue;
			}
			try
			{
				PatchClassProcessor patchClassProcessor = harmony.CreateClassProcessor(type2);
				patchClassProcessor.Patch();
				num++;
			}
			catch (Exception ex2)
			{
				num3++;
				Log.Warning("[RaidAnimalFix] " + tag + ": " + type2.Name + " FAILED: " + ex2.Message);
				PatchFailureCleanup.UnpatchClass(harmony, type2);
			}
		}
		Log.Message("[RaidAnimalFix] " + tag + ": patched " + num + ", skipped " + num2 + ", failed " + num3);
	}

	/// <summary>Есть ли у класса хоть одна цель в TargetMethods(). Нет такого метода или он упал — решает Harmony.</summary>
	private static bool AnyTargetMethods(Type type)
	{
		MethodInfo method = type.GetMethod("TargetMethods", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
		if (method == null || !typeof(IEnumerable<MethodBase>).IsAssignableFrom(method.ReturnType))
		{
			return true;
		}
		try
		{
			if (!(method.Invoke(null, null) is IEnumerable<MethodBase> targets))
			{
				return false;
			}
			foreach (MethodBase target in targets)
			{
				if (target != null)
				{
					return true;
				}
			}
			return false;
		}
		catch
		{
			return true;
		}
	}
}
