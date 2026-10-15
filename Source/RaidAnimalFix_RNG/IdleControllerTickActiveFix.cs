using System;
using System.Reflection;
using HarmonyLib;

namespace RaidAnimalFix;

[HarmonyPatch]
public static class IdleControllerTickActiveFix
{
	private static MethodBase TargetMethod()
	{
		Type type = RafTypes.Find("AM.Idle.IdleControllerComp");
		if (type == null)
		{
			return null;
		}
		return AccessTools.Method(type, "TickActive");
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
