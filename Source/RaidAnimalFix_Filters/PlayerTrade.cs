using System;
using System.Collections.Generic;
using System.Reflection;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RaidAnimalFix;

// ───────────────────────── Торговля между игроками (multifaction) ─────────────────────────

/// <summary>Состояние предложения обмена. Хранится числом в сейве.</summary>
public enum RafTradeState
{
	Open,
	Done,
	Cancelled
}

/// <summary>
/// Одна строка предложения: конкретная вещь (по thingIDNumber) и сколько из её
/// стака отдаётся. Ссылаемся по id, а не по Thing: вещь может исчезнуть, а
/// сейв/команды должны оставаться валидными — при исполнении всё ищется заново.
/// </summary>
public class RafTradeLine : IExposable
{
	public int thingId;

	public int count;

	public void ExposeData()
	{
		Scribe_Values.Look(ref thingId, "thingId", 0);
		Scribe_Values.Look(ref count, "count", 0);
	}
}

/// <summary>
/// Сторона сделки: фракция (loadID), «якорь» — откуда берутся и куда приходят
/// товары (карта колонии по uniqueID или караван по WorldObject.ID), строки
/// предложения, серебро и флаг согласия.
/// </summary>
public class RafTradeSide : IExposable
{
	public int factionId = -1;

	public int anchorKind;

	public int anchorId = -1;

	public List<RafTradeLine> lines = new List<RafTradeLine>();

	public int silver;

	public bool accepted;

	public void ExposeData()
	{
		Scribe_Values.Look(ref factionId, "factionId", -1);
		Scribe_Values.Look(ref anchorKind, "anchorKind", 0);
		Scribe_Values.Look(ref anchorId, "anchorId", -1);
		Scribe_Collections.Look(ref lines, "lines", LookMode.Deep);
		Scribe_Values.Look(ref silver, "silver", 0);
		Scribe_Values.Look(ref accepted, "accepted", defaultValue: false);
		if (Scribe.mode == LoadSaveMode.PostLoadInit && lines == null)
		{
			lines = new List<RafTradeLine>();
		}
	}

	public int CountOf(int thingId)
	{
		for (int i = 0; i < lines.Count; i++)
		{
			if (lines[i].thingId == thingId)
			{
				return lines[i].count;
			}
		}
		return 0;
	}
}

/// <summary>
/// Предложение обмена между двумя игровыми фракциями. revision растёт при каждом
/// изменении: согласие принимается, только если игрок видел именно эту версию
/// (иначе встречное изменение, пришедшее раньше в очереди команд, «подсунуло» бы
/// ему другую сделку).
/// </summary>
public class RafTradeOffer : IExposable
{
	public int id;

	public RafTradeSide a = new RafTradeSide();

	public RafTradeSide b = new RafTradeSide();

	public int createdTick;

	public int closedTick = -1;

	public RafTradeState state;

	public int revision;

	public void ExposeData()
	{
		Scribe_Values.Look(ref id, "id", 0);
		Scribe_Deep.Look(ref a, "a");
		Scribe_Deep.Look(ref b, "b");
		Scribe_Values.Look(ref createdTick, "createdTick", 0);
		Scribe_Values.Look(ref closedTick, "closedTick", -1);
		Scribe_Values.Look(ref state, "state", RafTradeState.Open);
		Scribe_Values.Look(ref revision, "revision", 0);
		if (Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			if (a == null)
			{
				a = new RafTradeSide();
			}
			if (b == null)
			{
				b = new RafTradeSide();
			}
		}
	}

	public RafTradeSide SideOf(Faction f)
	{
		if (f == null)
		{
			return null;
		}
		if (a.factionId == f.loadID)
		{
			return a;
		}
		if (b.factionId == f.loadID)
		{
			return b;
		}
		return null;
	}

	public RafTradeSide OtherSide(RafTradeSide s)
	{
		return (s == a) ? b : a;
	}

	public bool Involves(Faction f)
	{
		return SideOf(f) != null;
	}

	public void Touch()
	{
		a.accepted = false;
		b.accepted = false;
		revision++;
	}
}

