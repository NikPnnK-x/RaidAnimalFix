using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RaidAnimalFix;

[HarmonyPatch]
public static class FaceAnimRenderNodesFix
{
	private static MethodBase TargetMethod()
	{
		Type type = RafTypes.Find("FacialAnimation.DrawFaceGraphicsComp");
		if (type == null)
		{
			return null;
		}
		return AccessTools.Method(type, "CompRenderNodes");
	}

	private static void Prefix(ThingComp __instance, ref bool __state)
	{
		RngIsolation.BeginSeeded(RngIsolation.SeedOf(__instance), ref __state);
	}

	private static void Finalizer(bool __state)
	{
		RngIsolation.End(__state);
	}
}
