using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RaidAnimalFix;

/// <summary>
/// Окно сделки (локальное). Читает синхронное состояние предложения, но меняет
/// его только через RafPlayerTradeSync.*. Пока команда идёт по сети, показываем
/// «ожидаемое» значение (pending), чтобы быстрые клики складывались.
/// </summary>
public class Dialog_RafPlayerTrade : Window
{
	private const float RowH = 28f;

	private const float BtnW = 40f;

	private readonly int offerId;

	private Vector2 scrollMine;

	private Vector2 scrollTheirs;

	private List<Thing> cacheMine = new List<Thing>();

	private List<Thing> cacheTheirs = new List<Thing>();

	private int cacheSilver;

	private float cacheTime = -999f;

	private int cacheRevision = -1;

	/// <summary>thingId (−1 = серебро) → ожидаемое количество и время клика.</summary>
	private readonly Dictionary<int, int> pending = new Dictionary<int, int>();

	private readonly Dictionary<int, float> pendingTime = new Dictionary<int, float>();

	public override Vector2 InitialSize => new Vector2(980f, 680f);

	public Dialog_RafPlayerTrade(int offerId)
	{
		this.offerId = offerId;
		doCloseX = true;
		forcePause = false;
		absorbInputAroundWindow = false;
		closeOnClickedOutside = false;
		preventCameraMotion = false;
		draggable = true;
	}

