using System;
using System.Reflection;
using HarmonyLib;

namespace RaidAnimalFix;

[HarmonyPatch]
public static class FaceAnimResetFix
{
	private static MethodBase TargetMethod()
	{
		Type type = RafTypes.Find("FacialAnimation.FaceAnimation");
		if (type == null)
		{
			return null;
		}
		return AccessTools.Method(type, "Reset");
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
