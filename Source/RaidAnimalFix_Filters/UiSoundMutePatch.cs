using System;
using HarmonyLib;
using Verse;
using Verse.Sound;

namespace RaidAnimalFix;

[HarmonyPatch(typeof(SoundStarter))]
[HarmonyPatch("PlayOneShotOnCamera", new Type[]
{
	typeof(SoundDef),
	typeof(Map)
})]
public static class UiSoundMutePatch
{
	private static bool Prefix()
	{
		return UiSoundMute.Depth <= 0;
	}
}
