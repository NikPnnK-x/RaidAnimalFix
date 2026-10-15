using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using Steamworks;
using Verse;
using Verse.Steam;
using Version = System.Version;

namespace RaidAnimalFix;

// Публикация ЭТОГО мода в Steam Workshop кнопкой из игры.
// Загрузчик RimWorld ставит описание только при первой публикации (при обновлениях —
// нет), а заметку к обновлению пишет свою «[Auto-generated text]: Update on …». Здесь,
// только когда загружается мод с packageId customfix.raidanimalfix (у игроков не
// срабатывает никогда):
//  • описание — из About/WorkshopDescription.txt;
//  • заметка к обновлению — записи About/WorkshopChangeNotes.txt, ещё не опубликованные;
//  • после успешной загрузки — описания на других языках (WorkshopDescription.<яз>.txt),
//    только изменившиеся с прошлой публикации (каждый язык — отдельное обновление Steam).
// Что опубликовано, помнит файл RaidAnimalFix_Workshop.txt в папке настроек игры.

internal static class RafWorkshop
{
	private const string PackageId = "customfix.raidanimalfix";

	private const int DescriptionMax = 8000;

	private const int ChangeNoteMax = 7900;

	private static readonly string[][] Languages = new string[9][]
	{
		new string[2] { "ru", "russian" },
		new string[2] { "de", "german" },
		new string[2] { "es", "spanish" },
		new string[2] { "fr", "french" },
		new string[2] { "pl", "polish" },
		new string[2] { "pt", "brazilian" },
		new string[2] { "ja", "japanese" },
		new string[2] { "ko", "koreana" },
		new string[2] { "zh", "schinese" }
	};

	internal static WorkshopItemHook Hook;

	internal static UGCUpdateHandle_t Handle = UGCUpdateHandle_t.Invalid;

	private static string _publishingVersion;

	private static readonly Queue<string[]> LocalizedQueue = new Queue<string[]>();

	private static CallResult<SubmitItemUpdateResult_t> _localizedResult;

	private static string[] _localizedCurrent;

	private static string RecordPath => Path.Combine(GenFilePaths.ConfigFolderPath, "RaidAnimalFix_Workshop.txt");