/// <summary>
/// Синхронное хранилище предложений обмена. Всё состояние торговли живёт здесь
/// (а не в статике/окнах), чтобы одинаково сохраняться, грузиться и тикать у
/// всех клиентов. Тик мира снимает просроченные предложения и те, чьи якоря
/// пропали или разошлись.
/// </summary>
public class RafTradeData : WorldComponent
{
	/// <summary>2 игровых дня — срок жизни открытого предложения.</summary>
	public const int ExpireTicks = 120000;

	/// <summary>Сколько хранить закрытые предложения (чтобы открытое окно успело показать итог).</summary>
	private const int KeepClosedTicks = 60000;

	public int nextId = 1;

	public List<RafTradeOffer> offers = new List<RafTradeOffer>();

	public RafTradeData(World world)
		: base(world)
	{
	}

	public static RafTradeData Get()
	{
		World world = Verse.Find.World;
		if (world == null)
		{
			return null;
		}
		try
		{
			return world.GetComponent<RafTradeData>();
		}
		catch
		{
			return null;
		}
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Values.Look(ref nextId, "rafTradeNextId", 1);
		Scribe_Collections.Look(ref offers, "rafTradeOffers", LookMode.Deep);
		if (Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			if (offers == null)
			{
				offers = new List<RafTradeOffer>();
			}
			offers.RemoveAll((RafTradeOffer o) => o == null);
		}
	}

	public RafTradeOffer Find(int id)
	{
		for (int i = 0; i < offers.Count; i++)
		{
			if (offers[i].id == id)
			{
				return offers[i];
			}
		}
		return null;
	}

	public override void WorldComponentTick()
	{
		base.WorldComponentTick();
		int now = RafTrade.Now();
		if (now % 250 != 0 || offers.Count == 0)
		{
			return;
		}
		try
		{
			for (int i = offers.Count - 1; i >= 0; i--)
			{
				RafTradeOffer offer = offers[i];
				if (offer.state != RafTradeState.Open)
				{
					if (now - offer.closedTick > KeepClosedTicks)
					{
						offers.RemoveAt(i);
					}
					continue;
				}
				if (!RafTrade.AnchorsValid(offer))
				{
					RafTrade.Close(offer, RafTradeState.Cancelled, "RAF_PlayerTrade_Msg_AnchorLost");
				}
				else if (now - offer.createdTick > ExpireTicks)
				{
					RafTrade.Close(offer, RafTradeState.Cancelled, "RAF_PlayerTrade_Msg_Expired");
				}
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("RafTradeData.Tick", ex);
		}
	}
}

/// <summary>
/// Логика торговли: поиск якорей, доступных вещей, проверка и исполнение сделки.
/// Все функции, вызываемые из симуляции, опираются только на синхронное
/// состояние (списки карт/объектов мира/вещей в детерминированном порядке).
/// Локальная фракция игрока читается только для решения «показывать ли сообщение».
/// </summary>
public static class RafTrade
{
	public const int AnchorMap = 1;

	public const int AnchorCaravan = 2;

	public static int Now()
	{
		if (MpMapTime.TryGetWorldTicks(out var ticks))
		{
			return ticks;
		}
		return Find.TickManager.TicksGame;
	}

	/// <summary>Фракция этого клиента (только для UI и решения, показывать ли сообщение).</summary>
	public static Faction LocalFaction()
	{
		if (MpHelper.IsMultiplayer())
		{
			return MpHelper.GetCurrentPlayerFaction();
		}
		return Faction.OfPlayer;
	}

	public static bool IsLocal(Faction f)
	{
		return f != null && LocalFaction() == f;
	}

	/// <summary>Команду отправил этот клиент — только он открывает окно сделки.</summary>
	public static bool IssuedBySelf()
	{
		try
		{
			if (!MP.IsInMultiplayer)
			{
				return true;
			}
			return MP.IsExecutingSyncCommandIssuedBySelf;
		}
		catch
		{
			return true;
		}
	}

	public static Faction FactionById(int id)
	{
		List<Faction> all = Find.FactionManager.AllFactionsListForReading;
		for (int i = 0; i < all.Count; i++)
		{
			if (all[i].loadID == id)
			{
				return all[i];
			}
		}
		return null;
	}

	public static bool IsTradePartner(Faction f)
	{
		if (f == null || f.defeated)
		{
			return false;
		}
		if (MpHelper.IsMultiplayer())
		{
			return MpContext.IsRealPlayerFaction(f);
		}
		return f.IsPlayer;
	}

