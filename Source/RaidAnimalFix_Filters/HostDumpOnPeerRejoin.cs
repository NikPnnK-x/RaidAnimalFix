using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RaidAnimalFix;

[HarmonyPatch]
public static class HostDumpOnPeerRejoin
{
	private static bool _armed = true;

	private static int _lastAt = -100000;

	private static MethodBase TargetMethod()
	{
		Type type = AccessTools.TypeByName("Multiplayer.Client.TickPatch");
		if (!(type == null))
		{
			return AccessTools.Method(type, "DoUpdate");
		}
		return null;
	}

	[HarmonyPostfix]
	public static void Postfix()
	{
		try
		{
			if (!MpHelper.IsMultiplayer() || !MpJoinSession.IsHost())
			{
				return;
			}
			if (!MpJoinSession.TryGetJoiner(out var display, out var _))
			{
				_armed = true;
			}
			else if (_armed)
			{
				int num = ((Find.TickManager != null) ? Find.TickManager.TicksGame : 0);
				if (num - _lastAt >= 60)
				{
					_armed = false;
					_lastAt = num;
					Log.Message("[RaidAnimalFix] " + display + " перезаходит — выкладываем именной снимок хоста для сравнения");
					StateFingerprintProbe.FlushNow();
				}
			}
		}
		catch
		{
		}
	}
}
