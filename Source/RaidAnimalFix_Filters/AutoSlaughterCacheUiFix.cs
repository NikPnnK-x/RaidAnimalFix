using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RaidAnimalFix;

[HarmonyPatch(typeof(AutoSlaughterManager))]
[HarmonyPatch("AnimalsToSlaughter", MethodType.Getter)]
public static class AutoSlaughterCacheUiFix
{
	private static FieldInfo _cacheDirty;

	private static bool _resolved;

	private static FieldInfo DirtyField()
	{
		if (!_resolved)
		{
			_resolved = true;
			try
			{
				_cacheDirty = AccessTools.Field(typeof(AutoSlaughterManager), "cacheDirty");
			}
			catch
			{
			}
		}
		return _cacheDirty;
	}

	[HarmonyPrefix]
	public static void Prefix(AutoSlaughterManager __instance, ref bool __state)
	{
		__state = false;
		try
		{
			if (__instance != null && MpHelper.IsMultiplayer() && UiOnly.InInterface())
			{
				FieldInfo fieldInfo = DirtyField();
				if (!(fieldInfo == null) && (bool)fieldInfo.GetValue(__instance))
				{
					fieldInfo.SetValue(__instance, false);
					__state = true;
				}
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("AutoSlaughterCacheUiFix", ex);
		}
	}

	// Finalizer, а не Postfix: флаг возвращается даже если геттер бросил.
	[HarmonyFinalizer]
	public static void Postfix(AutoSlaughterManager __instance, bool __state)
	{
		if (!__state)
		{
			return;
		}
		try
		{
			FieldInfo fieldInfo = DirtyField();
			if (fieldInfo != null && __instance != null)
			{
				fieldInfo.SetValue(__instance, true);
			}
		}
		catch
		{
		}
	}
}
