using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RaidAnimalFix;

[HarmonyPatch(typeof(Messages))]
[HarmonyPatch("Message", new Type[]
{
	typeof(Message),
	typeof(bool)
})]
public static class MessageFilterFix2
{
	private static FieldInfo _liveMessagesField;

	private static bool _liveMessagesResolved;

	private static void Prefix(Message msg, out bool __state)
	{
		__state = false;
		if (MpHelper.IsMultiplayer() && msg != null)
		{
			try
			{
				__state = NotificationFilter.ShouldHide(msg.lookTargets, out var _);
			}
			catch
			{
				__state = false;
			}
			if (__state)
			{
				UiSoundMute.Depth++;
			}
		}
	}

	private static void Finalizer(Message msg, bool __state)
	{
		if (!__state)
		{
			return;
		}
		if (UiSoundMute.Depth > 0)
		{
			UiSoundMute.Depth--;
		}
		try
		{
			if (!_liveMessagesResolved)
			{
				_liveMessagesResolved = true;
				_liveMessagesField = AccessTools.Field(typeof(Messages), "liveMessages");
			}
			if (!(_liveMessagesField == null) && _liveMessagesField.GetValue(null) is List<Message> list)
			{
				list.Remove(msg);
			}
		}
		catch
		{
		}
	}
}
