using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RaidAnimalFix;

public static class PlayerFactionDetector
{
	private const int REFRESH_INTERVAL = 120;

	private static Dictionary<int, int> _allTileOwners = new Dictionary<int, int>();

	private static Dictionary<int, bool> _isPlayerFactionCache = new Dictionary<int, bool>();

	private static int _cachedCurrentFactionId = -1;

	private static int _lastLoggedFactionId = -1;

	private static int _lastRefreshTick = 0;

	private static bool _initialized = false;

	private static Game _game;

	// Часы в MP разные (карта/мир), а в новой игре TicksGame меньше прошлой —
	// разница уходила в минус и кэш тайлов не обновлялся вовсе.
	private static bool Stale()
	{
		if (!_initialized || _game != Current.Game)
		{
			return true;
		}
		int num = (Find.TickManager != null) ? Find.TickManager.TicksGame : 0;
		return Math.Abs(num - _lastRefreshTick) > 120;
	}

	public static int GetCurrentPlayerFactionId()
	{
		if (!MpHelper.IsMultiplayer())
		{
			return -1;
		}
		if (Stale())
		{
			Refresh();
		}
		return _cachedCurrentFactionId;
	}

	public static void Refresh()
	{
		_initialized = true;
		_game = Current.Game;
		_lastRefreshTick = ((Find.TickManager != null) ? Find.TickManager.TicksGame : 0);
		BuildTileMap();
		DetectCurrentPlayerFaction();
	}

	private static void BuildTileMap()
	{
		_allTileOwners.Clear();
		if (Find.World == null || Find.WorldObjects == null)
		{
			return;
		}
		try
		{
			foreach (Settlement settlement in Find.WorldObjects.Settlements)
			{
				if (settlement != null && settlement.Faction != null)
				{
					_allTileOwners[settlement.Tile] = settlement.Faction.loadID;
				}
			}
			foreach (WorldObject allWorldObject in Find.WorldObjects.AllWorldObjects)
			{
				if (allWorldObject != null && allWorldObject.Faction != null && !(allWorldObject is Caravan) && !_allTileOwners.ContainsKey(allWorldObject.Tile))
				{
					_allTileOwners[allWorldObject.Tile] = allWorldObject.Faction.loadID;
				}
			}
		}
		catch
		{
		}
	}

	private static void DetectCurrentPlayerFaction()
	{
		_cachedCurrentFactionId = -1;
		try
		{
			MpHelper.ResolveReflectionSafe();
			Type multiplayerType = MpHelper.MultiplayerType;
			if (multiplayerType != null)
			{
				PropertyInfo property = multiplayerType.GetProperty("RealPlayerFaction", BindingFlags.Static | BindingFlags.Public);
				if (property != null)
				{
					object value = property.GetValue(null, null);
					if (value is Faction faction)
					{
						_cachedCurrentFactionId = faction.loadID;
						if (_lastLoggedFactionId != faction.loadID)
						{
							_lastLoggedFactionId = faction.loadID;
							Log.Message("[RaidAnimalFix] PlayerFactionDetector: RealPlayerFaction = " + faction.loadID + " (" + ((faction.def != null) ? faction.def.defName : "?") + ")");
						}
						return;
					}
				}
			}
		}
		catch
		{
		}
		try
		{
			if (Find.Maps != null)
			{
				Dictionary<int, int> dictionary = new Dictionary<int, int>();
				foreach (Map map in Find.Maps)
				{
					if (map == null || map.mapPawns == null)
					{
						continue;
					}
					foreach (Pawn freeColonist in map.mapPawns.FreeColonists)
					{
						if (freeColonist != null && freeColonist.Faction != null)
						{
							int loadID = freeColonist.Faction.loadID;
							if (dictionary.ContainsKey(loadID))
							{
								dictionary[loadID]++;
							}
							else
							{
								dictionary[loadID] = 1;
							}
						}
					}
				}
				int num = 0;
				int num2 = -1;
				foreach (KeyValuePair<int, int> item in dictionary)
				{
					if (item.Value > num)
					{
						num = item.Value;
						num2 = item.Key;
					}
				}
				if (num2 >= 0)
				{
					_cachedCurrentFactionId = num2;
					if (_lastLoggedFactionId != num2)
					{
						_lastLoggedFactionId = num2;
						Log.Message("[RaidAnimalFix] PlayerFactionDetector: by pawns = " + num2 + " (" + num + " colonists)");
					}
					return;
				}
			}
		}
		catch
		{
		}
		try
		{
			if (Faction.OfPlayer != null)
			{
				_cachedCurrentFactionId = Faction.OfPlayer.loadID;
				if (_lastLoggedFactionId != _cachedCurrentFactionId)
				{
					_lastLoggedFactionId = _cachedCurrentFactionId;
					Log.Message("[RaidAnimalFix] PlayerFactionDetector: Faction.OfPlayer = " + Faction.OfPlayer.loadID);
				}
			}
		}
		catch
		{
		}
	}

	public static bool IsTileForCurrentPlayer(int tile)
	{
		if (!MpHelper.IsMultiplayer())
		{
			return true;
		}
		if (Stale())
		{
			Refresh();
		}
		if (!_allTileOwners.ContainsKey(tile))
		{
			return true;
		}
		int num = _allTileOwners[tile];
		int currentPlayerFactionId = GetCurrentPlayerFactionId();
		if (currentPlayerFactionId < 0)
		{
			return true;
		}
		return num == currentPlayerFactionId;
	}

	public static int GetTileOwner(int tile)
	{
		if (Stale())
		{
			Refresh();
		}
		if (_allTileOwners.TryGetValue(tile, out var value))
		{
			return value;
		}
		return -1;
	}

	public static bool IsPlayerFaction(Faction faction)
	{
		if (faction == null)
		{
			return false;
		}
		if (faction == Faction.OfPlayer)
		{
			return true;
		}
		int loadID = faction.loadID;
		if (_isPlayerFactionCache.TryGetValue(loadID, out var value))
		{
			return value;
		}
		bool flag = false;
		try
		{
			if (Find.Maps != null)
			{
				foreach (Map map in Find.Maps)
				{
					if (map == null || map.mapPawns == null || map.mapPawns.FreeColonists == null)
					{
						continue;
					}
					foreach (Pawn freeColonist in map.mapPawns.FreeColonists)
					{
						if (freeColonist.Faction == faction)
						{
							flag = true;
							break;
						}
					}
					if (flag)
					{
						break;
					}
				}
			}
		}
		catch
		{
		}
		if (!flag && faction.def != null && faction.def.defName != null)
		{
			string defName = faction.def.defName;
			if (defName.Contains("Player") || defName.Contains("PlayerFaction"))
			{
				flag = true;
			}
		}
		_isPlayerFactionCache[loadID] = flag;
		return flag;
	}

	public static void Invalidate()
	{
		_initialized = false;
		_cachedCurrentFactionId = -1;
	}
}
