using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace RaidAnimalFix;

internal static class UiOnly
{
	private static bool _resolved;

	private static PropertyInfo _inInterface;

	private static Func<bool> _inInterfaceGet;

	public static bool InInterface()
	{
		if (!_resolved)
		{
			_resolved = true;
			try
			{
				MpHelper.ResolveReflectionSafe();
				Type multiplayerType = MpHelper.MultiplayerType;
				if (multiplayerType != null)
				{
					_inInterface = multiplayerType.GetProperty("InInterface", BindingFlags.Static | BindingFlags.Public);
					MethodInfo getter = _inInterface?.GetGetMethod();
					_inInterfaceGet = (getter == null) ? null : (Delegate.CreateDelegate(typeof(Func<bool>), getter, throwOnBindFailure: false) as Func<bool>);
				}
			}
			catch
			{
			}
		}
		if (_inInterface == null)
		{
			return false;
		}
		// Колбэки длинных событий и ExecuteWhenFinished бегут после
		// currentEvent = null, и MP считает это «интерфейсом», хотя там может
		// идти отложенный код симуляции. Не фильтруем в этот момент.
		if (MpSim.simEventDepth > 0 || LongEventHandler.AnyEventNowOrWaiting || ExecutingWhenFinished())
		{
			return false;
		}
		try
		{
			return (_inInterfaceGet != null) ? _inInterfaceGet() : ((bool)_inInterface.GetValue(null, null));
		}
		catch
		{
			return false;
		}
	}

	private static FieldInfo _executingWhenFinished;

	private static bool _ewfResolved;

	private static bool ExecutingWhenFinished()
	{
		if (!_ewfResolved)
		{
			_ewfResolved = true;
			_executingWhenFinished = HarmonyLib.AccessTools.Field(typeof(LongEventHandler), "executingToExecuteWhenFinished");
		}
		try
		{
			return _executingWhenFinished != null && (bool)_executingWhenFinished.GetValue(null);
		}
		catch
		{
			return false;
		}
	}

	public static List<Pawn> FilterPrisoners(List<Pawn> src)
	{
		if (src == null || src.Count == 0 || !MpHelper.IsMultiplayer() || !InInterface())
		{
			return src;
		}
		Faction ofPlayer = Faction.OfPlayer;
		if (!MpContext.IsRealPlayerFaction(ofPlayer))
		{
			return src;
		}
		bool flag = false;
		for (int i = 0; i < src.Count; i++)
		{
			if (src[i].HostFaction != ofPlayer)
			{
				flag = true;
				break;
			}
		}
		if (!flag)
		{
			return src;
		}
		List<Pawn> list = new List<Pawn>(src.Count);
		for (int j = 0; j < src.Count; j++)
		{
			if (src[j].HostFaction == ofPlayer)
			{
				list.Add(src[j]);
			}
		}
		return list;
	}

	public static List<Pawn> FilterSlaves(List<Pawn> src)
	{
		if (src == null || src.Count == 0 || !MpHelper.IsMultiplayer() || !InInterface())
		{
			return src;
		}
		Faction ofPlayer = Faction.OfPlayer;
		if (!MpContext.IsRealPlayerFaction(ofPlayer))
		{
			return src;
		}
		bool flag = false;
		for (int i = 0; i < src.Count; i++)
		{
			if (src[i].Faction != ofPlayer)
			{
				flag = true;
				break;
			}
		}
		if (!flag)
		{
			return src;
		}
		List<Pawn> list = new List<Pawn>(src.Count);
		for (int j = 0; j < src.Count; j++)
		{
			if (src[j].Faction == ofPlayer)
			{
				list.Add(src[j]);
			}
		}
		return list;
	}
}
