using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RaidAnimalFix;

[StaticConstructorOnStartup]
public static class ModInitializer
{
	static ModInitializer()
	{
		try
		{
			Harmony harmony = new Harmony("customfix.raidanimalfix");
			MainSafePatcher.PatchAllSafe(harmony, Assembly.GetExecutingAssembly(), "Core patches");
			LongEventHandler.ExecuteWhenFinished(delegate
			{
				EnsureDesyncMonitor();
			});
			Log.Message("[RaidAnimalFix] Mod initialized (core)");
		}
		catch (Exception ex)
		{
			Log.Error("[RaidAnimalFix] Failed to initialize: " + ex);
		}
	}

	public static void EnsureDesyncMonitor()
	{
		try
		{
			if (Current.Game != null && Current.Game.GetComponent<DesyncMonitor>() == null)
			{
				Current.Game.components.Add(new DesyncMonitor(Current.Game));
				Log.Message("[RaidAnimalFix] DesyncMonitor component added");
			}
		}
		catch (Exception ex)
		{
			Log.Error("[RaidAnimalFix] EnsureDesyncMonitor error: " + ex.Message);
		}
	}
}
