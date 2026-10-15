using System;
using System.Reflection;
using HarmonyLib;

namespace RaidAnimalFix;

[HarmonyPatch]
public static class JoinHoldBannerFix
{
	private static MethodBase TargetMethod()
	{
		Type type = RafTypes.Find("Multiplayer.Client.IngameUIPatch");
		if (!(type == null))
		{
			return AccessTools.Method(type, "Prefix");
		}
		return null;
	}

	[HarmonyPostfix]
	public static void Postfix()
	{
		MpJoinSession.DrawBanner();
	}
}
