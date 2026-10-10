using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RaidAnimalFix;

[HarmonyPatch]
public static class MpQuestTickOrderFix
{
	public static MethodBase TargetMethod()
	{
		Type type = AccessTools.TypeByName("Multiplayer.Client.Comp.MultiplayerAsyncQuest");
		if (type == null)
		{
			return null;
		}
		return AccessTools.Method(type, "TickQuests", new Type[1] { typeof(IEnumerable<Quest>) });
	}

	[HarmonyPrefix]
	public static bool Prefix(IEnumerable<Quest> quests)
	{
		if (quests == null)
		{
			return false;
		}
		List<Quest> list = new List<Quest>();
		try
		{
			foreach (Quest quest in quests)
			{
				if (quest != null)
				{
					list.Add(quest);
				}
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("MpQuestTickOrderFix.snapshot", ex);
			return true;
		}
		list.Sort((Quest a, Quest b) => a.id.CompareTo(b.id));
		for (int i = 0; i < list.Count; i++)
		{
			try
			{
				list[i].QuestTick();
			}
			catch (Exception ex2)
			{
				Log.Error("[RaidAnimalFix] QuestTick failed for quest " + list[i].id + " (" + list[i].name + "): " + ex2);
			}
		}
		return false;
	}
}
