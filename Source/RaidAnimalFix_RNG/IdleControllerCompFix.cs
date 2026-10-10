using System;
using System.Reflection;
using HarmonyLib;

namespace RaidAnimalFix;

[HarmonyPatch]
public static class IdleControllerCompFix
{
	private static MethodBase TargetMethod()
	{
		Type type = AccessTools.TypeByName("AM.Idle.IdleControllerComp");
		if (type == null)
		{
			return null;
		}
		return AccessTools.Method(type, "HandleStartingFlavourAnim");
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
