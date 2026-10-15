using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RaidAnimalFix;

internal static class MpMapTime
{
	private static bool _resolved;

	private static MethodInfo _asyncTimeOfMap;

	private static FieldInfo _mapTicks;

	private static FieldInfo _gameField;

	private static FieldInfo _worldTimeComp;

	private static FieldInfo _worldTicks;

	private static void Resolve()
	{
		if (_resolved)
		{
			return;
		}
		_resolved = true;
		try
		{
			Type type = RafTypes.Find("Multiplayer.Client.Extensions");
			if (type != null)
			{
				_asyncTimeOfMap = AccessTools.Method(type, "AsyncTime", new Type[1] { typeof(Map) });
			}
			Type type2 = RafTypes.Find("Multiplayer.Client.AsyncTimeComp");
			if (type2 != null)
			{
				_mapTicks = type2.GetField("mapTicks", BindingFlags.Instance | BindingFlags.Public);
			}
			MpHelper.ResolveReflectionSafe();
			Type multiplayerType = MpHelper.MultiplayerType;
			if (multiplayerType != null)
			{
				_gameField = multiplayerType.GetField("game", BindingFlags.Static | BindingFlags.Public);
			}
			if (_gameField != null)
			{
				_worldTimeComp = _gameField.FieldType.GetField("asyncWorldTimeComp", BindingFlags.Instance | BindingFlags.Public);
				if (_worldTimeComp != null)
				{
					_worldTicks = _worldTimeComp.FieldType.GetField("worldTicks", BindingFlags.Instance | BindingFlags.Public);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] MpMapTime resolve failed: " + ex.Message);
		}
	}

	public static bool TryGetMapTicks(Map map, out int ticks)
	{
		ticks = 0;
		Resolve();
		if (map == null || _asyncTimeOfMap == null || _mapTicks == null)
		{
			return false;
		}
		try
		{
			object obj = _asyncTimeOfMap.Invoke(null, new object[1] { map });
			if (obj == null)
			{
				return false;
			}
			ticks = (int)_mapTicks.GetValue(obj);
			return true;
		}
		catch
		{
			return false;
		}
	}

	public static bool TryGetWorldTicks(out int ticks)
	{
		ticks = 0;
		Resolve();
		if (_gameField == null || _worldTimeComp == null || _worldTicks == null)
		{
			return false;
		}
		try
		{
			object value = _gameField.GetValue(null);
			if (value == null)
			{
				return false;
			}
			object value2 = _worldTimeComp.GetValue(value);
			if (value2 == null)
			{
				return false;
			}
			ticks = (int)_worldTicks.GetValue(value2);
			return true;
		}
		catch
		{
			return false;
		}
	}
}
