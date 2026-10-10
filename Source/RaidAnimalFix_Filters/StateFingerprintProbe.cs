using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RaidAnimalFix;

[HarmonyPatch("MapPostTick")]
[HarmonyPatch(typeof(Map))]
public static class StateFingerprintProbe
{
	private const int IntervalTicks = 60;

	private const int FlushEvery = 25;

	private const int MaxLines = 400000;

	private const int DetailKeep = 40;

	private static readonly List<string> _buf = new List<string>(33);

	private static readonly Dictionary<int, Queue<string>> _detail = new Dictionary<int, Queue<string>>();

	private static string _path;

	private static bool _pathReady;

	private static bool _disabled;

	private static int _written;

	private static FieldInfo _autoSlaughterCache;

	private static bool _autoSlaughterResolved;

	private static bool _rateResolved;

	private static bool _rateWarned;

	private static FieldInfo _forceNormalUntil;

	private static FieldInfo _nothingHappening;

	private static MethodInfo _asyncTimeOf;

	private static MethodInfo _desiredSpeedGet;

	private static MethodInfo _globalTimerGet;

	private static FieldInfo _tickingMap;

	private static bool _dumpFieldsResolved;

	private static FieldInfo _staleField;

	private static FieldInfo _joyLastGain;

	private static void ResolveRateFields()
	{
		if (_rateResolved)
		{
			return;
		}
		_rateResolved = true;
		try
		{
			_forceNormalUntil = AccessTools.Field(typeof(TimeSlower), "forceNormalSpeedUntil");
			Type type = AccessTools.TypeByName("Multiplayer.Client.TickPatch");
			if (type != null)
			{
				PropertyInfo property = type.GetProperty("Timer", BindingFlags.Static | BindingFlags.Public);
				if (property != null)
				{
					_globalTimerGet = property.GetGetMethod();
				}
			}
			Type type2 = AccessTools.TypeByName("Multiplayer.Client.Extensions");
			if (type2 != null)
			{
				_asyncTimeOf = AccessTools.Method(type2, "AsyncTime", new Type[1] { typeof(Map) });
			}
			Type type3 = AccessTools.TypeByName("Multiplayer.Client.AsyncTimeComp");
			if (type3 != null)
			{
				_nothingHappening = type3.GetField("nothingHappeningCached", BindingFlags.Instance | BindingFlags.NonPublic);
				_tickingMap = AccessTools.Field(type3, "tickingMap");
				PropertyInfo property2 = type3.GetProperty("DesiredTimeSpeed", BindingFlags.Instance | BindingFlags.Public);
				if (property2 != null)
				{
					_desiredSpeedGet = property2.GetGetMethod();
				}
			}
		}
		catch
		{
		}
	}

	private static string Path_()
	{
		if (!_pathReady)
		{
			_pathReady = true;
			try
			{
				_path = Path.Combine(GenFilePaths.SaveDataFolderPath, "RaidAnimalFix_fingerprint.txt");
				string text = "?";
				try
				{
					Type multiplayerType = MpHelper.MultiplayerType;
					FieldInfo fieldInfo = ((multiplayerType == null) ? null : multiplayerType.GetField("username", BindingFlags.Static | BindingFlags.Public));
					if (fieldInfo != null)
					{
						text = (fieldInfo.GetValue(null) as string) ?? "?";
					}
				}
				catch
				{
				}
				File.WriteAllText(_path, string.Concat("# RaidAnimalFix state fingerprint. player=", text, " version=", Assembly.GetExecutingAssembly().GetName().Version, " started=", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Environment.NewLine, "# m=карта src=m|w(тик карты|тик мира) g=общий_тик t=часы_источника fac=фракция", Environment.NewLine, "# pawns=пешек ph=хеш_позиций jh=хеш_работ des=меток as=автозабой th=вещей", Environment.NewLine, "# pg=хеш_прогресса_работ (startTick+ticksLeftThisToil+toil) mv=хеш_движения (nextCell+биты float costLeft/costTotal+узлов)", Environment.NewLine, "# spd=скорость fns=forceNormalSpeedUntil nh=nothingHappening — эти три задают ЧАСТОТУ тиков карты", Environment.NewLine));
				Log.Message("[RaidAnimalFix] State fingerprint log: " + _path);
			}
			catch (Exception ex)
			{
				_disabled = true;
				Log.Warning("[RaidAnimalFix] fingerprint disabled: " + ex.Message);
			}
		}
		return _path;
	}

	private static int AutoSlaughterCount(Map map)
	{
		if (!_autoSlaughterResolved)
		{
			_autoSlaughterResolved = true;
			try
			{
				_autoSlaughterCache = AccessTools.Field(typeof(AutoSlaughterManager), "animalsToSlaughterCached");
			}
			catch
			{
			}
		}
		if (_autoSlaughterCache == null || map.autoSlaughterManager == null)
		{
			return -1;
		}
		try
		{
			return (!(_autoSlaughterCache.GetValue(map.autoSlaughterManager) is List<Pawn> list)) ? (-1) : list.Count;
		}
		catch
		{
			return -1;
		}
	}

