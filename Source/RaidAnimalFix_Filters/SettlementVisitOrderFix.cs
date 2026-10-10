using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RaidAnimalFix;

[HarmonyPatch("GetFloatMenuOptions", new Type[] { typeof(Caravan) })]
[HarmonyPatch(typeof(Settlement))]
public static class SettlementVisitOrderFix
{
	private enum Kind
	{
		Other,
		Visit,
		Enter
	}

	private static string StayHint()
	{
		return "RAF_VisitStay".Translate();
	}

	private static string EnterPrefix()
	{
		return "RAF_EnterMapPrefix".Translate();
	}

	private static IEnumerable<FloatMenuOption> Postfix(IEnumerable<FloatMenuOption> __result, Caravan caravan, Settlement __instance)
	{
		List<FloatMenuOption> all = new List<FloatMenuOption>();
		foreach (FloatMenuOption item in __result)
		{
			all.Add(item);
		}
		bool reorder = false;
		string visitLabel = null;
		string enterLabel = null;
		TradeRequestComp request = null;
		try
		{
			Faction ofPlayer = Faction.OfPlayer;
			if (MpHelper.IsMultiplayer() && __instance != null && __instance.Faction != null && __instance.Faction != ofPlayer && MpContext.IsRealPlayerFaction(__instance.Faction))
			{
				reorder = true;
				visitLabel = "VisitSettlement".Translate(__instance.Label);
				enterLabel = "EnterMap".Translate(__instance.Label);
				request = TradeRequestHelper.ActiveRequest(__instance);
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("SettlementVisitOrderFix", ex);
			reorder = false;
		}
		if (!reorder)
		{
			for (int l = 0; l < all.Count; l++)
			{
				yield return all[l];
			}
			yield break;
		}
		string stayHint = StayHint();
		string enterPrefix = EnterPrefix();
		List<FloatMenuOption> visit = new List<FloatMenuOption>();
		List<FloatMenuOption> enter = new List<FloatMenuOption>();
		List<FloatMenuOption> rest = new List<FloatMenuOption>();
		for (int m = 0; m < all.Count; m++)
		{
			FloatMenuOption floatMenuOption = all[m];
			switch (Classify(floatMenuOption, visitLabel, enterLabel, stayHint, enterPrefix))
			{
			case Kind.Visit:
				visit.Add(floatMenuOption);
				break;
			case Kind.Enter:
				enter.Add(floatMenuOption);
				break;
			default:
				rest.Add(floatMenuOption);
				break;
			}
		}
		for (int n = 0; n < visit.Count; n++)
		{
			try
			{
				string text = visit[n].Label ?? "";
				if (text.IndexOf(stayHint, StringComparison.Ordinal) < 0)
				{
					text += stayHint;
					if (request != null)
					{
						text += "RAF_RequestSuffix".Translate(TradeRequestHelper.RequestLabel(request));
					}
					visit[n].Label = text;
				}
			}
			catch
			{
			}
		}
		for (int num = 0; num < enter.Count; num++)
		{
			try
			{
				string text2 = enter[num].Label ?? "";
				if (!text2.StartsWith(enterPrefix, StringComparison.Ordinal))
				{
					string text3 = (text2.StartsWith(enterLabel, StringComparison.Ordinal) ? text2.Substring(enterLabel.Length) : "");
					enter[num].Label = enterPrefix + __instance.Label + "RAF_EnterMapSuffix".Translate() + text3;
				}
			}
			catch
			{
			}
		}
		for (int k = 0; k < visit.Count; k++)
		{
			yield return visit[k];
		}
		for (int j = 0; j < rest.Count; j++)
		{
			yield return rest[j];
		}
		for (int i = 0; i < enter.Count; i++)
		{
			yield return enter[i];
		}
	}

	private static Type ArrivalActionType(FloatMenuOption o)
	{
		try
		{
			if (o == null || o.action == null || o.action.Method == null)
			{
				return null;
			}
			Type declaringType = o.action.Method.DeclaringType;
			while (declaringType != null)
			{
				if (declaringType.IsGenericType && declaringType.DeclaringType == typeof(CaravanArrivalActionUtility))
				{
					Type[] genericArguments = declaringType.GetGenericArguments();
					if (genericArguments.Length == 1)
					{
						return genericArguments[0];
					}
				}
				declaringType = declaringType.DeclaringType;
			}
		}
		catch
		{
		}
		return null;
	}

	private static Kind Classify(FloatMenuOption o, string visitLabel, string enterLabel, string stayHint, string enterPrefix)
	{
		string text = o.Label ?? "";
		if (text.StartsWith(enterPrefix, StringComparison.Ordinal))
		{
			return Kind.Enter;
		}
		if (text.IndexOf(stayHint, StringComparison.Ordinal) >= 0)
		{
			return Kind.Visit;
		}
		Type type = ArrivalActionType(o);
		if (type != null)
		{
			if (typeof(CaravanArrivalAction_Enter).IsAssignableFrom(type))
			{
				return Kind.Enter;
			}
			if (typeof(CaravanArrivalAction_VisitSettlement).IsAssignableFrom(type))
			{
				return Kind.Visit;
			}
			return Kind.Other;
		}
		bool flag = text.StartsWith(visitLabel, StringComparison.Ordinal);
		bool flag2 = text.StartsWith(enterLabel, StringComparison.Ordinal);
		if (flag)
		{
			return Kind.Visit;
		}
		if (flag2)
		{
			return Kind.Enter;
		}
		return Kind.Other;
	}
}