	/// <summary>Реальные игровые фракции, кроме me — в порядке FactionManager (детерминирован).</summary>
	public static List<Faction> OtherPlayerFactions(Faction me)
	{
		List<Faction> result = new List<Faction>();
		List<Faction> all = Find.FactionManager.AllFactionsListForReading;
		for (int i = 0; i < all.Count; i++)
		{
			Faction f = all[i];
			if (f != me && IsTradePartner(f))
			{
				result.Add(f);
			}
		}
		return result;
	}

	// ── Дипломатия (необязательная зависимость) ──

	private static bool _warResolved;

	private static MethodInfo _atWar;

	/// <summary>
	/// Если в сборке есть RaidAnimalFix.RelationsApi.AtWar(Faction, Faction) —
	/// торговля во время войны запрещена. Ищем отражением, чтобы фича работала и
	/// без дипломатии. Кэш — лишь результат поиска метода, одинаковый у всех.
	/// </summary>
	public static bool AtWar(Faction x, Faction y)
	{
		if (x == null || y == null)
		{
			return false;
		}
		if (!_warResolved)
		{
			_warResolved = true;
			try
			{
				Type t = typeof(RafTrade).Assembly.GetType("RaidAnimalFix.RelationsApi", throwOnError: false) ?? typeof(MpHelper).Assembly.GetType("RaidAnimalFix.RelationsApi", throwOnError: false);
				_atWar = t?.GetMethod("AtWar", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[2]
				{
					typeof(Faction),
					typeof(Faction)
				}, null);
				if (_atWar != null && _atWar.ReturnType != typeof(bool))
				{
					_atWar = null;
				}
			}
			catch
			{
				_atWar = null;
			}
		}
		if (_atWar == null)
		{
			return false;
		}
		try
		{
			return (bool)_atWar.Invoke(null, new object[2] { x, y });
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("RafTrade.AtWar", ex);
			return false;
		}
	}

	// ── Якоря ──

	public static Map ResolveMap(int uniqueId)
	{
		List<Map> maps = Find.Maps;
		for (int i = 0; i < maps.Count; i++)
		{
			if (maps[i].uniqueID == uniqueId)
			{
				return maps[i];
			}
		}
		return null;
	}

	public static Caravan ResolveCaravan(int id)
	{
		List<Caravan> caravans = Find.WorldObjects.Caravans;
		for (int i = 0; i < caravans.Count; i++)
		{
			if (caravans[i].ID == id)
			{
				return caravans[i];
			}
		}
		return null;
	}

	/// <summary>
	/// Домашняя карта фракции для удалённой торговли: первая (порядок Find.Maps)
	/// карта поселения этой фракции, где есть её колонисты; иначе — любая её.
	/// </summary>
	public static Map HomeMapOf(Faction f)
	{
		if (f == null)
		{
			return null;
		}
		Map fallback = null;
		List<Map> maps = Find.Maps;
		for (int i = 0; i < maps.Count; i++)
		{
			Map map = maps[i];
			if (map.ParentFaction != f || !(map.Parent is Settlement))
			{
				continue;
			}
			if (NotificationFilter.MapHasColonistsOf(map, f))
			{
				return map;
			}
			if (fallback == null)
			{
				fallback = map;
			}
		}
		return fallback;
	}

	public static bool AnchorValid(RafTradeSide s, Faction f)
	{
		if (s == null || f == null)
		{
			return false;
		}
		if (s.anchorKind == AnchorMap)
		{
			Map map = ResolveMap(s.anchorId);
			return map != null && map.ParentFaction == f;
		}
		if (s.anchorKind == AnchorCaravan)
		{
			Caravan caravan = ResolveCaravan(s.anchorId);
			return caravan != null && caravan.Spawned && caravan.Faction == f && caravan.PawnsListForReading.Count > 0;
		}
		return false;
	}

	public static PlanetTile AnchorTile(RafTradeSide s)
	{
		if (s.anchorKind == AnchorMap)
		{
			Map map = ResolveMap(s.anchorId);
			return map?.Tile ?? PlanetTile.Invalid;
		}
		Caravan caravan = ResolveCaravan(s.anchorId);
		return caravan?.Tile ?? PlanetTile.Invalid;
	}

