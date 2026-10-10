using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using Verse;

namespace RaidAnimalFix;

/// <summary>
/// Разбор отчёта Multiplayer о рассинхроне (MpDesyncs/Desync-NN.zip):
/// берём след первого расхождения у себя (local_traces.txt) и у хоста
/// (host_traces.txt), для каждого кадра находим метод, его мод и все
/// Harmony-патчи на нём. Итог — список «подозреваемых» модов: окно игроку,
/// строка в лог и файл Desync-NN.RaidAnimalFix.txt рядом с отчётом.
/// </summary>
public static class DesyncAnalyzer
{
	private sealed class Suspect
	{
		public string mod;

		public int ownFrames;

		public int patchedFrames;

		public bool onlyOneSide;

		public readonly List<string> methods = new List<string>();

		public int Score => ownFrames * 3 + patchedFrames * 2 + (onlyOneSide ? 4 : 0);
	}

	// "  at Verse.Pawn_JobTracker.StartJob (Verse.AI.Job,...) [0x00012] in <...>:0"
	// "  at (wrapper dynamic-method) Verse.Pawn.Verse.Pawn.Tick_Patch1 (Verse.Pawn) [0x...]"
	private static readonly Regex FrameRx = new Regex("at (?:\\(wrapper [^)]*\\) )?([A-Za-z0-9_.`+<>|/]+?)(?:_Patch\\d+)? ?\\(", RegexOptions.Compiled);

	private static readonly HashSet<string> NoiseTypes = new HashSet<string> { "Verse.Rand", "System.Object", "Multiplayer.Client.DeferredStackTracing", "Multiplayer.Client.Desyncs.DeferredStackTracing" };

	private static Dictionary<Assembly, string> _asmToMod;

	private static Dictionary<string, string> _harmonyIdToMod;

	public static string pendingSummary;

	private static string _lastAnalyzed;

	public static string DesyncsDir => Path.Combine(GenFilePaths.SaveDataFolderPath, "MpDesyncs");

	/// <summary>Самый свежий отчёт, записанный не раньше notBeforeUtc.</summary>
	public static string NewestReport(DateTime notBeforeUtc)
	{
		try
		{
			DirectoryInfo directoryInfo = new DirectoryInfo(DesyncsDir);
			if (!directoryInfo.Exists)
			{
				return null;
			}
			FileInfo fileInfo = directoryInfo.GetFiles("Desync-*.zip").Where((FileInfo f) => f.LastWriteTimeUtc >= notBeforeUtc).OrderByDescending((FileInfo f) => f.LastWriteTimeUtc).FirstOrDefault();
			return fileInfo?.FullName;
		}
		catch
		{
			return null;
		}
	}

