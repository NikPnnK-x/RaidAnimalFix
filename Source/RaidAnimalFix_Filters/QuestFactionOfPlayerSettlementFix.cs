using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;

namespace RaidAnimalFix;

[HarmonyPatch(typeof(QuestNode_GetFactionOf))]
[HarmonyPatch("DoWork")]
public static class QuestFactionOfPlayerSettlementFix
{
	private static void Postfix(QuestNode_GetFactionOf __instance, Slate slate)
	{
		if (!MpHelper.IsMultiplayer())
		{
			return;
		}
		try
		{
			string value = __instance.storeAs.GetValue(slate);
			if (!string.IsNullOrEmpty(value) && slate.Get<Faction>(value) == null)
			{
				Faction faction = slate.Get<WorldObject>("settlement")?.Faction;
				if (faction != null)
				{
					slate.Set(value, faction);
				}
			}
		}
		catch
		{
		}
	}
}
