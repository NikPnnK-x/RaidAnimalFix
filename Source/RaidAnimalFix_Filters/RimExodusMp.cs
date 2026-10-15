using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace RaidAnimalFix;

/// <summary>
/// Общие помощники совместимости с RimExodus — Seamless World в мультиплеере.
/// Ни на RimExodus, ни на Multiplayer ссылок нет — всё через рефлексию.
///
/// Главная замена: RimExodus решает «карта активна» по Find.CurrentMap (камера
/// ЭТОГО клиента). В MP есть синхронный аналог — AsyncTimeComp.CurrentPlayerCount
/// (сколько игроков смотрит на карту; меняется мировой командой PlayerCount,
/// одинаково у всех клиентов).
/// </summary>
internal static class RimExodusMp
{
	private static bool _resolved;

	private static bool _present;

	// Multiplayer
	private static MethodInfo _asyncTimeOfMap;

	private static PropertyInfo _playerCount;

	private static FieldInfo _asyncMap;

	private static PropertyInfo _desiredTimeSpeed;

	private static MethodInfo _mpCompOfMap;

	private static FieldInfo _factionData;

	private static FieldInfo _fmdAreaManager;

	private static PropertyInfo _gameComp;

	private static FieldInfo _asyncTimeFlag;

	private static PropertyInfo _worldComp;

	private static FieldInfo _spectatorFaction;

	// RimExodus
	private static MethodInfo _isDormant;

	private static MethodInfo _wake;

	private static MethodInfo _sleep;

	private static MethodInfo _unthrottle;

	private static MethodInfo _isGoverned;

	private static MethodInfo _isProtectedHome;

	private static MethodInfo _hasPlayerPawn;

	private static MethodInfo _isGenerating;

	private static Type _tileManagerType;

	private static MethodInfo _removeRollingMap;

	private static FieldInfo _neighbors;

	private static FieldInfo _linkNeighbor;

	private static Type _tileParentType;

	private static FieldInfo _preserveRecord;

	private static MethodInfo _releaseTileMesh;

	private static MethodInfo _tryBridgeJob;

	public static Type T(string name)
	{
		return RafTypes.Find("RimExodus." + name);
	}

	private static MethodInfo M(Type type, string name, Type[] args)
	{
		return (type == null) ? null : AccessTools.Method(type, name, args);
	}

