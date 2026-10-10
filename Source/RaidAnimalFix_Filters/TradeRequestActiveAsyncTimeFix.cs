using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RaidAnimalFix;

[HarmonyPatch(typeof(TradeRequestComp))]
[HarmonyPatch("ActiveRequest", MethodType.Getter)]
public static class TradeRequestActiveAsyncTimeFix
{
	private static void Postfix(TradeRequestComp __instance, ref bool __result)
	{
		if (!MpHelper.IsMultiplayer())
		{
			return;
		}
		try
		{
			if (__instance.expiration <= 0 || __instance.requestThingDef == null)
			{
				__result = false;
			}
			else
			{
				__result = HasOngoingQuest(__instance.parent);
			}
		}
		catch
		{
		}
	}

	public static bool HasOngoingQuest(WorldObject settlement)
	{
		return HasOngoingQuestOtherThan(settlement, null);
	}

	public static bool HasOngoingQuestOtherThan(WorldObject settlement, Quest except)
	{
		if (settlement == null || Find.QuestManager == null)
		{
			return false;
		}
		List<Quest> questsListForReading = Find.QuestManager.QuestsListForReading;
		for (int i = 0; i < questsListForReading.Count; i++)
		{
			Quest quest = questsListForReading[i];
			if (quest == null || quest == except || quest.State != QuestState.Ongoing)
			{
				continue;
			}
			List<QuestPart> partsListForReading = quest.PartsListForReading;
			for (int j = 0; j < partsListForReading.Count; j++)
			{
				if (partsListForReading[j] is QuestPart_InitiateTradeRequest questPart_InitiateTradeRequest && questPart_InitiateTradeRequest.settlement == settlement)
				{
					return true;
				}
			}
		}
		return false;
	}
}

/// <summary>
/// В MP ActiveRequest = «есть идущий квест». Запрос, чей квест уже закончился,
/// оставлял expiration > 0: Cleanup его не гасил (ActiveRequest уже false),
/// а следующий квест на том же поселении получал «already has an active
/// trade request» и старые def/count. Перед инициализацией нового запроса
/// гасим просроченный остаток.
/// </summary>
[HarmonyPatch(typeof(QuestPart_InitiateTradeRequest))]
[HarmonyPatch("Notify_QuestSignalReceived")]
public static class TradeRequestInitStaleFix
{
	private static void Prefix(QuestPart_InitiateTradeRequest __instance, Signal signal)
	{
		if (!MpHelper.IsMultiplayer() || __instance == null || __instance.settlement == null || signal.tag != __instance.inSignal)
		{
			return;
		}
		try
		{
			TradeRequestComp component = __instance.settlement.GetComponent<TradeRequestComp>();
			if (component != null && component.expiration > 0 && !TradeRequestActiveAsyncTimeFix.HasOngoingQuestOtherThan(__instance.settlement, __instance.quest))
			{
				component.Disable();
			}
		}
		catch (System.Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("TradeRequestInitStaleFix", ex);
		}
	}
}

[HarmonyPatch(typeof(QuestPart_InitiateTradeRequest))]
[HarmonyPatch("Cleanup")]
public static class TradeRequestCleanupFix
{
	private static void Postfix(QuestPart_InitiateTradeRequest __instance)
	{
		if (!MpHelper.IsMultiplayer() || __instance == null || __instance.keepAfterQuestEnds || __instance.settlement == null)
		{
			return;
		}
		try
		{
			TradeRequestComp component = __instance.settlement.GetComponent<TradeRequestComp>();
			if (component != null && component.expiration > 0 && !TradeRequestActiveAsyncTimeFix.HasOngoingQuestOtherThan(__instance.settlement, __instance.quest))
			{
				component.Disable();
			}
		}
		catch (System.Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("TradeRequestCleanupFix", ex);
		}
	}
}
