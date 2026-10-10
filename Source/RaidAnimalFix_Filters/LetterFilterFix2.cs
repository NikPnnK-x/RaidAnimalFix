using System;
using HarmonyLib;
using Verse;

namespace RaidAnimalFix;

[HarmonyPatch("ReceiveLetter", new Type[]
{
	typeof(Letter),
	typeof(string),
	typeof(int),
	typeof(bool)
})]
[HarmonyPatch(typeof(LetterStack))]
public static class LetterFilterFix2
{
	[ThreadStatic]
	private static bool _hideCurrent;

	[ThreadStatic]
	private static string _hideReason;

	private static void Prefix(Letter let, int delayTicks, ref bool playSound)
	{
		_hideCurrent = false;
		_hideReason = null;
		if (!MpHelper.IsMultiplayer() || let == null || delayTicks > 0)
		{
			return;
		}
		try
		{
			if (NotificationFilter.ShouldHide(let.lookTargets, out var reason))
			{
				_hideCurrent = true;
				_hideReason = reason;
				playSound = false;
			}
		}
		catch
		{
			_hideCurrent = false;
		}
	}

	private static void Postfix(LetterStack __instance, Letter let)
	{
		if (!_hideCurrent)
		{
			return;
		}
		_hideCurrent = false;
		try
		{
			if (__instance != null && let != null && __instance.LettersListForReading.Remove(let))
			{
				Log.Message("[RaidAnimalFix] LetterFilter: hidden letter '" + let.Label + "' (" + _hideReason + ")");
			}
		}
		catch
		{
		}
	}
}
