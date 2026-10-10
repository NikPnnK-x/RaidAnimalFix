using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;

namespace RaidAnimalFix;

/// <summary>
/// Режим разработчика → Desync Fix → «Дамп Harmony-патчей». Выгружает все
/// пропатченные методы с модами-владельцами патчей в
/// &lt;SaveData&gt;/RaidAnimalFix_HarmonyPatches.txt. Первый раздел — методы
/// симуляции (тики, работы, пути, инциденты, квесты...), на которые вешаются
/// моды кроме самой игры/Multiplayer/этого мода: главные кандидаты в причины
/// рассинхрона. Нужен там, где декомпиляция не показала цели патчей.
/// </summary>
public static class HarmonyPatchDump
{
	private static readonly string[] SimHints = new string[]
	{
		"Tick", "Job", "Pather", "PathFinder", "Incident", "Storyteller", "Quest", "Lord", "Rand", "Spawn", "Destroy", "Kill",
		"TakeDamage", "Designat", "WorkGiver", "JobDriver", "Thinker", "MentalState", "Need", "Hediff", "Pawn_", "Caravan",
		"Faction", "Letter", "Map", "World", "Trade", "Gen", "Verb", "Projectile", "Reachab", "Region", "Room"
	};

	private static readonly HashSet<string> TrustedMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "RimWorld", "Multiplayer", "Harmony" };

	[DebugAction("Desync Fix", "Dump Harmony patches", false, false, false, false, false, 0, false, allowedGameStates = AllowedGameStates.Entry | AllowedGameStates.Playing)]
	public static void Dump()
	{
		string path = Path.Combine(GenFilePaths.SaveDataFolderPath, "RaidAnimalFix_HarmonyPatches.txt");
		try
		{
			File.WriteAllText(path, Build());
			Messages.Message("[Desync Fix] " + path, MessageTypeDefOf.TaskCompletion, historical: false);
			Log.Message("[RaidAnimalFix] Harmony patch dump: " + path);
		}
		catch (Exception ex)
		{
			Log.Error("[RaidAnimalFix] Harmony patch dump failed: " + ex);
		}
	}

	public static string Build()
	{
		List<MethodBase> methods = Harmony.GetAllPatchedMethods().Where((MethodBase m) => m != null).OrderBy((MethodBase m) => m.DeclaringType?.FullName + "." + m.Name).ToList();
		StringBuilder sim = new StringBuilder();
		StringBuilder all = new StringBuilder();
		Dictionary<string, int> perMod = new Dictionary<string, int>();
		foreach (MethodBase method in methods)
		{
			HarmonyLib.Patches patchInfo = Harmony.GetPatchInfo(method);
			if (patchInfo == null)
			{
				continue;
			}
			string name = (method.DeclaringType?.FullName ?? "?") + "." + method.Name + "(" + string.Join(", ", method.GetParameters().Select((ParameterInfo p) => p.ParameterType.Name).ToArray()) + ")";
			StringBuilder entry = new StringBuilder();
			entry.AppendLine(name);
			bool suspicious = false;
			Append(entry, "prefix", patchInfo.Prefixes, perMod, ref suspicious);
			Append(entry, "postfix", patchInfo.Postfixes, perMod, ref suspicious);
			Append(entry, "transpiler", patchInfo.Transpilers, perMod, ref suspicious);
			Append(entry, "finalizer", patchInfo.Finalizers, perMod, ref suspicious);
			all.Append(entry);
			string ns = method.DeclaringType?.Namespace ?? "";
			bool game = ns.StartsWith("Verse") || ns.StartsWith("RimWorld");
			if (suspicious && game && SimHints.Any((string h) => name.IndexOf(h, StringComparison.Ordinal) >= 0))
			{
				sim.Append(entry);
			}
		}
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("# Desync Fix — Harmony patch dump, " + methods.Count + " patched methods");
		sb.AppendLine("# Patches per mod:");
		foreach (KeyValuePair<string, int> item in perMod.OrderByDescending((KeyValuePair<string, int> k) => k.Value))
		{
			sb.Append("#   ").Append(item.Value).Append("  ").AppendLine(item.Key);
		}
		sb.AppendLine();
		sb.AppendLine("## Simulation methods patched by mods other than RimWorld / Multiplayer / Desync Fix");
		sb.AppendLine(sim.ToString());
		sb.AppendLine("## All patched methods");
		sb.Append(all);
		return sb.ToString();
	}

	private static void Append(StringBuilder sb, string kind, IEnumerable<Patch> patches, Dictionary<string, int> perMod, ref bool suspicious)
	{
		foreach (Patch item in patches.OrderByDescending((Patch p) => p.priority))
		{
			string mod = DesyncAnalyzer.ModOfPatchPublic(item);
			perMod.TryGetValue(mod, out var value);
			perMod[mod] = value + 1;
			if (!TrustedMods.Contains(mod) && mod.IndexOf("customfix.raidanimalfix", StringComparison.OrdinalIgnoreCase) < 0 && mod.IndexOf("Desync Fix", StringComparison.OrdinalIgnoreCase) < 0)
			{
				suspicious = true;
			}
			sb.Append("    ").Append(kind).Append(" [").Append(mod).Append("] ").Append(item.PatchMethod?.DeclaringType?.FullName).Append('.').Append(item.PatchMethod?.Name).Append("  (owner ").Append(item.owner).AppendLine(")");
		}
	}
}