	[HarmonyPostfix]
	public static void Postfix(Map __instance)
	{
		if (_disabled)
		{
			return;
		}
		try
		{
			if (__instance == null || !MpHelper.IsMultiplayer())
			{
				return;
			}
			ResolveRateFields();
			char c = 'w';
			try
			{
				if (_tickingMap != null && object.ReferenceEquals(_tickingMap.GetValue(null), __instance))
				{
					c = 'm';
				}
			}
			catch
			{
			}
			int ticksGame = Find.TickManager.TicksGame;
			if (ticksGame % 60 != 0)
			{
				return;
			}
			int num = 0;
			int num2 = 0;
			int num3 = 0;
			int num4 = 0;
			int num5 = 0;
			StringBuilder stringBuilder = ((c == 'm') ? new StringBuilder(8192) : null);
			IReadOnlyList<Pawn> readOnlyList = ((__instance.mapPawns == null) ? null : __instance.mapPawns.AllPawnsSpawned);
			if (readOnlyList != null)
			{
				num = readOnlyList.Count;
				for (int i = 0; i < readOnlyList.Count; i++)
				{
					Pawn pawn = readOnlyList[i];
					if (pawn != null)
					{
						IntVec3 position = pawn.Position;
						num2 = Gen.HashCombineInt(num2, pawn.thingIDNumber, position.x, position.z);
						Job job = ((pawn.jobs == null) ? null : pawn.jobs.curJob);
						int num6 = ((job != null && job.def != null) ? job.def.shortHash : 0);
						num3 = Gen.HashCombineInt(Gen.HashCombineInt(num3, pawn.thingIDNumber), num6);
						ReadProgress(pawn, out var st, out var tl, out var ti, out var cl, out var ct, out var nl, out var nx, out var nz);
						num4 = Gen.HashCombineInt(Gen.HashCombineInt(num4, pawn.thingIDNumber), Gen.HashCombineInt(st, tl, ti, num6));
						num5 = Gen.HashCombineInt(Gen.HashCombineInt(num5, pawn.thingIDNumber), Gen.HashCombineInt(cl, ct, nl, nx));
						num5 = Gen.HashCombineInt(num5, nz);
						if (stringBuilder != null)
						{
							AppendPawn(stringBuilder, pawn);
						}
					}
				}
			}
			int num7 = ((__instance.designationManager == null) ? (-1) : __instance.designationManager.AllDesignations.Count);
			int num8 = ((__instance.listerThings == null) ? (-1) : __instance.listerThings.AllThings.Count);
			int num9 = -1;
			int num10 = -1;
			int num11 = -1;
			int num12 = -1;
			try
			{
				if (_globalTimerGet != null)
				{
					num12 = Convert.ToInt32(_globalTimerGet.Invoke(null, null));
				}
				TickManager tickManager = Find.TickManager;
				if (_forceNormalUntil != null && tickManager != null && tickManager.slower != null)
				{
					num9 = (int)_forceNormalUntil.GetValue(tickManager.slower);
				}
				if (_asyncTimeOf != null)
				{
					object obj2 = _asyncTimeOf.Invoke(null, new object[1] { __instance });
					if (obj2 != null)
					{
						if (_nothingHappening != null)
						{
							num10 = (((bool)_nothingHappening.GetValue(obj2)) ? 1 : 0);
						}
						if (_desiredSpeedGet != null)
						{
							num11 = Convert.ToInt32(_desiredSpeedGet.Invoke(obj2, null));
						}
					}
				}
			}
			catch (Exception ex)
			{
				if (!_rateWarned)
				{
					_rateWarned = true;
					Log.Warning("[RaidAnimalFix] fingerprint: не удалось прочитать поля темпа тиков: " + ex.Message);
				}
			}
			int num13 = Faction.OfPlayer?.loadID ?? (-1);
			_buf.Add("m=" + __instance.uniqueID + " src=" + c + " g=" + num12 + " t=" + ticksGame + " fac=" + num13 + " pawns=" + num + " ph=" + num2 + " jh=" + num3 + " pg=" + num4 + " mv=" + num5 + " des=" + num7 + " as=" + AutoSlaughterCount(__instance) + " th=" + num8 + " spd=" + num11 + " fns=" + num9 + " nh=" + num10);
			if (stringBuilder != null)
			{
				if (!_detail.TryGetValue(__instance.uniqueID, out var value))
				{
					value = new Queue<string>(41);
					_detail[__instance.uniqueID] = value;
				}
				value.Enqueue("m=" + __instance.uniqueID + " g=" + num12 + " t=" + ticksGame + " | " + stringBuilder.ToString());
				while (value.Count > 40)
				{
					value.Dequeue();
				}
			}
			if (_buf.Count >= 25)
			{
				Flush();
			}
		}
		catch (Exception ex2)
		{
			_disabled = true;
			Log.Warning("[RaidAnimalFix] fingerprint disabled after error: " + ex2.Message);
		}
	}