	public override void DoWindowContents(Rect inRect)
	{
		RafTradeOffer offer = RafTradeData.Get()?.Find(offerId);
		Faction me = RafTrade.LocalFaction();
		RafTradeSide mine = offer?.SideOf(me);
		Text.Font = GameFont.Small;
		if (offer == null || mine == null || offer.state != RafTradeState.Open)
		{
			string key = (offer == null || mine == null) ? "RAF_PlayerTrade_OfferGone" : ((offer.state == RafTradeState.Done) ? "RAF_PlayerTrade_State_Done" : "RAF_PlayerTrade_State_Cancelled");
			Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 60f), key.Translate());
			if (Widgets.ButtonText(new Rect(inRect.center.x - 80f, inRect.yMax - 36f, 160f, 34f), "RAF_PlayerTrade_Close".Translate()))
			{
				Close();
			}
			return;
		}
		RafTradeSide theirs = offer.OtherSide(mine);
		Faction other = RafTrade.FactionById(theirs.factionId);
		RefreshCache(offer, mine, me, theirs, other);
		PrunePending(mine);

		Text.Font = GameFont.Medium;
		Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f), "RAF_PlayerTrade_TradeWith".Translate(other?.Name ?? "?"));
		Text.Font = GameFont.Small;
		int left = offer.createdTick + RafTradeData.ExpireTicks - RafTrade.Now();
		string sub = (RafTrade.UsesDropPods(offer) ? "RAF_PlayerTrade_Delivery_Pods" : "RAF_PlayerTrade_Delivery_Direct").Translate() + "   " + "RAF_PlayerTrade_ExpiresIn".Translate(Math.Max(0, left).ToStringTicksToPeriod());
		Widgets.Label(new Rect(inRect.x, inRect.y + 36f, inRect.width, 24f), sub);

		float top = inRect.y + 66f;
		float bottomH = 72f;
		float colW = (inRect.width - 16f) / 2f;
		Rect leftCol = new Rect(inRect.x, top, colW, inRect.height - (top - inRect.y) - bottomH);
		Rect rightCol = new Rect(inRect.x + colW + 16f, top, colW, leftCol.height);
		DrawMine(leftCol, offer, mine, me);
		DrawTheirs(rightCol, theirs, other);

		Rect bottom = new Rect(inRect.x, inRect.yMax - bottomH + 6f, inRect.width, bottomH - 6f);
		Widgets.DrawLineHorizontal(bottom.x, bottom.y - 3f, bottom.width);
		string meState = (mine.accepted ? "RAF_PlayerTrade_Accepted" : "RAF_PlayerTrade_NotAccepted").Translate();
		string themState = (theirs.accepted ? "RAF_PlayerTrade_Accepted" : "RAF_PlayerTrade_NotAccepted").Translate();
		Widgets.Label(new Rect(bottom.x, bottom.y, bottom.width * 0.5f, 24f), "RAF_PlayerTrade_You".Translate(meState));
		Widgets.Label(new Rect(bottom.x, bottom.y + 24f, bottom.width * 0.5f, 24f), "RAF_PlayerTrade_Them".Translate(other?.Name ?? "?", themState));
		float bx = bottom.xMax - 3f * 170f;
		Rect acceptRect = new Rect(bx, bottom.y + 8f, 160f, 40f);
		bool canAccept = !mine.accepted && pending.Count == 0;
		if (Widgets.ButtonText(acceptRect, "RAF_PlayerTrade_Accept".Translate(), drawBackground: true, doMouseoverSound: true, canAccept) && canAccept)
		{
			RafPlayerTradeSync.Accept(offerId, me, offer.revision);
		}
		if (Widgets.ButtonText(new Rect(bx + 170f, bottom.y + 8f, 160f, 40f), "RAF_PlayerTrade_CancelTrade".Translate()))
		{
			RafPlayerTradeSync.CancelOffer(offerId, me);
		}
		if (Widgets.ButtonText(new Rect(bx + 340f, bottom.y + 8f, 160f, 40f), "RAF_PlayerTrade_Close".Translate()))
		{
			Close();
		}
	}

	private void RefreshCache(RafTradeOffer offer, RafTradeSide mine, Faction me, RafTradeSide theirs, Faction other)
	{
		if (Time.realtimeSinceStartup - cacheTime < 1.5f && cacheRevision == offer.revision)
		{
			return;
		}
		cacheTime = Time.realtimeSinceStartup;
		cacheRevision = offer.revision;
		cacheMine = RafTrade.AvailableItems(mine, me, false);
		cacheMine.Sort((Thing x, Thing y) =>
		{
			int c = string.Compare(x.def.label, y.def.label, StringComparison.OrdinalIgnoreCase);
			return (c != 0) ? c : x.thingIDNumber.CompareTo(y.thingIDNumber);
		});
		cacheSilver = RafTrade.SilverCount(mine, me);
		cacheTheirs = (other != null) ? RafTrade.AvailableItems(theirs, other, false) : new List<Thing>();
	}

	private void PrunePending(RafTradeSide mine)
	{
		if (pending.Count == 0)
		{
			return;
		}
		List<int> drop = null;
		foreach (KeyValuePair<int, int> kv in pending)
		{
			int actual = (kv.Key == -1) ? mine.silver : mine.CountOf(kv.Key);
			if (actual == kv.Value || Time.realtimeSinceStartup - pendingTime[kv.Key] > 3f)
			{
				(drop ?? (drop = new List<int>())).Add(kv.Key);
			}
		}
		if (drop != null)
		{
			foreach (int k in drop)
			{
				pending.Remove(k);
				pendingTime.Remove(k);
			}
		}
	}

	private int Shown(RafTradeSide mine, int key)
	{
		if (pending.TryGetValue(key, out var v))
		{
			return v;
		}
		return (key == -1) ? mine.silver : mine.CountOf(key);
	}

	private void Send(Faction me, int key, int value)
	{
		pending[key] = value;
		pendingTime[key] = Time.realtimeSinceStartup;
		if (key == -1)
		{
			RafPlayerTradeSync.SetSilver(offerId, me, value);
		}
		else
		{
			RafPlayerTradeSync.SetOfferLine(offerId, me, key, value);
		}
	}

	private void DrawMine(Rect rect, RafTradeOffer offer, RafTradeSide mine, Faction me)
	{
		Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f), "RAF_PlayerTrade_YourOffer".Translate(RafTrade.AnchorLabel(mine)));
		float y = rect.y + 26f;
		// Серебро.
		int silver = Shown(mine, -1);
		Rect srow = new Rect(rect.x, y, rect.width, RowH);
		Widgets.DrawHighlightIfMouseover(srow);
		Widgets.ThingIcon(new Rect(srow.x, srow.y, RowH, RowH), ThingDefOf.Silver);
		Widgets.Label(new Rect(srow.x + RowH + 4f, srow.y + 3f, srow.width - RowH - 4f - 5f * BtnW, RowH), "RAF_PlayerTrade_Silver".Translate() + ": " + silver + " (" + "RAF_PlayerTrade_Available".Translate(cacheSilver.ToString()) + ")");
		DrawButtons(srow, me, -1, silver, cacheSilver, 100);
		y += RowH + 4f;
		float value = 0f;
		for (int i = 0; i < cacheMine.Count; i++)
		{
			Thing t = cacheMine[i];
			int n = Shown(mine, t.thingIDNumber);
			if (n > 0 && !t.Destroyed)
			{
				value += t.MarketValue * (float)n;
			}
		}
		value += (float)silver;
		Widgets.Label(new Rect(rect.x, y, rect.width, 24f), "RAF_PlayerTrade_Value".Translate(value.ToStringMoney()));
		y += 26f;
		Rect outRect = new Rect(rect.x, y, rect.width, rect.yMax - y);
		if (cacheMine.Count == 0)
		{
			GUI.color = Color.gray;
			Widgets.Label(outRect, "RAF_PlayerTrade_NoItems".Translate());
			GUI.color = Color.white;
			return;
		}
		Rect view = new Rect(0f, 0f, outRect.width - 16f, cacheMine.Count * RowH);
		Widgets.BeginScrollView(outRect, ref scrollMine, view);
		for (int j = 0; j < cacheMine.Count; j++)
		{
			Thing t2 = cacheMine[j];
			if (t2.Destroyed)
			{
				continue;
			}
			Rect row = new Rect(0f, j * RowH, view.width, RowH);
			if (j % 2 == 1)
			{
				Widgets.DrawLightHighlight(row);
			}
			Widgets.DrawHighlightIfMouseover(row);
			int n2 = Shown(mine, t2.thingIDNumber);
			Widgets.ThingIcon(new Rect(row.x, row.y, RowH, RowH), t2);
			Rect label = new Rect(row.x + RowH + 4f, row.y + 3f, row.width - RowH - 4f - 5f * BtnW, RowH);
			if (n2 > 0)
			{
				GUI.color = new Color(0.6f, 1f, 0.6f);
			}
			Widgets.Label(label, n2 + " / " + t2.stackCount + "  " + t2.LabelCapNoCount);
			GUI.color = Color.white;
			TooltipHandler.TipRegion(label, t2.LabelCap);
			DrawButtons(row, me, t2.thingIDNumber, n2, t2.stackCount, 10);
		}
		Widgets.EndScrollView();
	}

	/// <summary>Кнопки 0 / −step / −1 / +1 / +step / Все.</summary>
	private void DrawButtons(Rect row, Faction me, int key, int cur, int max, int step)
	{
		float x = row.xMax - 5f * BtnW;
		if (Widgets.ButtonText(new Rect(x, row.y + 2f, BtnW - 2f, RowH - 4f), "0") && cur != 0)
		{
			Send(me, key, 0);
		}
		x += BtnW;
		if (Widgets.ButtonText(new Rect(x, row.y + 2f, BtnW - 2f, RowH - 4f), "-" + step) && cur > 0)
		{
			Send(me, key, Math.Max(0, cur - step));
		}
		x += BtnW;
		if (Widgets.ButtonText(new Rect(x, row.y + 2f, BtnW - 2f, RowH - 4f), "+1") && cur < max)
		{
			Send(me, key, cur + 1);
		}
		x += BtnW;
		if (Widgets.ButtonText(new Rect(x, row.y + 2f, BtnW - 2f, RowH - 4f), "+" + step) && cur < max)
		{
			Send(me, key, Math.Min(max, cur + step));
		}
		x += BtnW;
		if (Widgets.ButtonText(new Rect(x, row.y + 2f, BtnW - 2f, RowH - 4f), "RAF_PlayerTrade_All".Translate()) && cur < max)
		{
			Send(me, key, max);
		}
	}

	private void DrawTheirs(Rect rect, RafTradeSide theirs, Faction other)
	{
		Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f), "RAF_PlayerTrade_TheirOffer".Translate(other?.Name ?? "?", RafTrade.AnchorLabel(theirs)));
		float y = rect.y + 26f;
		Rect srow = new Rect(rect.x, y, rect.width, RowH);
		Widgets.ThingIcon(new Rect(srow.x, srow.y, RowH, RowH), ThingDefOf.Silver);
		Widgets.Label(new Rect(srow.x + RowH + 4f, srow.y + 3f, srow.width - RowH - 4f, RowH), "RAF_PlayerTrade_Silver".Translate() + ": " + theirs.silver);
		y += RowH + 4f;
		float value = theirs.silver;
		List<KeyValuePair<Thing, int>> rows = new List<KeyValuePair<Thing, int>>();
		for (int i = 0; i < theirs.lines.Count; i++)
		{
			RafTradeLine line = theirs.lines[i];
			Thing t = cacheTheirs.Find((Thing x) => x.thingIDNumber == line.thingId);
			rows.Add(new KeyValuePair<Thing, int>(t, line.count));
			if (t != null && !t.Destroyed)
			{
				value += t.MarketValue * (float)line.count;
			}
		}
		Widgets.Label(new Rect(rect.x, y, rect.width, 24f), "RAF_PlayerTrade_Value".Translate(value.ToStringMoney()));
		y += 26f;
		Rect outRect = new Rect(rect.x, y, rect.width, rect.yMax - y);
		if (rows.Count == 0)
		{
			GUI.color = Color.gray;
			Widgets.Label(outRect, "RAF_PlayerTrade_NothingOffered".Translate());
			GUI.color = Color.white;
			return;
		}
		Rect view = new Rect(0f, 0f, outRect.width - 16f, rows.Count * RowH);
		Widgets.BeginScrollView(outRect, ref scrollTheirs, view);
		for (int j = 0; j < rows.Count; j++)
		{
			Rect row = new Rect(0f, j * RowH, view.width, RowH);
			if (j % 2 == 1)
			{
				Widgets.DrawLightHighlight(row);
			}
			Thing t2 = rows[j].Key;
			if (t2 != null && !t2.Destroyed)
			{
				Widgets.ThingIcon(new Rect(row.x, row.y, RowH, RowH), t2);
				Rect label = new Rect(row.x + RowH + 4f, row.y + 3f, row.width - RowH - 4f, RowH);
				Widgets.Label(label, rows[j].Value + " x " + t2.LabelCapNoCount);
				TooltipHandler.TipRegion(label, t2.LabelCap);
			}
			else
			{
				GUI.color = Color.gray;
				Widgets.Label(new Rect(row.x + RowH + 4f, row.y + 3f, row.width - RowH - 4f, RowH), rows[j].Value + " x " + "RAF_PlayerTrade_Unknown".Translate());
				GUI.color = Color.white;
			}
		}
		Widgets.EndScrollView();
	}
}

