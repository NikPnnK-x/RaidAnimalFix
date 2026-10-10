using System;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace RaidAnimalFix;

[HarmonyPatch(typeof(Map))]
[HarmonyPatch("MapPostTick")]
public static class SiteRaidCountdownMapDriver
{
	[HarmonyPostfix]
	public static void Postfix(Map __instance)
	{
		try
		{
			// Только собственный тик карты. Мировой проход тоже зовёт MapPostTick
			// (оригинал отменён MP, но постфиксы бегут) — там не тикаем.
			if (__instance == null || !MpHelper.IsMultiplayer() || MpContext.TickingMap() != __instance)
			{
				return;
			}
			MapParent parent = __instance.Parent;
			if (parent == null)
			{
				return;
			}
			TimedDetectionRaids component = parent.GetComponent<TimedDetectionRaids>();
			if (component == null)
			{
				return;
			}
			SiteRaidCountdownMapTimeFix.drivenByMapTick = true;
			try
			{
				component.CompTickInterval(1);
			}
			finally
			{
				SiteRaidCountdownMapTimeFix.drivenByMapTick = false;
			}
		}
		catch (Exception ex)
		{
			SiteRaidCountdownMapTimeFix.drivenByMapTick = false;
			CaravanLeaveHelper.LogErrorThrottled("SiteRaidCountdownMapDriver", ex);
		}
	}
}
