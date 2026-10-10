using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RaidAnimalFix;

internal static class TradeRequestHelper
{
	private static MethodInfo _fulfill;

	private static MethodInfo _playerCanGive;

	private static Texture2D _icon;

	public static Settlement OtherPlayersSettlementAt(int tile, Faction me)
	{
		if (Find.WorldObjects == null)
		{
			return null;
		}
		List<Settlement> settlementBases = Find.WorldObjects.SettlementBases;
		for (int i = 0; i < settlementBases.Count; i++)
		{
			Settlement settlement = settlementBases[i];
			if (settlement != null && (int)settlement.Tile == tile && settlement.Faction != null && settlement.Faction != me && MpContext.IsRealPlayerFaction(settlement.Faction))
			{
				return settlement;
			}
		}
		return null;
	}

	public static TradeRequestComp ActiveRequest(Settlement s)
	{
		if (s == null)
		{
			return null;
		}
		TradeRequestComp component = s.GetComponent<TradeRequestComp>();
		if (component == null || !component.ActiveRequest)
		{
			return null;
		}
		return component;
	}

	public static string RequestLabel(TradeRequestComp comp)
	{
		try
		{
			return TradeRequestUtility.RequestedThingLabel(comp.requestThingDef, comp.requestCount);
		}
		catch
		{
			return comp.requestCount + "x " + ((comp.requestThingDef != null) ? comp.requestThingDef.label : "?");
		}
	}

	public static bool CanGive(TradeRequestComp comp, Thing t)
	{
		try
		{
			if (_playerCanGive == null)
			{
				_playerCanGive = AccessTools.Method(typeof(TradeRequestComp), "PlayerCanGive");
			}
			if (_playerCanGive != null)
			{
				return (bool)_playerCanGive.Invoke(comp, new object[1] { t });
			}
		}
		catch
		{
		}
		return true;
	}

	public static bool HasGoods(Caravan caravan, TradeRequestComp comp)
	{
		return CaravanInventoryUtility.HasThings(caravan, comp.requestThingDef, comp.requestCount, (Thing t) => CanGive(comp, t));
	}

	public static void Fulfill(TradeRequestComp comp, Caravan caravan)
	{
		if (_fulfill == null)
		{
			_fulfill = AccessTools.Method(typeof(TradeRequestComp), "Fulfill");
		}
		if (_fulfill == null)
		{
			Log.Error("[RaidAnimalFix] TradeRequestComp.Fulfill not found");
			return;
		}
		_fulfill.Invoke(comp, new object[1] { caravan });
	}

	public static Texture2D Icon()
	{
		if (_icon == null)
		{
			try
			{
				_icon = ContentFinder<Texture2D>.Get("UI/Commands/FulfillTradeRequest", reportFailure: false);
			}
			catch
			{
			}
			if (_icon == null)
			{
				_icon = FormCaravanComp.FormCaravanCommand;
			}
		}
		return _icon;
	}
}
