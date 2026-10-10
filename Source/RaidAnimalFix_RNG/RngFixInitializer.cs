using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RaidAnimalFix;

[StaticConstructorOnStartup]
public static class RngFixInitializer
{
	static RngFixInitializer()
	{
		try
		{
			Harmony harmony = new Harmony("customfix.raidanimalfix.rng");
			RngSafePatcher.PatchAllSafe(harmony, Assembly.GetExecutingAssembly(), "RNG isolation (3rd-party visuals)");
		}
		catch (Exception ex)
		{
			Log.Error("[RaidAnimalFix] RngFixInitializer error: " + ex.Message);
		}
	}
}
