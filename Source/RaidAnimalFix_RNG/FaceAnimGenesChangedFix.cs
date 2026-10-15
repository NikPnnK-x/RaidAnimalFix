using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RaidAnimalFix;

[HarmonyPatch]
public static class FaceAnimGenesChangedFix
{
	private static MethodBase TargetMethod()
	{
		Type type = RafTypes.Find("FacialAnimation.HarmonyPatches");
		if (type == null)
		{
			return null;
		}
		return AccessTools.Method(type, "Prefix_Notify_GenesChanged");
	}

	private static void Prefix(Pawn __0, ref bool __state)
	{
		RngIsolation.BeginSeeded(__0?.thingIDNumber ?? 0, ref __state);
	}

	private static void Finalizer(bool __state)
	{
		RngIsolation.End(__state);
	}
}