	public static bool IsOurs(WorkshopItemHook hook)
	{
		try
		{
			string about = Path.Combine(hook.Directory.FullName, "About", "About.xml");
			return File.Exists(about) && Regex.IsMatch(File.ReadAllText(about), "<packageId>\\s*" + Regex.Escape(PackageId) + "\\s*</packageId>", RegexOptions.IgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	private static string ReadAbout(string file)
	{
		try
		{
			string path = Path.Combine(Hook.Directory.FullName, "About", file);
			if (!File.Exists(path))
			{
				return null;
			}
			string text = File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n").Trim();
			return (text.Length == 0) ? null : text;
		}
		catch
		{
			return null;
		}
	}

	private static string Cap(string text, int max)
	{
		return (text.Length <= max) ? text : text.Substring(0, max);
	}

	// ---------- описание ----------

	public static void OnItemData(UGCUpdateHandle_t handle, WorkshopItemHook hook)
	{
		if (!IsOurs(hook))
		{
			return;
		}
		Hook = hook;
		Handle = handle;
		string description = ReadAbout("WorkshopDescription.txt");
		if (description != null)
		{
			SteamUGC.SetItemDescription(handle, Cap(description, DescriptionMax));
			Log.Message("[RaidAnimalFix] Workshop: description from About/WorkshopDescription.txt (" + description.Length + " chars)");
		}
	}

	// ---------- заметка к обновлению ----------

	/// <summary>Замена вызова SteamUGC.SubmitItemUpdate в загрузчике RimWorld.</summary>
	public static SteamAPICall_t Submit(UGCUpdateHandle_t handle, string changeNote)
	{
		try
		{
			if (Hook != null && handle == Handle)
			{
				string notes = BuildChangeNote(out _publishingVersion);
				if (!notes.NullOrEmpty())
				{
					changeNote = notes;
					Log.Message("[RaidAnimalFix] Workshop: change note from About/WorkshopChangeNotes.txt (up to " + _publishingVersion + ", " + notes.Length + " chars)");
				}
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] Workshop: change note failed, using the default: " + ex.Message);
		}
		return SteamUGC.SubmitItemUpdate(handle, changeNote);
	}

	private static string BuildChangeNote(out string topVersion)
	{
		topVersion = null;
		string text = ReadAbout("WorkshopChangeNotes.txt");
		return (text == null) ? null : ComposeChangeNote(text, LastPublishedVersion(), out topVersion);
	}

	/// <summary>Неопубликованные записи журнала (новые сверху), не длиннее лимита Steam.</summary>
	internal static string ComposeChangeNote(string text, Version published, out string topVersion)
	{
		topVersion = null;
		text = text.Replace("\r\n", "\n");
		// Записи: «[b]3.0.942.0[/b] …» и «[b]3.0.942.0 (по-русски)[/b] …», разделитель «---».
		List<KeyValuePair<Version, string>> blocks = new List<KeyValuePair<Version, string>>();
		foreach (string raw in Regex.Split(text, "\\n-{3,}\\n"))
		{
			string block = raw.Trim();
			Match m = Regex.Match(block, "^\\[b\\](\\d+(?:\\.\\d+){1,3})");
			if (m.Success && Version.TryParse(m.Groups[1].Value, out Version v))
			{
				blocks.Add(new KeyValuePair<Version, string>(v, block));
			}
		}
		if (blocks.Count == 0)
		{
			return null;
		}
		Version newest = blocks[0].Key;
		topVersion = newest.ToString();
		StringBuilder sb = new StringBuilder();
		foreach (KeyValuePair<Version, string> b in blocks)
		{
			bool unpublished = (published != null) ? (b.Key > published) : (b.Key == newest);
			if (!unpublished)
			{
				continue;
			}
			string piece = ((sb.Length > 0) ? "\n\n---\n\n" : "") + b.Value;
			if (sb.Length + piece.Length > ChangeNoteMax)
			{
				if (sb.Length == 0)
				{
					sb.Append(Cap(b.Value, ChangeNoteMax));
				}
				break;
			}
			sb.Append(piece);
		}
		return sb.ToString();
	}

	/// <summary>Последняя опубликованная версия: наш файл, иначе копия из Workshop на диске.</summary>
	private static Version LastPublishedVersion()
	{
		if (Version.TryParse(ReadRecord().TryGetValue("version", out string recorded) ? recorded : null, out Version v))
		{
			return v;
		}
		try
		{
			if (SteamUGC.GetItemInstallInfo(Hook.PublishedFileId, out ulong _, out string folder, 1024u, out uint _) && !folder.NullOrEmpty())
			{
				string notes = Path.Combine(folder, "About", "WorkshopChangeNotes.txt");
				if (File.Exists(notes))
				{
					Match m = Regex.Match(File.ReadAllText(notes, Encoding.UTF8), "\\[b\\](\\d+(?:\\.\\d+){1,3})");
					if (m.Success && Version.TryParse(m.Groups[1].Value, out v))
					{
						return v;
					}
				}
			}
		}
		catch
		{
		}
		return null;
	}

	// ---------- после загрузки: запись и переводы описания ----------

	public static void OnSubmitted(bool success)
	{
		WorkshopItemHook hook = Hook;
		Hook = null;
		Handle = UGCUpdateHandle_t.Invalid;
		if (hook == null || !success)
		{
			return;
		}
		Dictionary<string, string> record = ReadRecord();
		if (!_publishingVersion.NullOrEmpty())
		{
			record["version"] = _publishingVersion;
		}
		WriteRecord(record);
		Hook = hook;
		LocalizedQueue.Clear();
		foreach (string[] lang in Languages)
		{
			string text = ReadAbout("WorkshopDescription." + lang[0] + ".txt");
			if (text == null)
			{
				continue;
			}
			string hash = Hash(text);
			if (!record.TryGetValue("desc." + lang[0], out string old) || old != hash)
			{
				LocalizedQueue.Enqueue(new string[4] { lang[0], lang[1], text, hash });
			}
		}
		if (LocalizedQueue.Count == 0)
		{
			Hook = null;
			return;
		}
		Log.Message("[RaidAnimalFix] Workshop: updating " + LocalizedQueue.Count + " localized description(s)");
		NextLocalized();
	}

	private static void NextLocalized()
	{
		if (LocalizedQueue.Count == 0 || Hook == null)
		{
			Hook = null;
			_localizedCurrent = null;
			return;
		}
		_localizedCurrent = LocalizedQueue.Dequeue();
		try
		{
			UGCUpdateHandle_t handle = SteamUGC.StartItemUpdate(SteamUtils.GetAppID(), Hook.PublishedFileId);
			SteamUGC.SetItemUpdateLanguage(handle, _localizedCurrent[1]);
			SteamUGC.SetItemTitle(handle, Hook.Name);
			SteamUGC.SetItemDescription(handle, Cap(_localizedCurrent[2], DescriptionMax));
			SteamAPICall_t call = SteamUGC.SubmitItemUpdate(handle, null);
			_localizedResult = CallResult<SubmitItemUpdateResult_t>.Create(OnLocalizedSubmitted);
			_localizedResult.Set(call);
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] Workshop: localized description (" + _localizedCurrent[1] + ") failed: " + ex.Message);
			Hook = null;
		}
	}

	private static void OnLocalizedSubmitted(SubmitItemUpdateResult_t result, bool ioFailure)
	{
		string[] cur = _localizedCurrent;
		if (ioFailure || result.m_eResult != EResult.k_EResultOK)
		{
			Log.Warning("[RaidAnimalFix] Workshop: localized description (" + cur?[1] + ") not accepted: " + result.m_eResult);
			Hook = null;
			_localizedResult = null;
			return;
		}
		Dictionary<string, string> record = ReadRecord();
		record["desc." + cur[0]] = cur[3];
		WriteRecord(record);
		Log.Message("[RaidAnimalFix] Workshop: description (" + cur[1] + ") updated");
		NextLocalized();
	}

	// ---------- файл записи ----------

	private static Dictionary<string, string> ReadRecord()
	{
		Dictionary<string, string> result = new Dictionary<string, string>();
		try
		{
			if (File.Exists(RecordPath))
			{
				foreach (string line in File.ReadAllLines(RecordPath))
				{
					int eq = line.IndexOf('=');
					if (eq > 0)
					{
						result[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
					}
				}
			}
		}
		catch
		{
		}
		return result;
	}

	private static void WriteRecord(Dictionary<string, string> record)
	{
		try
		{
			File.WriteAllLines(RecordPath, record.OrderBy((KeyValuePair<string, string> kv) => kv.Key, StringComparer.Ordinal).Select((KeyValuePair<string, string> kv) => kv.Key + "=" + kv.Value).ToArray());
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] Workshop: cannot write " + RecordPath + ": " + ex.Message);
		}
	}

	private static string Hash(string text)
	{
		using SHA1 sha = SHA1.Create();
		return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "");
	}
}

[HarmonyPatch(typeof(Workshop), "SetWorkshopItemDataFrom")]
public static class WorkshopPublishDescription
{
	private static void Postfix(UGCUpdateHandle_t updateHandle, WorkshopItemHook hook)
	{
		try
		{
			RafWorkshop.OnItemData(updateHandle, hook);
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] Workshop: description failed: " + ex.Message);
		}
	}
}