	/// <summary>
	/// Сделка с караваном требует, чтобы стороны были вместе: караван–караван на
	/// одном тайле, караван–колония на тайле колонии или соседнем. Карта–карта
	/// допустимы всегда (доставка капсулами).
	/// </summary>
	public static bool Colocated(RafTradeSide x, RafTradeSide y)
	{
		if (x.anchorKind == AnchorMap && y.anchorKind == AnchorMap)
		{
			return true;
		}
		PlanetTile tx = AnchorTile(x);
		PlanetTile ty = AnchorTile(y);
		if (!tx.Valid || !ty.Valid)
		{
			return false;
		}
		if (x.anchorKind == AnchorCaravan && y.anchorKind == AnchorCaravan)
		{
			return tx == ty;
		}
		return Find.WorldGrid.IsNeighborOrSame(tx, ty);
	}

	/// <summary>Нужны ли капсулы: только для двух разных карт.</summary>
	public static bool UsesDropPods(RafTradeOffer o)
	{
		return o.a.anchorKind == AnchorMap && o.b.anchorKind == AnchorMap && o.a.anchorId != o.b.anchorId;
	}

	public static bool AnchorsValid(RafTradeOffer o)
	{
		Faction fa = FactionById(o.a.factionId);
		Faction fb = FactionById(o.b.factionId);
		return fa != null && fb != null && AnchorValid(o.a, fa) && AnchorValid(o.b, fb) && Colocated(o.a, o.b);
	}

	public static string AnchorLabel(RafTradeSide s)
	{
		if (s.anchorKind == AnchorMap)
		{
			Map map = ResolveMap(s.anchorId);
			return (map?.Parent != null) ? map.Parent.LabelCap.ToString() : "?";
		}
		Caravan caravan = ResolveCaravan(s.anchorId);
		return (caravan != null) ? caravan.LabelCap.ToString() : "?";
	}

	// ── Доступные вещи ──

	/// <summary>
	/// Вещи якоря, которые сторона может отдать, отсортированные по thingIDNumber.
	/// Карта: несущие вещи на складах (зоны/стеллажи), не запрещённые для фракции
	/// (MP хранит запрет по фракциям). Караван: инвентарь пешек каравана.
	/// silver: null — все, true — только серебро, false — всё, кроме серебра.
	/// </summary>
	public static List<Thing> AvailableItems(RafTradeSide s, Faction f, bool? silver)
	{
		List<Thing> result = new List<Thing>();
		if (!AnchorValid(s, f))
		{
			return result;
		}
		if (s.anchorKind == AnchorMap)
		{
			Map map = ResolveMap(s.anchorId);
			List<Thing> things = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);
			for (int i = 0; i < things.Count; i++)
			{
				Thing t = things[i];
				if (t == null || !t.Spawned || t.Destroyed || t is Pawn || t.stackCount <= 0)
				{
					continue;
				}
				if (!MatchesSilver(t, silver) || !t.IsInAnyStorage() || t.IsForbidden(f))
				{
					continue;
				}
				result.Add(t);
			}
		}
		else
		{
			Caravan caravan = ResolveCaravan(s.anchorId);
			List<Thing> items = new List<Thing>(CaravanInventoryUtility.AllInventoryItems(caravan));
			for (int j = 0; j < items.Count; j++)
			{
				Thing t2 = items[j];
				if (t2 != null && !t2.Destroyed && !(t2 is Pawn) && t2.stackCount > 0 && MatchesSilver(t2, silver))
				{
					result.Add(t2);
				}
			}
		}
		result.Sort((Thing x, Thing y) => x.thingIDNumber.CompareTo(y.thingIDNumber));
		return result;
	}

	private static bool MatchesSilver(Thing t, bool? silver)
	{
		if (!silver.HasValue)
		{
			return true;
		}
		return (t.def == ThingDefOf.Silver) == silver.Value;
	}

	public static int SilverCount(RafTradeSide s, Faction f)
	{
		int n = 0;
		List<Thing> list = AvailableItems(s, f, true);
		for (int i = 0; i < list.Count; i++)
		{
			n += list[i].stackCount;
		}
		return n;
	}