/// <summary>
/// Окно «Торговля с игроками»: открытые предложения своей фракции и кнопки
/// удалённой торговли (доставка капсулами) с каждой другой игровой фракцией.
/// </summary>
public class Dialog_RafTradeHub : Window
{
	private Vector2 scroll;

	public override Vector2 InitialSize => new Vector2(620f, 560f);

	public Dialog_RafTradeHub()
	{
		doCloseX = true;
		forcePause = false;
		absorbInputAroundWindow = false;
		closeOnClickedOutside = true;
		preventCameraMotion = false;
		draggable = true;
	}

	public override void DoWindowContents(Rect inRect)
	{
		Faction me = RafTrade.LocalFaction();
		RafTradeData data = RafTradeData.Get();
		Text.Font = GameFont.Medium;
		Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f), "RAF_PlayerTrade_HubLabel".Translate());
		Text.Font = GameFont.Small;
		List<RafTradeOffer> mine = new List<RafTradeOffer>();
		if (data != null && me != null)
		{
			for (int i = 0; i < data.offers.Count; i++)
			{
				if (data.offers[i].state == RafTradeState.Open && data.offers[i].Involves(me))
				{
					mine.Add(data.offers[i]);
				}
			}
		}
		List<Faction> others = (me != null) ? RafTrade.OtherPlayerFactions(me) : new List<Faction>();
		Rect outRect = new Rect(inRect.x, inRect.y + 40f, inRect.width, inRect.height - 40f);
		Rect view = new Rect(0f, 0f, outRect.width - 16f, 30f + Math.Max(1, mine.Count) * 32f + 40f + Math.Max(1, others.Count) * 32f + 20f);
		Widgets.BeginScrollView(outRect, ref scroll, view);
		float y = 0f;
		Widgets.Label(new Rect(0f, y, view.width, 26f), "RAF_PlayerTrade_ActiveOffers".Translate());
		y += 28f;
		if (mine.Count == 0)
		{
			GUI.color = Color.gray;
			Widgets.Label(new Rect(10f, y, view.width, 26f), "RAF_PlayerTrade_NoOffers".Translate());
			GUI.color = Color.white;
			y += 32f;
		}
		foreach (RafTradeOffer o in mine)
		{
			RafTradeSide my = o.SideOf(me);
			RafTradeSide their = o.OtherSide(my);
			Faction f = RafTrade.FactionById(their.factionId);
			string label = (f?.Name ?? "?") + " — " + RafTrade.AnchorLabel(my) + " ⇄ " + RafTrade.AnchorLabel(their);
			if (their.accepted)
			{
				label = label + " (" + "RAF_PlayerTrade_Accepted".Translate() + ")";
			}
			Widgets.Label(new Rect(10f, y + 3f, view.width - 130f, 26f), label);
			if (Widgets.ButtonText(new Rect(view.width - 110f, y, 110f, 28f), "RAF_PlayerTrade_Open".Translate()))
			{
				Find.WindowStack.Add(new Dialog_RafPlayerTrade(o.id));
			}
			y += 32f;
		}
		y += 8f;
		Widgets.Label(new Rect(0f, y, view.width, 26f), "RAF_PlayerTrade_RemoteHeader".Translate());
		y += 28f;
		bool haveHome = RafTrade.HomeMapOf(me) != null;
		if (others.Count == 0)
		{
			GUI.color = Color.gray;
			Widgets.Label(new Rect(10f, y, view.width, 26f), "RAF_PlayerTrade_NoPlayers".Translate());
			GUI.color = Color.white;
		}
		foreach (Faction f2 in others)
		{
			string reason = null;
			if (!haveHome)
			{
				reason = "RAF_PlayerTrade_NoHomeMap".Translate();
			}
			else if (RafTrade.HomeMapOf(f2) == null)
			{
				reason = "RAF_PlayerTrade_TheyNoHomeMap".Translate(f2.Name);
			}
			else if (RafTrade.AtWar(me, f2))
			{
				reason = "RAF_PlayerTrade_AtWar".Translate(f2.Name);
			}
			Widgets.Label(new Rect(10f, y + 3f, view.width - 250f, 26f), f2.Name + ((reason != null) ? (" — " + reason) : ""));
			Rect btn = new Rect(view.width - 230f, y, 230f, 28f);
			if (Widgets.ButtonText(btn, "RAF_PlayerTrade_RemoteButton".Translate(), drawBackground: true, doMouseoverSound: true, reason == null) && reason == null)
			{
				RafPlayerTradeSync.CreateOffer(me, f2, RafTrade.AnchorMap, -1, RafTrade.AnchorMap, -1);
			}
			y += 32f;
		}
		Widgets.EndScrollView();
	}
}

