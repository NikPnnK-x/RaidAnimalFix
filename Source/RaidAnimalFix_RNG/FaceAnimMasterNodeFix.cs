using System;
using System.Reflection;
using HarmonyLib;

namespace RaidAnimalFix;

[HarmonyPatch]
public static class FaceAnimMasterNodeFix
{
	private static MethodBase TargetMethod()
	{
		Type type = RafTypes.Find("FacialAnimation.NLFacialAnimationMasterNode");
		if (type == null)
		{
			return null;
		}
		return AccessTools.Method(type, "UpdateAnimation");
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
