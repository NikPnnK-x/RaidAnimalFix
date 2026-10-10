using System.Reflection;
using HarmonyLib;
using Verse;

namespace RaidAnimalFix;

internal static class MeleeAnimGuard
{
	private static bool _logged;

	private static FieldInfo _defField;

	private static bool _defFieldResolved;

	public static bool ChangesWorldState(object animRenderer)
	{
		try
		{
			if (!_defFieldResolved)
			{
				_defFieldResolved = true;
				_defField = AccessTools.Field(animRenderer.GetType(), "Def");
			}
			if (_defField == null)
			{
				return false;
			}
			object value = _defField.GetValue(animRenderer);
			if (value == null)
			{
				return false;
			}
			FieldInfo fieldInfo = AccessTools.Field(value.GetType(), "type");
			if (fieldInfo == null)
			{
				return false;
			}
			object value2 = fieldInfo.GetValue(value);
			return value2 != null && value2.ToString() != "Idle";
		}
		catch
		{
			return false;
		}
	}

	public static void LogOnce()
	{
		if (!_logged)
		{
			_logged = true;
			Log.Warning("[RaidAnimalFix] Melee Animation: decorative mode in multiplayer. Executions, duels, lassos and unique skills are disabled because their timeline runs on the render loop (frame rate, current map only) and their events change world state. Idle animations and weapon drawing still work; melee combat falls back to vanilla.");
		}
	}
}