/// <summary>Общие куски гизмо торговли (только UI).</summary>
internal static class RafTradeGizmos
{
	private static Texture2D _icon;

	public static Texture2D Icon
	{
		get
		{
			if (_icon == null)
			{
				_icon = ContentFinder<Texture2D>.Get("UI/Commands/Trade", reportFailure: false) ?? BaseContent.BadTex;
			}
			return _icon;
		}
	}

	public static Command_Action Hub()
	{
		return new Command_Action
		{
			defaultLabel = "RAF_PlayerTrade_HubLabel".Translate(),
			defaultDesc = "RAF_PlayerTrade_HubDesc".Translate(),
			icon = Icon,
			action = delegate
			{
				Find.WindowStack.Add(new Dialog_RafTradeHub());
			}
		};
	}

	public static Command_Action TradeWith(Faction me, Faction other, string where, int myKind, int myId, int otherKind, int otherId)
	{
		Command_Action cmd = new Command_Action
		{
			defaultLabel = "RAF_PlayerTrade_TradeWith".Translate(other.Name),
			defaultDesc = "RAF_PlayerTrade_TradeWithDesc".Translate(other.Name, where),
			icon = Icon,
			action = delegate
			{
				RafPlayerTradeSync.CreateOffer(me, other, myKind, myId, otherKind, otherId);
			}
		};
		if (RafTrade.AtWar(me, other))
		{
			cmd.Disable("RAF_PlayerTrade_AtWar".Translate(other.Name));
		}
		return cmd;
	}
}