	private static void Resolve()
	{
		if (_resolved)
		{
			return;
		}
		_resolved = true;
		try
		{
			_present = T("SeamlessDormancyGovernor") != null || T("IncrementalMapGenerator") != null;
			Type ext = RafTypes.Find("Multiplayer.Client.Extensions");
			_asyncTimeOfMap = M(ext, "AsyncTime", new Type[1] { typeof(Map) });
			_mpCompOfMap = M(ext, "MpComp", new Type[1] { typeof(Map) });
			Type atc = RafTypes.Find("Multiplayer.Client.AsyncTimeComp");
			if (atc != null)
			{
				_playerCount = AccessTools.Property(atc, "CurrentPlayerCount");
				_asyncMap = AccessTools.Field(atc, "map");
				_desiredTimeSpeed = AccessTools.Property(atc, "DesiredTimeSpeed");
			}
			Type mmc = RafTypes.Find("Multiplayer.Client.MultiplayerMapComp");
			_factionData = (mmc == null) ? null : AccessTools.Field(mmc, "factionData");
			Type fmd = RafTypes.Find("Multiplayer.Client.FactionMapData");
			_fmdAreaManager = (fmd == null) ? null : AccessTools.Field(fmd, "areaManager");
			Type mp = RafTypes.Find("Multiplayer.Client.Multiplayer");
			_gameComp = (mp == null) ? null : AccessTools.Property(mp, "GameComp");
			Type gc = RafTypes.Find("Multiplayer.Client.Comp.MultiplayerGameComp") ?? RafTypes.Find("Multiplayer.Client.MultiplayerGameComp");
			_asyncTimeFlag = (gc == null) ? null : AccessTools.Field(gc, "asyncTime");
			_worldComp = (mp == null) ? null : AccessTools.Property(mp, "WorldComp");
			Type wc = RafTypes.Find("Multiplayer.Client.MultiplayerWorldComp");
			_spectatorFaction = (wc == null) ? null : AccessTools.Field(wc, "spectatorFaction");
			if (!_present)
			{
				return;
			}
			Type dm = T("SeamlessDormancyManager");
			_isDormant = M(dm, "IsDormant", new Type[1] { typeof(Map) });
			_wake = M(dm, "Wake", new Type[2]
			{
				typeof(Map),
				typeof(string)
			});
			_sleep = M(dm, "Sleep", new Type[3]
			{
				typeof(Map),
				typeof(string),
				typeof(bool)
			});
			_unthrottle = M(T("SeamlessTickThrottle"), "Unthrottle", new Type[3]
			{
				typeof(Map),
				typeof(string),
				typeof(bool)
			});
			Type gov = T("SeamlessMapGovernance");
			_isGoverned = M(gov, "IsGoverned", new Type[1] { typeof(Map) });
			_isProtectedHome = M(gov, "IsProtectedHome", new Type[1] { typeof(Map) });
			_hasPlayerPawn = M(gov, "HasPlayerPawn", new Type[1] { typeof(Map) });
			_isGenerating = M(T("IncrementalMapGenerator"), "IsGenerating", new Type[1] { typeof(Map) });
			_tileManagerType = T("SeamlessTileManager");
			_removeRollingMap = M(_tileManagerType, "RemoveRollingMap", new Type[2]
			{
				typeof(MapParent),
				typeof(bool)
			});
			_neighbors = (_tileManagerType == null) ? null : AccessTools.Field(_tileManagerType, "neighbors");
			Type link = T("NeighborLink");
			_linkNeighbor = (link == null) ? null : AccessTools.Field(link, "neighbor");
			_tileParentType = T("MapParent_SeamlessTile");
			_preserveRecord = (_tileParentType == null) ? null : AccessTools.Field(_tileParentType, "preserveRecord");
			_releaseTileMesh = M(T("TileWorldIcons"), "ReleaseTileMesh", new Type[1] { typeof(PlanetTile) });
			_tryBridgeJob = M(T("SeamlessCrossMapOrders"), "TryBridgeJob", new Type[4]
			{
				typeof(Pawn),
				typeof(Map),
				typeof(IntVec3),
				typeof(Job)
			});
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] RimExodusMp resolve: " + ex.Message);
		}
	}

	/// <summary>RimExodus загружен.</summary>
	public static bool Present
	{
		get
		{
			Resolve();
			return _present;
		}
	}

	/// <summary>RimExodus загружен и идёт мультиплеер.</summary>
	public static bool Active => Present && MpHelper.IsMultiplayer();

	public static object AsyncTime(Map map)
	{
		Resolve();
		if (map == null || _asyncTimeOfMap == null)
		{
			return null;
		}
		try
		{
			return _asyncTimeOfMap.Invoke(null, new object[1] { map });
		}
		catch
		{
			return null;
		}
	}

	public static Map MapOfAsyncTime(object comp)
	{
		Resolve();
		return (comp == null || _asyncMap == null) ? null : (_asyncMap.GetValue(comp) as Map);
	}

	/// <summary>
	/// Синхронная замена «map == Find.CurrentMap»: на карту смотрит хотя бы один
	/// игрок (по счётчику MP). Одинаково у всех клиентов.
	/// </summary>
	public static bool AnyPlayerViewing(Map map)
	{
		Resolve();
		object comp = AsyncTime(map);
		if (comp == null || _playerCount == null)
		{
			return false;
		}
		try
		{
			return (int)_playerCount.GetValue(comp, null) > 0;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>Включено ли асинхронное время в этой MP-игре.</summary>
	public static bool AsyncTimeOn
	{
		get
		{
			Resolve();
			try
			{
				object gc = _gameComp?.GetValue(null, null);
				return gc != null && _asyncTimeFlag != null && (bool)_asyncTimeFlag.GetValue(gc);
			}
			catch
			{
				return false;
			}
		}
	}

	public static bool TryGetSpeed(Map map, out TimeSpeed speed)
	{
		speed = TimeSpeed.Paused;
		object comp = AsyncTime(map);
		if (comp == null || _desiredTimeSpeed == null)
		{
			return false;
		}
		try
		{
			speed = (TimeSpeed)_desiredTimeSpeed.GetValue(comp, null);
			return true;
		}
		catch
		{
			return false;
		}
	}

	public static void SetSpeed(Map map, TimeSpeed speed)
	{
		object comp = AsyncTime(map);
		if (comp != null && _desiredTimeSpeed != null)
		{
			try
			{
				_desiredTimeSpeed.SetValue(comp, speed, null);
			}
			catch
			{
			}
		}
	}

	/// <summary>Фракция-наблюдатель MP (в мировом тике она стоит в контексте вместо игрока).</summary>
	public static Faction SpectatorFaction
	{
		get
		{
			Resolve();
			try
			{
				object wc = _worldComp?.GetValue(null, null);
				return (wc == null || _spectatorFaction == null) ? null : (_spectatorFaction.GetValue(wc) as Faction);
			}
			catch
			{
				return null;
			}
		}
	}

	/// <summary>Фракция живого игрока (не наблюдатель MP).</summary>
	public static bool IsPlayerFaction(Faction f)
	{
		return f != null && f.IsPlayer && f != SpectatorFaction;
	}

	/// <summary>
	/// В MP у каждой фракции свой AreaManager на карте (MultiplayerMapComp.factionData),
	/// а в мировом тике на всех картах выставлена фракция-наблюдатель. Ищем фракцию
	/// с наибольшей домашней зоной — её зону и надо учитывать при решении «сохранить карту».
	/// </summary>
	public static Faction BestHomeFaction(Map map, out int homeCells)
	{
		homeCells = 0;
		Resolve();
		if (map == null || _mpCompOfMap == null || _factionData == null || _fmdAreaManager == null)
		{
			return null;
		}
		try
		{
			object comp = _mpCompOfMap.Invoke(null, new object[1] { map });
			if (!(((comp == null) ? null : _factionData.GetValue(comp)) is IDictionary dict))
			{
				return null;
			}
			int bestId = int.MinValue;
			foreach (DictionaryEntry entry in dict)
			{
				int count = ((_fmdAreaManager.GetValue(entry.Value) as AreaManager)?.Home?.TrueCount).GetValueOrDefault();
				int id = (int)entry.Key;
				// Ключи упорядочены (SortedDictionary) — при равенстве берём меньший id, детерминированно.
				if (count > homeCells)
				{
					homeCells = count;
					bestId = id;
				}
			}
			if (bestId == int.MinValue)
			{
				return null;
			}
			List<Faction> factions = Find.FactionManager.AllFactionsListForReading;
			for (int i = 0; i < factions.Count; i++)
			{
				if (factions[i].loadID == bestId)
				{
					return factions[i];
				}
			}
		}
		catch
		{
		}
		return null;
	}

	/// <summary>Домашняя зона других игроков (кроме actor) на карте.</summary>
	public static int OtherPlayersHomeCells(Map map, Faction actor)
	{
		Resolve();
		if (map == null || _mpCompOfMap == null || _factionData == null || _fmdAreaManager == null)
		{
			return 0;
		}
		int total = 0;
		try
		{
			object comp = _mpCompOfMap.Invoke(null, new object[1] { map });
			if (((comp == null) ? null : _factionData.GetValue(comp)) is IDictionary dict)
			{
				foreach (DictionaryEntry entry in dict)
				{
					if (actor == null || (int)entry.Key != actor.loadID)
					{
						total += ((_fmdAreaManager.GetValue(entry.Value) as AreaManager)?.Home?.TrueCount).GetValueOrDefault();
					}
				}
			}
		}
		catch
		{
		}
		return total;
	}

	public static bool OtherPlayerPawns(Map map, Faction actor)
	{
		IReadOnlyList<Pawn> pawns = map?.mapPawns?.AllPawnsSpawned;
		if (pawns == null)
		{
			return false;
		}
		for (int i = 0; i < pawns.Count; i++)
		{
			Faction f = pawns[i].Faction;
			if (f != null && f != actor && IsPlayerFaction(f))
			{
				return true;
			}
		}
		return false;
	}

	// ---- вызовы RimExodus ----

	private static object Call(MethodInfo m, params object[] args)
	{
		if (m == null)
		{
			return null;
		}
		try
		{
			return m.Invoke(null, args);
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] RimExodus call " + m.Name + ": " + (ex.InnerException ?? ex).Message);
			return null;
		}
	}

	private static bool CallBool(MethodInfo m, Map map)
	{
		Resolve();
		return Call(m, map) is bool b && b;
	}

	public static bool IsDormant(Map map)
	{
		return CallBool(_isDormant, map);
	}

	public static bool IsGoverned(Map map)
	{
		return CallBool(_isGoverned, map);
	}

	public static bool IsProtectedHome(Map map)
	{
		return CallBool(_isProtectedHome, map);
	}

	public static bool HasPlayerPawn(Map map)
	{
		return CallBool(_hasPlayerPawn, map);
	}

	public static bool IsGenerating(Map map)
	{
		return CallBool(_isGenerating, map);
	}

	public static void Wake(Map map, string reason)
	{
		Resolve();
		Call(_wake, map, reason);
	}

	public static void Sleep(Map map, string reason, bool manual)
	{
		Resolve();
		Call(_sleep, map, reason, manual);
	}

	public static void Unthrottle(Map map, string reason)
	{
		Resolve();
		Call(_unthrottle, map, reason, true);
	}

	public static bool TryBridgeJob(Pawn pawn, Map targetMap, IntVec3 cell)
	{
		Resolve();
		return Call(_tryBridgeJob, pawn, targetMap, cell, null) is bool b && b;
	}

	public static void RemoveRollingMap(Map map, MapParent parent)
	{
		Resolve();
		if (map == null || _tileManagerType == null || _removeRollingMap == null)
		{
			return;
		}
		MapComponent comp = map.GetComponent(_tileManagerType);
		if (comp == null)
		{
			return;
		}
		try
		{
			_removeRollingMap.Invoke(comp, new object[2] { parent, false });
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] RimExodus RemoveRollingMap: " + (ex.InnerException ?? ex).Message);
		}
	}

	public static bool IsTileParent(MapParent parent)
	{
		Resolve();
		return parent != null && _tileParentType != null && _tileParentType.IsInstanceOfType(parent);
	}

	public static bool HasPreserveRecord(MapParent parent)
	{
		Resolve();
		return IsTileParent(parent) && _preserveRecord != null && _preserveRecord.GetValue(parent) != null;
	}

	public static void ReleaseTileMesh(PlanetTile tile)
	{
		Resolve();
		Call(_releaseTileMesh, tile);
	}

	/// <summary>
	/// Карты, связанные с данной швами RimExodus (через NeighborLink), включая её саму.
	/// Обход в ширину; порядок детерминирован (порядок списков соседей сохраняется в сейве).
	/// </summary>
	public static List<Map> ConnectedMaps(Map start, Predicate<Map> canEnter = null)
	{
		Resolve();
		List<Map> result = new List<Map>();
		if (start == null || _tileManagerType == null || _neighbors == null || _linkNeighbor == null)
		{
			return result;
		}
		HashSet<Map> seen = new HashSet<Map> { start };
		Queue<Map> queue = new Queue<Map>();
		queue.Enqueue(start);
		while (queue.Count > 0)
		{
			Map map = queue.Dequeue();
			result.Add(map);
			if (!(map.GetComponent(_tileManagerType) is MapComponent comp) || !(_neighbors.GetValue(comp) is IList links))
			{
				continue;
			}
			for (int i = 0; i < links.Count; i++)
			{
				Map other = (links[i] == null) ? null : (_linkNeighbor.GetValue(links[i]) as MapParent)?.Map;
				if (other != null && !other.Disposed && seen.Add(other) && (canEnter == null || canEnter(other)))
				{
					queue.Enqueue(other);
				}
			}
		}
		return result;
	}
}
