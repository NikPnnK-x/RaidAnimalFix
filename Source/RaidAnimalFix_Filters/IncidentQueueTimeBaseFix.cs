using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RaidAnimalFix;

[HarmonyPatch(typeof(IncidentQueue))]
[HarmonyPatch("IncidentQueueTick")]
public static class IncidentQueueTimeBaseFix
{
	private static bool _resolved;

	private static FieldInfo _listField;

	private static List<QueuedIncident> GetList(IncidentQueue q)
	{
		if (!_resolved)
		{
			_resolved = true;
			try
			{
				_listField = AccessTools.Field(typeof(IncidentQueue), "queuedIncidents");
			}
			catch
			{
			}
		}
		if (_listField == null)
		{
			return null;
		}
		return _listField.GetValue(q) as List<QueuedIncident>;
	}

	[HarmonyPrefix]
	public static bool Prefix(IncidentQueue __instance)
	{
		Map map;
		bool flag;
		List<QueuedIncident> list;
		try
		{
			if (!MpHelper.IsMultiplayer())
			{
				return true;
			}
			map = MpContext.TickingMap();
			flag = map == null && MpContext.TickingWorld();
			if (map == null && !flag)
			{
				return true;
			}
			list = GetList(__instance);
			if (list == null || list.Count == 0)
			{
				return list == null;
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("IncidentQueueTimeBaseFix.resolve", ex);
			return true;
		}
		try
		{
			int ticksGame = Find.TickManager.TicksGame;
			for (int num = list.Count - 1; num >= 0; num--)
			{
				if (num < list.Count)
				{
					QueuedIncident queuedIncident = list[num];
					if (queuedIncident == null)
					{
						list.RemoveAt(num);
					}
					else
					{
						FiringIncident firingIncident = queuedIncident.FiringIncident;
						Map map2 = ((firingIncident != null && firingIncident.parms != null) ? (firingIncident.parms.target as Map) : null);
						if ((map2 != null) ? (map2 == map) : flag)
						{
							if (!queuedIncident.TriedToFire)
							{
								if (queuedIncident.FireTick <= ticksGame)
								{
									bool flag2 = Find.Storyteller.TryFire(firingIncident, queued: true);
									queuedIncident.Notify_TriedToFire();
									if (flag2 || queuedIncident.RetryDurationTicks == 0)
									{
										list.Remove(queuedIncident);
									}
								}
							}
							else if (queuedIncident.FireTick + queuedIncident.RetryDurationTicks <= ticksGame)
							{
								list.Remove(queuedIncident);
							}
							else if (ticksGame % 833 == Rand.RangeSeeded(0, 833, queuedIncident.FireTick))
							{
								bool flag3 = Find.Storyteller.TryFire(firingIncident, queued: true);
								queuedIncident.Notify_TriedToFire();
								if (flag3)
								{
									list.Remove(queuedIncident);
								}
							}
						}
					}
				}
			}
		}
		catch (Exception ex2)
		{
			CaravanLeaveHelper.LogErrorThrottled("IncidentQueueTimeBaseFix", ex2);
		}
		return false;
	}
}