/// <summary>
/// Гизмо своего каравана: «Торговать с …» для поселения другого игрока на том же
/// или соседнем тайле и для каравана другого игрока на том же тайле, плюс окно
/// торговли. Только чтение мира; изменения — через синхронную команду.
/// </summary>
[HarmonyPatch(typeof(Caravan), "GetGizmos")]
public static class PlayerTradeCaravanGizmos
{
	private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Caravan __instance)
	{
		foreach (Gizmo g in __result)
		{
			yield return g;
		}
		List<Gizmo> extra = null;
		try
		{
			extra = Build(__instance);
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("PlayerTradeCaravanGizmos", ex);
		}
		if (extra == null)
		{
			yield break;
		}
		foreach (Gizmo g2 in extra)
		{
			yield return g2;
		}
	}

	private static List<Gizmo> Build(Caravan caravan)
	{
		if (!MpHelper.IsMultiplayer() || caravan == null || !caravan.Spawned)
		{
			return null;
		}
		Faction me = RafTrade.LocalFaction();
		if (me == null || caravan.Faction != me)
		{
			return null;
		}
		List<Gizmo> list = new List<Gizmo>();
		List<Settlement> settlements = Find.WorldObjects.Settlements;
		for (int i = 0; i < settlements.Count; i++)
		{
			Settlement s = settlements[i];
			Faction f = s.Faction;
			if (f != null && f != me && RafTrade.IsTradePartner(f) && s.HasMap && s.Map.ParentFaction == f && Find.WorldGrid.IsNeighborOrSame(caravan.Tile, s.Tile))
			{
				list.Add(RafTradeGizmos.TradeWith(me, f, s.LabelCap, RafTrade.AnchorCaravan, caravan.ID, RafTrade.AnchorMap, s.Map.uniqueID));
			}
		}
		List<Caravan> caravans = Find.WorldObjects.Caravans;
		for (int j = 0; j < caravans.Count; j++)
		{
			Caravan c = caravans[j];
			Faction f2 = c.Faction;
			if (c != caravan && f2 != null && f2 != me && RafTrade.IsTradePartner(f2) && c.Tile == caravan.Tile)
			{
				list.Add(RafTradeGizmos.TradeWith(me, f2, c.LabelCap, RafTrade.AnchorCaravan, caravan.ID, RafTrade.AnchorCaravan, c.ID));
			}
		}
		list.Add(RafTradeGizmos.Hub());
		return list;
	}
}

/// <summary>Гизмо своего поселения на карте мира: окно торговли с игроками (в т.ч. удалённой).</summary>
[HarmonyPatch(typeof(Settlement), "GetGizmos")]
public static class PlayerTradeSettlementGizmo
{
	private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Settlement __instance)
	{
		foreach (Gizmo g in __result)
		{
			yield return g;
		}
		bool show = false;
		try
		{
			show = MpHelper.IsMultiplayer() && __instance != null && __instance.Faction != null && __instance.Faction == RafTrade.LocalFaction();
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("PlayerTradeSettlementGizmo", ex);
		}
		if (show)
		{
			yield return RafTradeGizmos.Hub();
		}
	}
}
