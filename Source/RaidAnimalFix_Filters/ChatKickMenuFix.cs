using System;
using System.Reflection;
using HarmonyLib;

namespace RaidAnimalFix;

[HarmonyPatch]
public static class ChatKickMenuFix
{
	private static MethodBase TargetMethod()
	{
		Type type = RafTypes.Find("Multiplayer.Client.ChatWindow");
		if (!(type == null))
		{
			return AccessTools.Method(type, "ClickPlayer");
		}
		return null;
	}

	[HarmonyPostfix]
	public static void Postfix(object[] __args)
	{
		if (__args != null && __args.Length > 0)
		{
			MpJoinSession.OnClickPlayer(__args[0]);
		}
	}
}
