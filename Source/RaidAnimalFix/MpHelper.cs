using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RaidAnimalFix;

public static class MpHelper
{
	private static Type _multiplayerType;

	private static Type _sessionType;

	private static PropertyInfo _clientProp;

	private static PropertyInfo _localServerProp;

	private static PropertyInfo _sessionProp;

	private static FieldInfo _sessionField;

	private static PropertyInfo _realPlayerFactionProp;

	private static FieldInfo _playersField;

	private static FieldInfo _desyncedField;

	private static bool _reflectionResolved;

	// Скомпилированные геттеры: IsMultiplayer/RealPlayerFaction зовутся из
	// горячих патчей (тик пешек, письма, геттеры) — рефлексия там дорогая.
	private static Func<object> _clientGet;

	private static Func<object> _localServerGet;

	private static Func<Faction> _realPlayerFactionGet;

	private static T Getter<T>(PropertyInfo prop) where T : class
	{
		try
		{
			MethodInfo methodInfo = prop?.GetGetMethod();
			return (methodInfo == null) ? null : (Delegate.CreateDelegate(typeof(T), methodInfo, throwOnBindFailure: false) as T);
		}
		catch
		{
			return null;
		}
	}

	public static Type SessionType
	{
		get
		{
			ResolveReflection();
			return _sessionType;
		}
	}

	public static Type MultiplayerType
	{
		get
		{
			ResolveReflection();
			return _multiplayerType;
		}
	}

	public static PropertyInfo SessionProp
	{
		get
		{
			ResolveReflection();
			return _sessionProp;
		}
	}

	public static void ResolveReflectionSafe()
	{
		ResolveReflection();
	}

	private static void ResolveReflection()
	{
		if (_reflectionResolved)
		{
			return;
		}
		_reflectionResolved = true;
		try
		{
			_multiplayerType = RafTypes.Find("Multiplayer.Client.Multiplayer");
			if (_multiplayerType == null)
			{
				Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
				foreach (Assembly assembly in assemblies)
				{
					string name = assembly.GetName().Name;
					if (name != "Multiplayer" && name != "0MultiplayerAPI")
					{
						continue;
					}
					Type[] types = assembly.GetTypes();
					foreach (Type type in types)
					{
						if (type.Name == "Multiplayer" && type.IsAbstract && type.IsSealed)
						{
							_multiplayerType = type;
							break;
						}
					}
					if (_multiplayerType != null)
					{
						break;
					}
				}
			}
			if (!(_multiplayerType == null))
			{
				BindingFlags bindingAttr = BindingFlags.Static | BindingFlags.Public;
				_clientProp = _multiplayerType.GetProperty("Client", bindingAttr);
				_localServerProp = _multiplayerType.GetProperty("LocalServer", bindingAttr);
				_realPlayerFactionProp = _multiplayerType.GetProperty("RealPlayerFaction", bindingAttr);
				_clientGet = Getter<Func<object>>(_clientProp);
				_localServerGet = Getter<Func<object>>(_localServerProp);
				_realPlayerFactionGet = Getter<Func<Faction>>(_realPlayerFactionProp);
				_sessionProp = _multiplayerType.GetProperty("session", bindingAttr);
				_sessionField = _multiplayerType.GetField("session", bindingAttr);
				if (_sessionProp != null)
				{
					_sessionType = _sessionProp.PropertyType;
				}
				else if (_sessionField != null)
				{
					_sessionType = _sessionField.FieldType;
				}
				if (_sessionType != null)
				{
					_playersField = _sessionType.GetField("players", BindingFlags.Instance | BindingFlags.Public);
					_desyncedField = _sessionType.GetField("desynced", BindingFlags.Instance | BindingFlags.Public);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error("[RaidAnimalFix] Reflection resolution failed: " + ex.Message);
		}
	}

	public static bool IsMultiplayer()
	{
		ResolveReflection();
		if (_clientProp == null)
		{
			return false;
		}
		try
		{
			return ((_clientGet != null) ? _clientGet() : _clientProp.GetValue(null, null)) != null;
		}
		catch
		{
			return false;
		}
	}

	public static bool IsHost()
	{
		ResolveReflection();
		if (_localServerProp == null)
		{
			return false;
		}
		try
		{
			return ((_localServerGet != null) ? _localServerGet() : _localServerProp.GetValue(null, null)) != null;
		}
		catch
		{
			return false;
		}
	}

	public static Faction GetCurrentPlayerFaction()
	{
		ResolveReflection();
		try
		{
			if (_realPlayerFactionGet != null)
			{
				Faction faction = _realPlayerFactionGet();
				if (faction != null)
				{
					return faction;
				}
			}
			else if (_realPlayerFactionProp != null && _realPlayerFactionProp.GetValue(null, null) is Faction result)
			{
				return result;
			}
		}
		catch
		{
		}
		try
		{
			return Faction.OfPlayer;
		}
		catch
		{
			return null;
		}
	}

	public static object GetSession()
	{
		ResolveReflection();
		try
		{
			if (_sessionProp != null)
			{
				return _sessionProp.GetValue(null, null);
			}
			if (_sessionField != null)
			{
				return _sessionField.GetValue(null);
			}
		}
		catch
		{
		}
		return null;
	}

	public static bool IsSessionDesynced()
	{
		try
		{
			object session = GetSession();
			if (session == null || _desyncedField == null)
			{
				return false;
			}
			return (bool)_desyncedField.GetValue(session);
		}
		catch
		{
			return false;
		}
	}

	public static int GetPlayerCount()
	{
		ResolveReflection();
		try
		{
			object session = GetSession();
			if (session != null && _playersField != null && _playersField.GetValue(session) is IList list && list.Count > 0)
			{
				return list.Count;
			}
		}
		catch
		{
		}
		return 1;
	}

	public static bool IsMapForCurrentPlayer(Map map)
	{
		if (!IsMultiplayer())
		{
			return true;
		}
		if (map == null)
		{
			return false;
		}
		try
		{
			Faction currentPlayerFaction = GetCurrentPlayerFaction();
			Faction parentFaction = map.ParentFaction;
			if (currentPlayerFaction == null || parentFaction == null)
			{
				return true;
			}
			return parentFaction == currentPlayerFaction;
		}
		catch
		{
			return true;
		}
	}

	public static bool IsTargetForCurrentPlayer(object target)
	{
		if (!IsMultiplayer())
		{
			return true;
		}
		if (target == null)
		{
			return false;
		}
		try
		{
			if (target is Map map)
			{
				return IsMapForCurrentPlayer(map);
			}
			if (target is WorldObject worldObject)
			{
				Faction currentPlayerFaction = GetCurrentPlayerFaction();
				if (currentPlayerFaction == null || worldObject.Faction == null)
				{
					return true;
				}
				return worldObject.Faction == currentPlayerFaction;
			}
		}
		catch
		{
		}
		return true;
	}
}