	public static Thing FindAvailable(RafTradeSide s, Faction f, int thingId)
	{
		List<Thing> list = AvailableItems(s, f, false);
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i].thingIDNumber == thingId)
			{
				return list[i];
			}
		}
		return null;
	}

	// ── Сообщения ──

	/// <summary>Сообщение обеим сторонам — показывается только у игроков этих двух фракций.</summary>
	public static void Notify(RafTradeOffer o, string key, MessageTypeDef type, string extra = null)
	{
		Faction fa = FactionById(o.a.factionId);
		Faction fb = FactionById(o.b.factionId);
		NotifyOne(fa, fb, key, type, extra);
		NotifyOne(fb, fa, key, type, extra);
	}

	public static void NotifyOne(Faction to, Faction other, string key, MessageTypeDef type, string extra = null)
	{
		try
		{
			if (to == null || !IsLocal(to))
			{
				return;
			}
			string otherName = other?.Name ?? "?";
			string text = (extra == null) ? key.Translate(otherName).ToString() : key.Translate(otherName, extra).ToString();
			Messages.Message(text, type, historical: false);
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("RafTrade.Notify", ex);
		}
	}

	public static void Close(RafTradeOffer o, RafTradeState state, string msgKey)
	{
		o.state = state;
		o.closedTick = Now();
		o.a.accepted = false;
		o.b.accepted = false;
		o.revision++;
		if (msgKey != null)
		{
			Notify(o, msgKey, (state == RafTradeState.Done) ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NeutralEvent);
		}
	}

	// ── Исполнение ──

	/// <summary>
	/// Выполняет на карте с её часами и данными фракции f (как MP при командах
	/// этой фракции): запрет, склады, выбор точки сброса зависят от фракции.
	/// </summary>
	public static void RunAs(Map map, Faction f, Action action)
	{
		bool pushed = false;
		object snapshot = null;
		if (MpHelper.IsMultiplayer() && MpSim.CanRestoreContext && f != null)
		{
			try
			{
				MpSim.PushFaction(map, f);
				pushed = true;
				if (map != null)
				{
					snapshot = MpSim.SetTimeFromMap(map);
				}
			}
			catch (Exception ex)
			{
				CaravanLeaveHelper.LogErrorThrottled("RafTrade.RunAs.push", ex);
			}
		}
		try
		{
			action();
		}
		finally
		{
			try
			{
				if (snapshot != null)
				{
					MpSim.RestoreTime(snapshot);
				}
				if (pushed)
				{
					MpSim.PopFaction(map);
				}
			}
			catch (Exception ex2)
			{
				CaravanLeaveHelper.LogErrorThrottled("RafTrade.RunAs.pop", ex2);
			}
		}
	}

	/// <summary>
	/// Собирает, что именно отдаёт сторона: (вещь, количество) в порядке
	/// thingIDNumber, затем стаки серебра по возрастанию id. null + reason, если
	/// что-то пропало/уменьшилось.
	/// </summary>
	private static List<KeyValuePair<Thing, int>> BuildTake(RafTradeSide s, Faction f, out string reason)
	{
		reason = null;
		List<KeyValuePair<Thing, int>> take = new List<KeyValuePair<Thing, int>>();
		List<Thing> items = AvailableItems(s, f, null);
		List<RafTradeLine> lines = new List<RafTradeLine>(s.lines);
		lines.Sort((RafTradeLine x, RafTradeLine y) => x.thingId.CompareTo(y.thingId));
		for (int i = 0; i < lines.Count; i++)
		{
			RafTradeLine line = lines[i];
			if (line.count <= 0)
			{
				continue;
			}
			Thing thing = items.Find((Thing t) => t.thingIDNumber == line.thingId && t.def != ThingDefOf.Silver);
			if (thing == null || thing.stackCount < line.count)
			{
				reason = "RAF_PlayerTrade_Reason_Missing".Translate((thing != null) ? thing.LabelNoCount : ("#" + line.thingId)).ToString();
				return null;
			}
			take.Add(new KeyValuePair<Thing, int>(thing, line.count));
		}
		int need = s.silver;
		if (need > 0)
		{
			for (int j = 0; j < items.Count; j++)
			{
				if (need <= 0)
				{
					break;
				}
				Thing t2 = items[j];
				if (t2.def != ThingDefOf.Silver)
				{
					continue;
				}
				int n = Math.Min(need, t2.stackCount);
				take.Add(new KeyValuePair<Thing, int>(t2, n));
				need -= n;
			}
			if (need > 0)
			{
				reason = "RAF_PlayerTrade_Reason_Silver".Translate().ToString();
				return null;
			}
		}
		return take;
	}

	private static List<Thing> DoTake(RafTradeSide s, Faction f, List<KeyValuePair<Thing, int>> take)
	{
		List<Thing> goods = new List<Thing>();
		Map map = (s.anchorKind == AnchorMap) ? ResolveMap(s.anchorId) : null;
		RunAs(map, f, delegate
		{
			for (int i = 0; i < take.Count; i++)
			{
				Thing thing = take[i].Key;
				int count = Math.Min(take[i].Value, thing.stackCount);
				if (count <= 0 || thing.Destroyed)
				{
					continue;
				}
				Thing part = thing.SplitOff(count);
				if (part != null)
				{
					goods.Add(part);
				}
			}
		});
		return goods;
	}

	/// <summary>Клетка у колонистов фракции (наименьший thingID) или торговая точка сброса.</summary>
	private static IntVec3 DeliveryCell(Map map, Faction f, bool preferColonists)
	{
		if (preferColonists)
		{
			Pawn best = null;
			IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
			for (int i = 0; i < pawns.Count; i++)
			{
				Pawn p = pawns[i];
				if (p != null && p.Faction == f && p.RaceProps.Humanlike && !p.Dead && !p.Downed && (best == null || p.thingIDNumber < best.thingIDNumber))
				{
					best = p;
				}
			}
			if (best != null)
			{
				return best.Position;
			}
		}
		return DropCellFinder.TradeDropSpot(map);
	}

	private static void Deliver(List<Thing> goods, RafTradeSide to, Faction f, bool pods)
	{
		if (goods.Count == 0)
		{
			return;
		}
		if (to.anchorKind == AnchorCaravan)
		{
			Caravan caravan = ResolveCaravan(to.anchorId);
			for (int i = 0; i < goods.Count; i++)
			{
				CaravanInventoryUtility.GiveThing(caravan, goods[i]);
			}
			return;
		}
		Map map = ResolveMap(to.anchorId);
		RunAs(map, f, delegate
		{
			if (pods)
			{
				IntVec3 dropCenter = DeliveryCell(map, f, preferColonists: false);
				DropPodUtility.DropThingsNear(dropCenter, map, goods, 110, canInstaDropDuringInit: false, leaveSlag: false, canRoofPunch: false, forbid: false, allowFogged: false, f);
				return;
			}
			IntVec3 center = DeliveryCell(map, f, preferColonists: true);
			List<Thing> failed = new List<Thing>();
			for (int j = 0; j < goods.Count; j++)
			{
				if (!GenPlace.TryPlaceThing(goods[j], center, map, ThingPlaceMode.Near))
				{
					failed.Add(goods[j]);
				}
			}
			if (failed.Count > 0)
			{
				DropPodUtility.DropThingsNear(DeliveryCell(map, f, preferColonists: false), map, failed, 110, canInstaDropDuringInit: false, leaveSlag: false, canRoofPunch: false, forbid: false, allowFogged: false, f);
			}
		});
	}

	/// <summary>
	/// Оба согласны: перепроверяем всё по синхронному состоянию и переносим.
	/// Если что-то не сходится — сделка не выполняется, согласия сбрасываются.
	/// </summary>
	public static void TryExecute(RafTradeOffer o)
	{
		Faction fa = FactionById(o.a.factionId);
		Faction fb = FactionById(o.b.factionId);
		if (fa == null || fb == null || !AnchorsValid(o))
		{
			Close(o, RafTradeState.Cancelled, "RAF_PlayerTrade_Msg_AnchorLost");
			return;
		}
		string reason = null;
		if (AtWar(fa, fb))
		{
			reason = "RAF_PlayerTrade_Reason_War".Translate().ToString();
		}
		else if (o.a.lines.Count == 0 && o.a.silver <= 0 && o.b.lines.Count == 0 && o.b.silver <= 0)
		{
			reason = "RAF_PlayerTrade_Reason_Empty".Translate().ToString();
		}
		List<KeyValuePair<Thing, int>> takeA = null;
		List<KeyValuePair<Thing, int>> takeB = null;
		if (reason == null)
		{
			takeA = BuildTake(o.a, fa, out reason);
		}
		if (reason == null)
		{
			takeB = BuildTake(o.b, fb, out reason);
		}
		if (reason != null)
		{
			o.Touch();
			Notify(o, "RAF_PlayerTrade_Msg_Failed", MessageTypeDefOf.RejectInput, reason);
			return;
		}
		bool pods = UsesDropPods(o);
		List<Thing> goodsA = DoTake(o.a, fa, takeA);
		List<Thing> goodsB = DoTake(o.b, fb, takeB);
		Deliver(goodsA, o.b, fb, pods);
		Deliver(goodsB, o.a, fa, pods);
		Log.Message("[RaidAnimalFix] PlayerTrade #" + o.id + ": " + fa.Name + " отдал " + goodsA.Count + " стаков, " + fb.Name + " отдал " + goodsB.Count + " стаков" + (pods ? " (капсулы)" : " (напрямую)"));
		Close(o, RafTradeState.Done, "RAF_PlayerTrade_Msg_Done");
	}
}

