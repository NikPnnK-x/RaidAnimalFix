using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RaidAnimalFix;

internal static class BloodAnimGuard
{
	[ThreadStatic]
	private static int _depth;

	public static bool Inside => _depth > 0;

	public static void Begin(ref bool state)
	{
		state = false;
		if (MpHelper.IsMultiplayer())
		{
			Rand.PushState();
			_depth++;
			state = true;
		}
	}

	public static void End(bool state)
	{
		if (state)
		{
			if (_depth > 0)
			{
				_depth--;
			}
			Rand.PopState();
		}
	}

	public static MethodBase Entry(string typeName, string methodName)
	{
		Type type = AccessTools.TypeByName(typeName);
		if (type == null)
		{
			return null;
		}
		return AccessTools.Method(type, methodName);
	}
}
