using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace RaidAnimalFix;

internal static class PatherFailLog
{
	private const int Keep = 24;

	private static readonly Queue<string> _ring = new Queue<string>(25);

	private static bool _resolved;

	private static bool _off;

	private static FieldInfo _willCollide;

	private static FieldInfo _lastBlocker;

	private static FieldInfo _noCellTicks;

	private static FieldInfo _collidesPath;

	private static FieldInfo _dangerPath;

	private static FieldInfo _destination;

	private static FieldInfo _pawnField;

	private static void Resolve()
	{
		if (_resolved)
		{
			return;
		}
		_resolved = true;
		try
		{
			Type typeFromHandle = typeof(Pawn_PathFollower);
			_willCollide = AccessTools.Field(typeFromHandle, "cachedWillCollideNextCell");
			_lastBlocker = AccessTools.Field(typeFromHandle, "lastBlocker");
			_noCellTicks = AccessTools.Field(typeFromHandle, "failedToFindCloseUnoccupiedCellTicks");
			_collidesPath = AccessTools.Field(typeFromHandle, "foundPathWhichCollidesWithPawns");
			_dangerPath = AccessTools.Field(typeFromHandle, "foundPathWithDanger");
			_destination = AccessTools.Field(typeFromHandle, "destination");
			_pawnField = AccessTools.Field(typeFromHandle, "pawn");
		}
		catch
		{
		}
	}

	private static int IntOf(FieldInfo f, object inst)
	{
		if (f == null || inst == null)
		{
			return -1;
		}
		try
		{
			return Convert.ToInt32(f.GetValue(inst));
		}
		catch
		{
			return -1;
		}
	}

	private static bool BoolOf(FieldInfo f, object inst)
	{
		if (f == null || inst == null)
		{
			return false;
		}
		try
		{
			return (bool)f.GetValue(inst);
		}
		catch
		{
			return false;
		}
	}

	private static void DescribeCell(Map map, IntVec3 c, out string edifice, out string pawns)
	{
		edifice = "none";
		pawns = "";
		if (map == null || !c.IsValid || !c.InBounds(map))
		{
			return;
		}
		try
		{
			Building edifice2 = c.GetEdifice(map);
			if (edifice2 != null && edifice2.def != null)
			{
				edifice = edifice2.def.defName;
			}
			List<Thing> list = map.thingGrid.ThingsListAtFast(c);
			StringBuilder stringBuilder = null;
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i] is Pawn pawn)
				{
					if (stringBuilder == null)
					{
						stringBuilder = new StringBuilder();
					}
					else
					{
						stringBuilder.Append('/');
					}
					stringBuilder.Append(pawn.thingIDNumber);
				}
			}
			if (stringBuilder != null)
			{
				pawns = stringBuilder.ToString();
			}
		}
		catch
		{
		}
	}

	public static void Note(Pawn_PathFollower pf)
	{
		if (_off)
		{
			return;
		}
		try
		{
			if (pf == null || !MpHelper.IsMultiplayer())
			{
				return;
			}
			Resolve();
			Pawn pawn = ((_pawnField == null) ? null : (_pawnField.GetValue(pf) as Pawn));
			if (pawn == null)
			{
				return;
			}
			Map map = pawn.Map;
			if (map == null)
			{
				return;
			}
			if (!MpMapTime.TryGetMapTicks(map, out var ticks))
			{
				ticks = ((Find.TickManager == null) ? (-1) : Find.TickManager.TicksGame);
			}
			IntVec3 nextCell = pf.nextCell;
			DescribeCell(map, nextCell, out var edifice, out var pawns);
			Pawn pawn2 = null;
			try
			{
				pawn2 = ((_lastBlocker == null) ? null : (_lastBlocker.GetValue(pf) as Pawn));
			}
			catch
			{
			}
			string text = "?";
			try
			{
				if (_destination != null)
				{
					object value = _destination.GetValue(pf);
					if (value is LocalTargetInfo)
					{
						text = ((LocalTargetInfo)value).Cell.ToString();
					}
				}
			}
			catch
			{
			}
			Job job = ((pawn.jobs == null) ? null : pawn.jobs.curJob);
			string text2 = ((job == null || job.def == null || job.def.defName == null) ? "none" : job.def.defName);
			string text3 = ((pawn.def == null || pawn.def.defName == null) ? "?" : pawn.def.defName);
			string item = "fail m=" + map.uniqueID + " t=" + ticks + " " + text3 + pawn.thingIDNumber + " at=" + pawn.Position.x + "." + pawn.Position.z + " nc=" + nextCell.x + "." + nextCell.z + " job=" + text2 + " cl=" + pf.nextCellCostLeft.ToString("R", CultureInfo.InvariantCulture) + " ct=" + pf.nextCellCostTotal.ToString("R", CultureInfo.InvariantCulture) + " nl=" + ((pf.curPath == null) ? (-1) : pf.curPath.NodesLeftCount) + " path=" + ((pf.curPath == null) ? "null" : "ok") + " dest=" + text + " edifice=" + edifice + " cellPawns=" + ((pawns.Length == 0) ? "-" : pawns) + " collideNext=" + (BoolOf(_willCollide, pf) ? 1 : 0) + " blocker=" + ((pawn2 == null) ? "-" : pawn2.thingIDNumber.ToString()) + " noCellTicks=" + IntOf(_noCellTicks, pf) + " pathCollides=" + IntOf(_collidesPath, pf) + " pathDanger=" + IntOf(_dangerPath, pf) + " ta=" + StateFingerprintProbe.FormatTargetA(job) + " stale=" + StateFingerprintProbe.FormatStale(pf);
			_ring.Enqueue(item);
			while (_ring.Count > 24)
			{
				_ring.Dequeue();
			}
		}
		catch (Exception ex)
		{
			_off = true;
			Log.Warning("[RaidAnimalFix] PatherFailLog выключен после ошибки: " + ex.Message);
		}
	}

	public static void Drain(List<string> outp)
	{
		if (_ring.Count == 0)
		{
			return;
		}
		try
		{
			outp.Add("# ---- срывы пути (последние " + _ring.Count + ") ----");
			foreach (string item in _ring)
			{
				outp.Add(item);
			}
			_ring.Clear();
		}
		catch
		{
		}
	}
}