	internal static void FlushNow()
	{
		if (_buf.Count > 0)
		{
			Flush();
		}
		DumpDetail();
	}

	private static void DumpDetail()
	{
		try
		{
			string text = Path_();
			if (text == null)
			{
				return;
			}
			string text2 = text.Replace("_fingerprint.txt", "_desync_detail.txt");
			List<string> list = new List<string>();
			list.Add("# ---- рассинхрон " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ----");
			list.Add("# формат: вид+ид:x,z,работа,st=тикСтартаРаботы,tl=тиковДоКонцаТойла,ti=индексТойла,nc=следующаяКлетка,cl=остатокСтоимостиШага(float),ct=стоимостьКлетки(float),nl=узловПутиОсталось,joy=Need_Joy.CurLevel(float|-),tt=расписание(defName|-),ta=цельA(вид+ид|клетка|-),stale=curPathJobIsStale(0|1|-),jgt=Need_Joy.lastGainTick(если есть радость)");
			foreach (KeyValuePair<int, Queue<string>> item in _detail)
			{
				foreach (string item2 in item.Value)
				{
					list.Add(item2);
				}
			}
			PatherFailLog.Drain(list);
			list.Add("# ---- сейчас (все карты, в том числе на паузе) ----");
			DumpLive(list);
			File.AppendAllLines(text2, list);
			_detail.Clear();
			Log.Message("[RaidAnimalFix] поимённый снимок при рассинхроне записан: " + text2);
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] не удалось записать поимённый снимок: " + ex.Message);
		}
	}

	private static int FloatBits(float f)
	{
		return BitConverter.ToInt32(BitConverter.GetBytes(f), 0);
	}

	private static void ReadProgress(Pawn p, out int st, out int tl, out int ti, out int cl, out int ct, out int nl, out int nx, out int nz)
	{
		st = 0;
		tl = 0;
		ti = 0;
		cl = 0;
		ct = 0;
		nl = 0;
		nx = 0;
		nz = 0;
		try
		{
			Job job = ((p.jobs == null) ? null : p.jobs.curJob);
			if (job != null)
			{
				st = job.startTick;
			}
			JobDriver jobDriver = ((p.jobs == null) ? null : p.jobs.curDriver);
			if (jobDriver != null)
			{
				tl = jobDriver.ticksLeftThisToil;
				ti = jobDriver.CurToilIndex;
			}
			Pawn_PathFollower pather = p.pather;
			if (pather != null)
			{
				cl = FloatBits(pather.nextCellCostLeft);
				ct = FloatBits(pather.nextCellCostTotal);
				nx = pather.nextCell.x;
				nz = pather.nextCell.z;
				nl = ((pather.curPath == null) ? (-1) : pather.curPath.NodesLeftCount);
			}
		}
		catch
		{
		}
	}

	private static void AppendPawn(StringBuilder det, Pawn p)
	{
		ReadProgress(p, out var st, out var tl, out var ti, out var _, out var _, out var nl, out var nx, out var nz);
		Job job = ((p.jobs == null) ? null : p.jobs.curJob);
		IntVec3 position = p.Position;
		string value = ((p.def == null || p.def.defName == null) ? "?" : p.def.defName);
		string value2 = ((job == null || job.def == null || job.def.defName == null) ? "none" : job.def.defName);
		string value3 = "0";
		string value4 = "0";
		Pawn_PathFollower pather = p.pather;
		if (pather != null)
		{
			value3 = pather.nextCellCostLeft.ToString("R", CultureInfo.InvariantCulture);
			value4 = pather.nextCellCostTotal.ToString("R", CultureInfo.InvariantCulture);
		}
		det.Append(value).Append(p.thingIDNumber).Append(':')
			.Append(position.x)
			.Append(',')
			.Append(position.z)
			.Append(',')
			.Append(value2)
			.Append(",st=")
			.Append(st)
			.Append(",tl=")
			.Append(tl)
			.Append(",ti=")
			.Append(ti)
			.Append(",nc=")
			.Append(nx)
			.Append('.')
			.Append(nz)
			.Append(",cl=")
			.Append(value3)
			.Append(",ct=")
			.Append(value4)
			.Append(",nl=")
			.Append(nl);
		AppendDumpExtras(det, p, job, pather);
		det.Append(' ');
	}

