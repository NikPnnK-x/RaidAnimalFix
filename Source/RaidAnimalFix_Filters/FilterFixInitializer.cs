using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RaidAnimalFix;

[StaticConstructorOnStartup]
public static class FilterFixInitializer
{
	static FilterFixInitializer()
	{
		try
		{
			Log.Message("[RaidAnimalFix] Filters version " + Assembly.GetExecutingAssembly().GetName().Version);
			Harmony harmony = new Harmony("customfix.raidanimalfix.filters");
			FiltersSafePatcher.PatchAllSafe(harmony, Assembly.GetExecutingAssembly(), "Filter patches (FIX 84-121)");
			MapTradeRequestFulfill.EnsureSyncRegistered();
			MpApiRegistry.RegisterAll();
			MpUsernameFix.ApplyNow();
		}
		catch (Exception ex)
		{
			Log.Error("[RaidAnimalFix] FilterFixInitializer error: " + ex.Message);
		}
	}
}
