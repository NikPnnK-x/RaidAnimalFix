using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RaidAnimalFix;

internal static class MpSessionHelper
{
	private static bool _resolved;

	private static MethodInfo _mpCompMethod;

	private static FieldInfo _sessionManagerField;

	private static PropertyInfo _allSessionsProp;

	private static void Resolve()
	{
		if (_resolved)
		{
			return;
		}
		_resolved = true;
		try
		{
			Type type = AccessTools.TypeByName("Multiplayer.Client.Extensions");
			if (type != null)
			{
				_mpCompMethod = type.GetMethod("MpComp", BindingFlags.Static | BindingFlags.Public, null, new Type[1] { typeof(Map) }, null);
			}
			if (_mpCompMethod == null)
			{
				Log.Warning("[RaidAnimalFix] MpSessionHelper: Multiplayer.Client.Extensions.MpComp(Map) not found — session check disabled");
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] MpSessionHelper resolve failed: " + ex.Message);
		}
	}

	public static Faction GetForeignCaravanSessionFaction(Map map, Faction me)
	{
		Resolve();
		if (_mpCompMethod == null || map == null)
		{
			return null;
		}
		try
		{
			object obj = _mpCompMethod.Invoke(null, new object[1] { map });
			if (obj == null)
			{
				return null;
			}
			if (_sessionManagerField == null)
			{
				_sessionManagerField = obj.GetType().GetField("sessionManager", BindingFlags.Instance | BindingFlags.Public);
			}
			if (_sessionManagerField == null)
			{
				return null;
			}
			object value = _sessionManagerField.GetValue(obj);
			if (value == null)
			{
				return null;
			}
			if (_allSessionsProp == null)
			{
				_allSessionsProp = value.GetType().GetProperty("AllSessions", BindingFlags.Instance | BindingFlags.Public);
			}
			if (_allSessionsProp == null)
			{
				return null;
			}
			if (!(_allSessionsProp.GetValue(value, null) is IEnumerable enumerable))
			{
				return null;
			}
			foreach (object item in enumerable)
			{
				if (item != null && !(item.GetType().Name != "CaravanFormingSession"))
				{
					FieldInfo field = item.GetType().GetField("faction", BindingFlags.Instance | BindingFlags.Public);
					Faction faction = ((field != null) ? (field.GetValue(item) as Faction) : null);
					if (faction != null && faction != me)
					{
						return faction;
					}
				}
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("MpSessionHelper", ex);
		}
		return null;
	}
}