[HarmonyPatch]
public static class WorkshopPublishChangeNote
{
	private static IEnumerable<MethodBase> TargetMethods()
	{
		yield return AccessTools.Method(typeof(Workshop), "Upload");
		yield return AccessTools.Method(typeof(Workshop), "OnItemCreated");
	}

	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
	{
		MethodInfo submit = AccessTools.Method(typeof(SteamUGC), "SubmitItemUpdate");
		MethodInfo ours = AccessTools.Method(typeof(RafWorkshop), "Submit");
		int n = 0;
		foreach (CodeInstruction ci in instructions)
		{
			if (ci.Calls(submit))
			{
				ci.opcode = OpCodes.Call;
				ci.operand = ours;
				n++;
			}
			yield return ci;
		}
		if (n == 0)
		{
			Log.Warning("[RaidAnimalFix] Workshop: SubmitItemUpdate not found in " + __originalMethod?.Name + " (RimWorld updated?) - change notes stay default");
		}
	}
}

[HarmonyPatch(typeof(Workshop), "OnItemSubmitted")]
public static class WorkshopPublishSubmitted
{
	private static void Postfix(SubmitItemUpdateResult_t result, bool IOFailure)
	{
		try
		{
			RafWorkshop.OnSubmitted(!IOFailure && result.m_eResult == EResult.k_EResultOK);
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] Workshop: post-upload step failed: " + ex.Message);
		}
	}
}
