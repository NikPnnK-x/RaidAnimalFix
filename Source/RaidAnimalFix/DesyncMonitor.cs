using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RaidAnimalFix;

public class DesyncMonitor : GameComponent
{
	private const float RejoinDelaySeconds = 2f;

	private const float MaxWaitForReportSeconds = 30f;

	private const int MaxRetries = 5;

	private const float RetryWindowSeconds = 600f;

	private static MethodInfo _doRejoinMethod;

	private static bool _rejoinerResolved;

	private bool _pending;

	private float _rejoinAt;

	private float _armedAt;

	private int _retryCount;

	private float _lastDesyncTime = -99999f;

	private float _nextPoll;

	private static FieldInfo _infoWrittenField;

	public DesyncMonitor(Game game)
	{
	}

	// Разбор отчёта: ставится при появлении окна рассинхрона, выполняется
	// как только MP допишет zip (до автоперезахода).
	private static bool _analyzePending;

	private static float _analyzeArmedAt;

	private static DateTime _analyzeSinceUtc;

	public static void NoteDesyncForAnalysis()
	{
		if (!_analyzePending)
		{
			_analyzePending = true;
			_analyzeArmedAt = Time.realtimeSinceStartup;
			_analyzeSinceUtc = DateTime.UtcNow.AddSeconds(-10.0);
		}
	}

	private static void TickAnalysis(float now)
	{
		if (_analyzePending)
		{
			bool written = DesyncReportWritten();
			if (written || now > _analyzeArmedAt + 30f)
			{
				string text = DesyncAnalyzer.NewestReport(_analyzeSinceUtc);
				if (text != null || now > _analyzeArmedAt + 30f)
				{
					_analyzePending = false;
					DesyncAnalyzer.AnalyzeAndReport(text);
				}
			}
		}
		string pendingSummary = DesyncAnalyzer.pendingSummary;
		if (pendingSummary != null && Find.WindowStack != null && !LongEventHandler.AnyEventNowOrWaiting && Current.ProgramState == ProgramState.Playing)
		{
			DesyncAnalyzer.pendingSummary = null;
			int num = pendingSummary.IndexOf("\n\n", StringComparison.Ordinal);
			string text2 = (num > 0) ? pendingSummary.Substring(0, num) : pendingSummary;
			Find.WindowStack.Add(new Dialog_MessageBox("[Desync Fix] " + "RAF_DesyncAnalysisHeader".Translate().Resolve() + "\n\n" + text2 + "\n\n<SaveData>/MpDesyncs/*.RaidAnimalFix.txt"));
		}
	}

	public override void GameComponentUpdate()
	{
		try
		{
			float realtimeSinceStartup = Time.realtimeSinceStartup;
			TickAnalysis(realtimeSinceStartup);
			if (!_pending && realtimeSinceStartup >= _nextPoll)
			{
				_nextPoll = realtimeSinceStartup + 0.5f;
				if (MpHelper.IsMultiplayer() && MpHelper.IsSessionDesynced())
				{
					Arm("session.desynced == true");
				}
			}
			if (_pending && realtimeSinceStartup >= _rejoinAt && (DesyncReportWritten() || !(realtimeSinceStartup < _armedAt + 30f)))
			{
				_pending = false;
				DoRejoin();
			}
		}
		catch
		{
		}
	}

	private static bool DesyncReportWritten()
	{
		try
		{
			WindowStack windowStack = Find.WindowStack;
			if (windowStack == null)
			{
				return true;
			}
			IList<Window> windows = windowStack.Windows;
			for (int i = 0; i < windows.Count; i++)
			{
				Window window = windows[i];
				if (window != null && !(window.GetType().Name != "DesyncedWindow"))
				{
					if (_infoWrittenField == null)
					{
						_infoWrittenField = window.GetType().GetField("infoWritten", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					}
					if (_infoWrittenField == null)
					{
						return true;
					}
					return (bool)_infoWrittenField.GetValue(window);
				}
			}
		}
		catch
		{
		}
		return true;
	}

	public void Arm(string reason)
	{
		// Хост — источник истины: при рассинхроне перезаходят клиенты.
		// Автоперезапуск хоста ставит на паузу и выкидывает всех.
		if (MpHelper.IsHost())
		{
			if (!_pending && Time.realtimeSinceStartup - _lastDesyncTime > 5f)
			{
				_lastDesyncTime = Time.realtimeSinceStartup;
				Log.Warning("[RaidAnimalFix] DESYNC detected on host (" + reason + "). Host does not auto-rejoin; clients will. Report: <SaveData>\\MpDesyncs\\Desync-N.zip");
			}
			return;
		}
		if (!_pending)
		{
			float realtimeSinceStartup = Time.realtimeSinceStartup;
			if (realtimeSinceStartup - _lastDesyncTime > 600f)
			{
				_retryCount = 0;
			}
			_lastDesyncTime = realtimeSinceStartup;
			if (_retryCount >= 5)
			{
				Log.Warning("[RaidAnimalFix] Desync again (" + reason + "), but auto-resync limit reached (" + 5 + " in 10 min). Use the Multiplayer desync window manually.");
				return;
			}
			_retryCount++;
			_pending = true;
			_armedAt = realtimeSinceStartup;
			_rejoinAt = realtimeSinceStartup + 2f;
			Log.Warning("[RaidAnimalFix] DESYNC detected (" + reason + "). Auto-resync after the desync report is written (~" + 2f + "s, attempt " + _retryCount + "/" + 5 + "). Report: <SaveData>\\MpDesyncs\\Desync-N.zip");
		}
	}

	public bool AutoResyncAvailable()
	{
		float realtimeSinceStartup = Time.realtimeSinceStartup;
		if (realtimeSinceStartup - _lastDesyncTime > 600f)
		{
			return true;
		}
		return _retryCount < 5;
	}

	private void DoRejoin()
	{
		try
		{
			ResolveRejoiner();
			if (_doRejoinMethod == null)
			{
				Log.Warning("[RaidAnimalFix] Rejoiner.DoRejoin not found — cannot auto-resync");
			}
			else if (MpHelper.IsMultiplayer())
			{
				Log.Message("[RaidAnimalFix] Auto-resync: requesting rejoin...");
				_doRejoinMethod.Invoke(null, null);
			}
		}
		catch (Exception ex)
		{
			Log.Error("[RaidAnimalFix] Auto-resync failed: " + ex);
		}
	}

	private static void ResolveRejoiner()
	{
		if (_rejoinerResolved)
		{
			return;
		}
		_rejoinerResolved = true;
		try
		{
			Type type = AccessTools.TypeByName("Multiplayer.Client.Rejoiner");
			if (type == null)
			{
				Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
				foreach (Assembly assembly in assemblies)
				{
					if (assembly.GetName().Name != "Multiplayer")
					{
						continue;
					}
					Type[] types = assembly.GetTypes();
					foreach (Type type2 in types)
					{
						if (type2.Name == "Rejoiner")
						{
							type = type2;
							break;
						}
					}
				}
			}
			if (type != null)
			{
				_doRejoinMethod = type.GetMethod("DoRejoin", BindingFlags.Static | BindingFlags.Public);
			}
		}
		catch
		{
		}
	}
}
