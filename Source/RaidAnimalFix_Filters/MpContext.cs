using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RaidAnimalFix;

internal static class MpContext
{
	private static bool _resolved;

	private static FieldInfo _tickingMapField;

	private static FieldInfo _tickingWorldField;

	private static PropertyInfo _worldCompProp;

	private static FieldInfo _spectatorField;

	// Быстрый доступ: эти проверки идут из горячих патчей.
	private static AccessTools.FieldRef<Map> _tickingMapRef;

	private static AccessTools.FieldRef<bool> _tickingWorldRef;

	// Фракция-наблюдатель за партию не меняется — кэш на Game.
	private static Game _spectatorGame;

	private static Faction _spectatorCached;

	private static void Resolve()
	{
		if (_resolved)
		{
			return;
		}
		_resolved = true;
		try
		{
			Type type = AccessTools.TypeByName("Multiplayer.Client.AsyncTimeComp");
			if (type != null)
			{
				_tickingMapField = type.GetField("tickingMap", BindingFlags.Static | BindingFlags.Public);
			}
			Type type2 = AccessTools.TypeByName("Multiplayer.Client.AsyncTime.AsyncWorldTimeComp") ?? AccessTools.TypeByName("Multiplayer.Client.AsyncWorldTimeComp");
			if (type2 != null)
			{
				_tickingWorldField = type2.GetField("tickingWorld", BindingFlags.Static | BindingFlags.Public);
			}
			try
			{
				if (_tickingMapField != null && _tickingMapField.FieldType == typeof(Map))
				{
					_tickingMapRef = AccessTools.StaticFieldRefAccess<Map>(_tickingMapField);
				}
				if (_tickingWorldField != null && _tickingWorldField.FieldType == typeof(bool))
				{
					_tickingWorldRef = AccessTools.StaticFieldRefAccess<bool>(_tickingWorldField);
				}
			}
			catch
			{
				_tickingMapRef = null;
				_tickingWorldRef = null;
			}
			MpHelper.ResolveReflectionSafe();
			Type multiplayerType = MpHelper.MultiplayerType;
			if (multiplayerType != null)
			{
				_worldCompProp = multiplayerType.GetProperty("WorldComp", BindingFlags.Static | BindingFlags.Public);
			}
			if (_worldCompProp != null)
			{
				_spectatorField = _worldCompProp.PropertyType.GetField("spectatorFaction", BindingFlags.Instance | BindingFlags.Public);
			}
			Log.Message("[RaidAnimalFix] MpContext: tickingMap=" + (_tickingMapField != null) + " tickingWorld=" + (_tickingWorldField != null) + " spectator=" + (_spectatorField != null));
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] MpContext resolve failed: " + ex.Message);
		}
	}

	public static Map TickingMap()
	{
		Resolve();
		if (_tickingMapField == null)
		{
			return null;
		}
		try
		{
			return (_tickingMapRef != null) ? _tickingMapRef() : (_tickingMapField.GetValue(null) as Map);
		}
		catch
		{
			return null;
		}
	}

	public static bool TickingWorld()
	{
		Resolve();
		if (_tickingWorldField == null)
		{
			return false;
		}
		try
		{
			return (_tickingWorldRef != null) ? _tickingWorldRef() : ((bool)_tickingWorldField.GetValue(null));
		}
		catch
		{
			return false;
		}
	}

	public static bool SpectatorKnown()
	{
		Resolve();
		return _spectatorField != null;
	}

	public static Faction Spectator()
	{
		Resolve();
		if (_worldCompProp == null || _spectatorField == null)
		{
			return null;
		}
		Game game = Current.Game;
		if (game != null && game == _spectatorGame && _spectatorCached != null)
		{
			return _spectatorCached;
		}
		try
		{
			object value = _worldCompProp.GetValue(null, null);
			if (value == null)
			{
				return null;
			}
			Faction faction = _spectatorField.GetValue(value) as Faction;
			if (faction != null)
			{
				_spectatorGame = game;
				_spectatorCached = faction;
			}
			return faction;
		}
		catch
		{
			return null;
		}
	}

	public static bool IsSpectator(Faction f)
	{
		if (f == null)
		{
			return false;
		}
		Faction faction = Spectator();
		if (faction != null)
		{
			return faction == f;
		}
		return f.Name == "Spectator";
	}

	public static bool IsRealPlayerFaction(Faction f)
	{
		if (f != null && f.IsPlayer)
		{
			return !IsSpectator(f);
		}
		return false;
	}
}