	public static string AnalyzeAndReport(string zipPath)
	{
		if (zipPath == null || zipPath == _lastAnalyzed)
		{
			return null;
		}
		_lastAnalyzed = zipPath;
		string text;
		try
		{
			text = Analyze(zipPath);
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] DesyncAnalyzer failed: " + ex.Message);
			return null;
		}
		try
		{
			File.WriteAllText(Path.ChangeExtension(zipPath, null) + ".RaidAnimalFix.txt", text);
		}
		catch
		{
		}
		Log.Warning("[RaidAnimalFix] Desync analysis (" + Path.GetFileName(zipPath) + "):\n" + text);
		pendingSummary = text;
		return text;
	}

	public static string Analyze(string zipPath)
	{
		string local = null;
		string host = null;
		using (ZipArchive zipArchive = ZipFile.OpenRead(zipPath))
		{
			local = ReadEntry(zipArchive, "local_traces.txt");
			host = ReadEntry(zipArchive, "host_traces.txt");
		}
		List<string> localFrames = FirstDesyncFrames(local);
		List<string> hostFrames = FirstDesyncFrames(host);
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("Report: " + Path.GetFileName(zipPath));
		if (localFrames.Count == 0 && hostFrames.Count == 0)
		{
			stringBuilder.AppendLine("No traces in report (Multiplayer: Settings → desync traces; traces are recorded only for Rand calls).");
			return stringBuilder.ToString();
		}
		HashSet<string> localSet = new HashSet<string>(localFrames);
		HashSet<string> hostSet = new HashSet<string>(hostFrames);
		Dictionary<string, Suspect> suspects = new Dictionary<string, Suspect>();
		foreach (string item in localFrames.Concat(hostFrames).Distinct())
		{
			bool oneSide = hostFrames.Count > 0 && localFrames.Count > 0 && (localSet.Contains(item) != hostSet.Contains(item));
			Attribute(item, oneSide, suspects);
		}
		List<Suspect> list = suspects.Values.Where((Suspect s) => s.mod != "RimWorld" && s.mod != "Multiplayer" && s.mod != "Harmony").OrderByDescending((Suspect s) => s.Score).ToList();
		stringBuilder.AppendLine((list.Count == 0) ? "No mod code or patches on the desynced trace — likely vanilla/Multiplayer itself." : "Probable culprits (most likely first):");
		for (int i = 0; i < list.Count && i < 6; i++)
		{
			Suspect suspect = list[i];
			stringBuilder.Append("  ").Append(i + 1).Append(". ").Append(suspect.mod).Append("  [own frames: ").Append(suspect.ownFrames)
				.Append(", patches on trace: ")
				.Append(suspect.patchedFrames)
				.Append(suspect.onlyOneSide ? ", ONLY ON ONE SIDE" : "")
				.AppendLine("]");
			foreach (string item2 in suspect.methods.Distinct().Take(4))
			{
				stringBuilder.Append("       ").AppendLine(item2);
			}
		}
		stringBuilder.AppendLine();
		AppendTrace(stringBuilder, "Local first desynced trace", localFrames, hostSet, hostFrames.Count > 0);
		AppendTrace(stringBuilder, "Host first desynced trace", hostFrames, localSet, localFrames.Count > 0);
		return stringBuilder.ToString();
	}

	private static void AppendTrace(StringBuilder sb, string title, List<string> frames, HashSet<string> other, bool mark)
	{
		if (frames.Count == 0)
		{
			return;
		}
		sb.AppendLine(title + ":");
		foreach (string frame in frames)
		{
			sb.Append((mark && !other.Contains(frame)) ? "  * " : "    ").AppendLine(frame);
		}
	}

	private static string ReadEntry(ZipArchive zip, string name)
	{
		ZipArchiveEntry entry = zip.GetEntry(name);
		if (entry == null)
		{
			return null;
		}
		using StreamReader streamReader = new StreamReader(entry.Open());
		return streamReader.ReadToEnd();
	}

	/// <summary>Кадры (Тип.Метод) из блока «Trace of first desynced map random state».</summary>
	private static List<string> FirstDesyncFrames(string traces)
	{
		List<string> list = new List<string>();
		if (string.IsNullOrEmpty(traces))
		{
			return list;
		}
		int num = traces.IndexOf("Trace of first desynced", StringComparison.Ordinal);
		if (num < 0)
		{
			return list;
		}
		int num2 = traces.IndexOf("Context traces:", num, StringComparison.Ordinal);
		string text = (num2 > 0) ? traces.Substring(num, num2 - num) : traces.Substring(num);
		foreach (Match item in FrameRx.Matches(text))
		{
			string text2 = Normalize(item.Groups[1].Value);
			if (text2 != null && (list.Count == 0 || list[list.Count - 1] != text2))
			{
				list.Add(text2);
			}
		}
		return list;
	}

	// "Verse.Pawn.Verse.Pawn.Tick" (обёртка Harmony) → "Verse.Pawn.Tick"
	private static string Normalize(string full)
	{
		int num = full.LastIndexOf('.');
		if (num <= 0)
		{
			return null;
		}
		string text = full.Substring(0, num);
		string text2 = full.Substring(num + 1);
		int num2 = text.Length / 2;
		if (text.Length % 2 == 1 && text[num2] == '.' && text.Substring(0, num2) == text.Substring(num2 + 1))
		{
			text = text.Substring(0, num2);
		}
		if (NoiseTypes.Contains(text))
		{
			return null;
		}
		return text + "." + text2;
	}

	private static void Attribute(string frame, bool oneSide, Dictionary<string, Suspect> suspects)
	{
		int num = frame.LastIndexOf('.');
		string text = frame.Substring(0, num);
		string name = frame.Substring(num + 1);
		Type type = AccessTools.TypeByName(text.Replace('/', '+'));
		if (type == null)
		{
			return;
		}
		Get(suspects, ModOf(type.Assembly), frame, oneSide).ownFrames++;
		MethodInfo[] methods;
		try
		{
			methods = type.GetMethods(AccessTools.all);
		}
		catch
		{
			return;
		}
		HashSet<string> hashSet = new HashSet<string>();
		foreach (MethodInfo methodInfo in methods)
		{
			if (methodInfo.Name != name)
			{
				continue;
			}
			HarmonyLib.Patches patchInfo = Harmony.GetPatchInfo(methodInfo);
			if (patchInfo == null)
			{
				continue;
			}
			foreach (Patch item in patchInfo.Prefixes.Concat(patchInfo.Postfixes).Concat(patchInfo.Transpilers).Concat(patchInfo.Finalizers))
			{
				string text2 = ModOfPatch(item);
				if (hashSet.Add(text2))
				{
					Get(suspects, text2, frame + "  ← " + item.PatchMethod.DeclaringType?.FullName + "." + item.PatchMethod.Name, oneSide).patchedFrames++;
				}
			}
		}
	}

	private static Suspect Get(Dictionary<string, Suspect> suspects, string mod, string method, bool oneSide)
	{
		if (!suspects.TryGetValue(mod, out var value))
		{
			value = new Suspect
			{
				mod = mod
			};
			suspects[mod] = value;
		}
		value.methods.Add(method);
		value.onlyOneSide |= oneSide;
		return value;
	}

	public static string ModOfPatchPublic(Patch patch)
	{
		return ModOfPatch(patch);
	}

	private static string ModOfPatch(Patch patch)
	{
		Assembly assembly = patch.PatchMethod?.DeclaringType?.Assembly;
		string text = (assembly == null) ? null : ModOf(assembly);
		if (text != null && text != "?")
		{
			return text;
		}
		BuildMaps();
		return _harmonyIdToMod.TryGetValue(patch.owner ?? "", out var value) ? value : (patch.owner ?? "?");
	}

	private static string ModOf(Assembly asm)
	{
		BuildMaps();
		if (_asmToMod.TryGetValue(asm, out var value))
		{
			return value;
		}
		string name = asm.GetName().Name;
		if (name == "Assembly-CSharp" || name.StartsWith("UnityEngine") || name == "mscorlib" || name.StartsWith("System"))
		{
			return "RimWorld";
		}
		if (name == "0Harmony")
		{
			return "Harmony";
		}
		return name;
	}

	private static void BuildMaps()
	{
		if (_asmToMod != null)
		{
			return;
		}
		_asmToMod = new Dictionary<Assembly, string>();
		_harmonyIdToMod = new Dictionary<string, string>();
		foreach (ModContentPack runningMod in LoadedModManager.RunningMods)
		{
			string value = runningMod.Name + " (" + runningMod.PackageIdPlayerFacing + ")";
			if (runningMod.PackageIdPlayerFacing != null && runningMod.PackageIdPlayerFacing.IndexOf("rwmt.multiplayer", StringComparison.OrdinalIgnoreCase) >= 0 && runningMod.PackageIdPlayerFacing.IndexOf("compat", StringComparison.OrdinalIgnoreCase) < 0)
			{
				value = "Multiplayer";
			}
			if (runningMod.IsCoreMod || runningMod.IsOfficialMod)
			{
				value = "RimWorld";
			}
			foreach (Assembly loadedAssembly in runningMod.assemblies.loadedAssemblies)
			{
				_asmToMod[loadedAssembly] = value;
			}
			if (runningMod.PackageId != null)
			{
				_harmonyIdToMod[runningMod.PackageId] = value;
			}
		}
	}
}
