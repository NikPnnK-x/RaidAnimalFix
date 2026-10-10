using System;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;

namespace RaidAnimalFix;

[HarmonyPatch(typeof(Settlement))]
[HarmonyPatch("Label", MethodType.Getter)]
public static class PlayerSettlementLabelFix
{
	[HarmonyPostfix]
	public static void Postfix(Settlement __instance, ref string __result)
	{
		try
		{
			if (!MpHelper.IsMultiplayer())
			{
				return;
			}
			Faction faction = __instance.Faction;
			if (!MpContext.IsRealPlayerFaction(faction))
			{
				return;
			}
			string name = faction.Name;
			if (!string.IsNullOrEmpty(name))
			{
				if (__result == null)
				{
					__result = name;
				}
				else if (__result.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0)
				{
					__result = __result + " (" + name + ")";
				}
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("PlayerSettlementLabelFix", ex);
		}
	}
}