/// <summary>
/// Синхронные команды торговли. Вызываются из UI; MP перехватывает вызов и
/// выполняет его у всех клиентов. Аргументы — только Faction и int: якоря и
/// вещи передаются id и заново находятся в синхронном состоянии (так команда не
/// привязывается к карте конкретной вещи и не ломается, если вещь пропала).
/// actor = MP.RealPlayerFaction кликнувшего; проверяется, что это сторона сделки.
/// </summary>
public static class RafPlayerTradeSync
{
	/// <summary>
	/// Открыть сделку. Для якоря-карты id &lt; 0 означает «домашняя карта фракции»
	/// (удалённая торговля) — выбирается в симуляции детерминированно.
	/// Если между этими фракциями уже есть открытое предложение с теми же
	/// якорями — оно переиспользуется, с другими — отменяется и заменяется.
	/// </summary>
	[SyncMethod]
	public static void CreateOffer(Faction actor, Faction other, int myKind, int myId, int otherKind, int otherId)
	{
		try
		{
			RafTradeData data = RafTradeData.Get();
			if (data == null || actor == null || other == null || actor == other || !RafTrade.IsTradePartner(actor) || !RafTrade.IsTradePartner(other))
			{
				return;
			}
			if (RafTrade.AtWar(actor, other))
			{
				RafTrade.NotifyOne(actor, other, "RAF_PlayerTrade_Msg_CannotCreate", MessageTypeDefOf.RejectInput, "RAF_PlayerTrade_Reason_War".Translate().ToString());
				return;
			}
			RafTradeSide mine = MakeSide(actor, myKind, myId);
			RafTradeSide theirs = MakeSide(other, otherKind, otherId);
			if (mine == null || theirs == null || !RafTrade.AnchorValid(mine, actor) || !RafTrade.AnchorValid(theirs, other) || !RafTrade.Colocated(mine, theirs))
			{
				RafTrade.NotifyOne(actor, other, "RAF_PlayerTrade_Msg_CannotCreate", MessageTypeDefOf.RejectInput, "RAF_PlayerTrade_Reason_Anchor".Translate().ToString());
				return;
			}
			RafTradeOffer offer = null;
			for (int i = 0; i < data.offers.Count; i++)
			{
				RafTradeOffer o = data.offers[i];
				if (o.state != RafTradeState.Open || !o.Involves(actor) || !o.Involves(other))
				{
					continue;
				}
				RafTradeSide s1 = o.SideOf(actor);
				RafTradeSide s2 = o.SideOf(other);
				if (s1.anchorKind == mine.anchorKind && s1.anchorId == mine.anchorId && s2.anchorKind == theirs.anchorKind && s2.anchorId == theirs.anchorId)
				{
					offer = o;
				}
				else
				{
					RafTrade.Close(o, RafTradeState.Cancelled, "RAF_PlayerTrade_Msg_Cancelled");
				}
			}
			if (offer == null)
			{
				offer = new RafTradeOffer
				{
					id = data.nextId++,
					a = mine,
					b = theirs,
					createdTick = RafTrade.Now(),
					state = RafTradeState.Open
				};
				data.offers.Add(offer);
				RafTrade.NotifyOne(other, actor, "RAF_PlayerTrade_Msg_Proposed", MessageTypeDefOf.PositiveEvent);
			}
			if (RafTrade.IssuedBySelf() && RafTrade.IsLocal(actor))
			{
				Find.WindowStack.Add(new Dialog_RafPlayerTrade(offer.id));
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("RafPlayerTradeSync.CreateOffer", ex);
		}
	}

	private static RafTradeSide MakeSide(Faction f, int kind, int id)
	{
		if (kind == RafTrade.AnchorMap)
		{
			Map map = (id < 0) ? RafTrade.HomeMapOf(f) : RafTrade.ResolveMap(id);
			if (map == null)
			{
				return null;
			}
			id = map.uniqueID;
		}
		else if (kind != RafTrade.AnchorCaravan)
		{
			return null;
		}
		return new RafTradeSide
		{
			factionId = f.loadID,
			anchorKind = kind,
			anchorId = id
		};
	}

	private static RafTradeOffer OpenOffer(int offerId, Faction actor, out RafTradeSide side)
	{
		side = null;
		RafTradeOffer offer = RafTradeData.Get()?.Find(offerId);
		if (offer == null || offer.state != RafTradeState.Open || actor == null)
		{
			return null;
		}
		side = offer.SideOf(actor);
		if (side == null || !RafTrade.AnchorValid(side, actor))
		{
			return null;
		}
		return offer;
	}

	/// <summary>Сколько штук вещи thingId предлагает actor (0 — убрать). Любое изменение сбрасывает оба согласия.</summary>
	[SyncMethod]
	public static void SetOfferLine(int offerId, Faction actor, int thingId, int count)
	{
		try
		{
			RafTradeSide side;
			RafTradeOffer offer = OpenOffer(offerId, actor, out side);
			if (offer == null)
			{
				return;
			}
			int idx = side.lines.FindIndex((RafTradeLine l) => l.thingId == thingId);
			if (count > 0)
			{
				Thing thing = RafTrade.FindAvailable(side, actor, thingId);
				if (thing == null)
				{
					return;
				}
				count = Math.Min(count, thing.stackCount);
			}
			int old = (idx >= 0) ? side.lines[idx].count : 0;
			if (old == Math.Max(count, 0))
			{
				return;
			}
			if (count <= 0)
			{
				side.lines.RemoveAt(idx);
			}
			else if (idx >= 0)
			{
				side.lines[idx].count = count;
			}
			else
			{
				side.lines.Add(new RafTradeLine
				{
					thingId = thingId,
					count = count
				});
			}
			offer.Touch();
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("RafPlayerTradeSync.SetOfferLine", ex);
		}
	}

	[SyncMethod]
	public static void SetSilver(int offerId, Faction actor, int count)
	{
		try
		{
			RafTradeSide side;
			RafTradeOffer offer = OpenOffer(offerId, actor, out side);
			if (offer == null)
			{
				return;
			}
			count = Math.Max(0, Math.Min(count, RafTrade.SilverCount(side, actor)));
			if (side.silver != count)
			{
				side.silver = count;
				offer.Touch();
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("RafPlayerTradeSync.SetSilver", ex);
		}
	}

	/// <summary>Согласие на версию revision. Когда согласны оба — сделка исполняется здесь же.</summary>
	[SyncMethod]
	public static void Accept(int offerId, Faction actor, int revision)
	{
		try
		{
			RafTradeSide side;
			RafTradeOffer offer = OpenOffer(offerId, actor, out side);
			if (offer == null)
			{
				return;
			}
			if (offer.revision != revision)
			{
				if (RafTrade.IsLocal(actor) && RafTrade.IssuedBySelf())
				{
					Messages.Message("RAF_PlayerTrade_Msg_Changed".Translate(), MessageTypeDefOf.RejectInput, historical: false);
				}
				return;
			}
			side.accepted = true;
			if (offer.a.accepted && offer.b.accepted)
			{
				RafTrade.TryExecute(offer);
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("RafPlayerTradeSync.Accept", ex);
		}
	}

	[SyncMethod]
	public static void CancelOffer(int offerId, Faction actor)
	{
		try
		{
			RafTradeOffer offer = RafTradeData.Get()?.Find(offerId);
			if (offer != null && offer.state == RafTradeState.Open && offer.Involves(actor))
			{
				RafTrade.Close(offer, RafTradeState.Cancelled, "RAF_PlayerTrade_Msg_Cancelled");
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("RafPlayerTradeSync.CancelOffer", ex);
		}
	}
}
