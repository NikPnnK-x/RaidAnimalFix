using System.Reflection;
using HarmonyLib;
using Verse;

namespace RaidAnimalFix;

[HarmonyPatch("Add")]
[HarmonyPatch(typeof(WindowStack))]
public static class DesyncWindowInterceptor
{
	private static bool Prefix(Window window)
	{
		try
		{
			if (window == null || window.GetType().Name != "DesyncedWindow")
			{
				return true;
			}
			if (!MpHelper.IsMultiplayer())
			{
				return true;
			}
			string text = null;
			try
			{
				FieldInfo field = window.GetType().GetField("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (field != null)
				{
					text = field.GetValue(window) as string;
				}
			}
			catch
			{
			}
			Log.Warning("[RaidAnimalFix] Multiplayer desync window: " + (text ?? "(no text)"));
			ModInitializer.EnsureDesyncMonitor();
			DesyncMonitor.NoteDesyncForAnalysis();
			DesyncMonitor desyncMonitor = ((Current.Game != null) ? Current.Game.GetComponent<DesyncMonitor>() : null);
			if (desyncMonitor != null && desyncMonitor.AutoResyncAvailable())
			{
				desyncMonitor.Arm("DesyncedWindow");
			}
			return true;
		}
		catch
		{
			return true;
		}
	}
}
