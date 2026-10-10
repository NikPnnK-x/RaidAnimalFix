using System;
using System.Reflection;
using HarmonyLib;

namespace RaidAnimalFix;

[HarmonyPatch]
public static class FingerprintFlushOnDesync
{
	private static MethodBase TargetMethod()
	{
		Type type = AccessTools.TypeByName("Multiplayer.Client.SyncCoordinator");
		if (!(type == null))
		{
			return AccessTools.Method(type, "HandleDesync");
		}
		return null;
	}

	[HarmonyPrefix]
	public static void Prefix()
	{
		try
		{
			StateFingerprintProbe.FlushNow();
		}
		catch
		{
		}
	}
}
