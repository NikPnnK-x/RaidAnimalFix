using System;
using System.Reflection;
using HarmonyLib;

namespace RaidAnimalFix;

[HarmonyPatch]
public static class MpUsernameFixPatch
{
	private static MethodBase TargetMethod()
	{
		Type type = RafTypes.Find("Multiplayer.Client.MultiplayerStatic");
		if (!(type == null))
		{
			return AccessTools.Method(type, "SetUsername");
		}
		return null;
	}

	[HarmonyPostfix]
	public static void Postfix()
	{
		MpUsernameFix.ApplyNow();
	}
}
