using System;
using System.Reflection;
using HarmonyLib;

namespace RaidAnimalFix;

[HarmonyPatch]
public static class HoldTicksWhileJoining
{
	private static MethodBase TargetMethod()
	{
		Type type = RafTypes.Find("Multiplayer.Client.TickPatch");
		if (!(type == null))
		{
			return AccessTools.Method(type, "DoUpdate");
		}
		return null;
	}

	[HarmonyPrefix]
	public static bool Prefix(ref bool worked)
	{
		try
		{
			if (!MpJoinSession.ShouldHoldTicks())
			{
				return true;
			}
			worked = false;
			return false;
		}
		catch
		{
			return true;
		}
	}
}
