using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RaidAnimalFix;

[HarmonyPatch]
public static class BloodNoFilthFix
{
	private static MethodBase TargetMethod()
	{
		if (RafTypes.Find("BloodAnimations.FleckThrownBlood") == null)
		{
			return null;
		}
		return AccessTools.Method(typeof(FilthMaker), "TryMakeFilth", new Type[7]
		{
			typeof(IntVec3),
			typeof(Map),
			typeof(ThingDef),
			typeof(IEnumerable<string>),
			typeof(bool),
			typeof(Filth).MakeByRefType(),
			typeof(FilthSourceFlags)
		});
	}

	private static bool Prefix(ref Filth outFilth, ref bool __result)
	{
		if (!BloodAnimGuard.Inside)
		{
			return true;
		}
		outFilth = null;
		__result = false;
		return false;
	}
}