	private static void ResolveDumpFields()
	{
		if (_dumpFieldsResolved)
		{
			return;
		}
		_dumpFieldsResolved = true;
		try
		{
			_staleField = AccessTools.Field(typeof(Pawn_PathFollower), "curPathJobIsStale");
			_joyLastGain = AccessTools.Field(typeof(Need_Joy), "lastGainTick");
		}
		catch
		{
		}
	}

	internal static string FormatTargetA(Job j)
	{
		if (j == null)
		{
			return "-";
		}
		try
		{
			LocalTargetInfo targetA = j.targetA;
			if (targetA.HasThing)
			{
				Thing thing = targetA.Thing;
				if (thing == null)
				{
					return "-";
				}
				string text = ((thing.def == null || thing.def.defName == null) ? "?" : thing.def.defName);
				return text + thing.thingIDNumber;
			}
			if (targetA.Cell.IsValid)
			{
				int x = targetA.Cell.x;
				string text2 = x.ToString();
				int z = targetA.Cell.z;
				return text2 + "." + z;
			}
		}
		catch
		{
		}
		return "-";
	}

	internal static string FormatStale(Pawn_PathFollower pf)
	{
		ResolveDumpFields();
		if (pf == null || _staleField == null)
		{
			return "-";
		}
		try
		{
			return ((bool)_staleField.GetValue(pf)) ? "1" : "0";
		}
		catch
		{
			return "-";
		}
	}

	private static void AppendDumpExtras(StringBuilder det, Pawn p, Job j, Pawn_PathFollower pf)
	{
		string value = "-";
		Need_Joy need_Joy = null;
		try
		{
			if (p.needs != null)
			{
				need_Joy = p.needs.joy;
			}
			if (need_Joy != null)
			{
				value = need_Joy.CurLevel.ToString("R", CultureInfo.InvariantCulture);
			}
		}
		catch
		{
		}
		string value2 = "-";
		try
		{
			if (p.IsColonist && p.timetable != null)
			{
				TimeAssignmentDef currentAssignment = p.timetable.CurrentAssignment;
				if (currentAssignment != null && currentAssignment.defName != null)
				{
					value2 = currentAssignment.defName;
				}
			}
		}
		catch
		{
		}
		det.Append(",joy=").Append(value).Append(",tt=")
			.Append(value2)
			.Append(",ta=")
			.Append(FormatTargetA(j))
			.Append(",stale=")
			.Append(FormatStale(pf));
		if (need_Joy == null)
		{
			return;
		}
		ResolveDumpFields();
		if (_joyLastGain == null)
		{
			return;
		}
		try
		{
			det.Append(",jgt=").Append(Convert.ToInt32(_joyLastGain.GetValue(need_Joy)));
		}
		catch
		{
		}
	}

	private static void DumpLive(List<string> outp)
	{
		if (Current.Game == null || Find.Maps == null)
		{
			return;
		}
		int num = -1;
		try
		{
			Type type = AccessTools.TypeByName("Multiplayer.Client.TickPatch");
			PropertyInfo propertyInfo = ((type == null) ? null : type.GetProperty("Timer", BindingFlags.Static | BindingFlags.Public));
			if (propertyInfo != null)
			{
				num = Convert.ToInt32(propertyInfo.GetValue(null, null));
			}
		}
		catch
		{
		}
		if (num < 0 && Find.TickManager != null)
		{
			num = Find.TickManager.TicksGame;
		}
		for (int i = 0; i < Find.Maps.Count; i++)
		{
			Map map = Find.Maps[i];
			if (map == null || map.mapPawns == null)
			{
				continue;
			}
			StringBuilder stringBuilder = new StringBuilder(8192);
			IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
			if (allPawnsSpawned != null)
			{
				for (int j = 0; j < allPawnsSpawned.Count; j++)
				{
					Pawn pawn = allPawnsSpawned[j];
					if (pawn != null)
					{
						AppendPawn(stringBuilder, pawn);
					}
				}
			}
			if (!MpMapTime.TryGetMapTicks(map, out var ticks))
			{
				ticks = ((Find.TickManager == null) ? (-1) : Find.TickManager.TicksGame);
			}
			outp.Add("live m=" + map.uniqueID + " g=" + num + " t=" + ticks + " pawns=" + (allPawnsSpawned?.Count ?? 0) + " | " + stringBuilder);
		}
	}

	private static void Flush()
	{
		string text = Path_();
		if (_disabled || text == null)
		{
			_buf.Clear();
			return;
		}
		try
		{
			File.AppendAllLines(text, _buf);
			_written += _buf.Count;
			if (_written >= 400000)
			{
				_disabled = true;
				Log.Message("[RaidAnimalFix] fingerprint: достигнут предел строк, запись остановлена");
			}
		}
		catch (Exception ex)
		{
			_disabled = true;
			Log.Warning("[RaidAnimalFix] fingerprint write failed: " + ex.Message);
		}
		_buf.Clear();
	}
}
