using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RaidAnimalFix;

/// <summary>
/// При старте игры в MP: (1) ленивые патчи для модов, которые грузятся
/// позже нас (Map Preview через LunarLoader); (2) проверка списка модов —
/// хост получает окно с несовместимыми/рискованными модами, клиенты — сообщение.
/// </summary>
[HarmonyPatch(typeof(Game), "FinalizeInit")]
public static class ModSafetyOnGameStart
{
	private static void Postfix()
	{
		if (!MpHelper.IsMultiplayer())
		{
			return;
		}
		try
		{
			MapPreviewMpGuard.Apply();
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] MapPreview guard: " + ex.Message);
		}
		try
		{
			ReplaceStuffOverRockToggleFix.ForceOn();
			ReplaceStuffBridgeReorderFix.NormalizeOrder();
		}
		catch (Exception ex3)
		{
			Log.Warning("[RaidAnimalFix] ReplaceStuff compat: " + ex3.Message);
		}
		try
		{
			ModSafetyCheck.Run();
		}
		catch (Exception ex2)
		{
			Log.Warning("[RaidAnimalFix] Mod safety check: " + ex2.Message);
		}
	}
}

/// <summary>
/// Map Preview: кнопка «перебросить сид» на карте мира пишет SeedRerollData
/// (WorldComponent) только у нажавшего → карта на этом тайле генерируется у
/// всех по-разному. Превью в игре строит карту в фоновом потоке и засоряет
/// следы рассинхрона MP, а перед GenerateMap может ждать его до 60 с.
/// В MP: превью и тулбар в игре выключены (геттеры, настройки не трогаем),
/// Commit/Reset сидов во время игры заблокированы.
/// </summary>
internal static class MapPreviewMpGuard
{
	private static bool _applied;

	public static void Apply()
	{
		if (_applied)
		{
			return;
		}
		Type settings = AccessTools.TypeByName("MapPreview.MapPreviewSettings");
		Type reroll = AccessTools.TypeByName("MapPreview.SeedRerollData");
		if (settings == null && reroll == null)
		{
			return;
		}
		_applied = true;
		Harmony harmony = new Harmony("customfix.raidanimalfix.mappreview");
		HarmonyMethod falseInMp = new HarmonyMethod(typeof(MapPreviewMpGuard), "FalseInMp");
		HarmonyMethod blockInMp = new HarmonyMethod(typeof(MapPreviewMpGuard), "BlockInPlay");
		int num = 0;
		if (settings != null)
		{
			foreach (string name in new string[2] { "PreviewEnabledNow", "ToolbarEnabledNow" })
			{
				MethodInfo getter = AccessTools.PropertyGetter(settings, name);
				if (getter != null)
				{
					harmony.Patch(getter, falseInMp);
					num++;
				}
			}
		}
		if (reroll != null)
		{
			MethodInfo commit = AccessTools.Method(reroll, "Commit", new Type[3]
			{
				typeof(int),
				typeof(int),
				typeof(bool)
			});
			MethodInfo reset = AccessTools.Method(reroll, "Reset", new Type[2]
			{
				typeof(int),
				typeof(bool)
			});
			if (commit != null)
			{
				harmony.Patch(commit, blockInMp);
				num++;
			}
			if (reset != null)
			{
				harmony.Patch(reset, blockInMp);
				num++;
			}
		}
		Log.Message("[RaidAnimalFix] MapPreview: превью/тулбар в игре и переброс сида выключены в мультиплеере (" + num + " патчей)");
	}

	public static bool FalseInMp(ref bool __result)
	{
		if (!MpHelper.IsMultiplayer() || Current.ProgramState != ProgramState.Playing)
		{
			return true;
		}
		__result = false;
		return false;
	}

	public static bool BlockInPlay()
	{
		return !MpHelper.IsMultiplayer() || Current.ProgramState != ProgramState.Playing;
	}
}

internal static class ModSafetyCheck
{
	private enum Verdict
	{
		Safe,
		Mitigated,
		Risky,
		Incompatible
	}

	private struct Entry
	{
		public string id;

		public Verdict verdict;

		public string reason;

		/// <summary>
		/// Вердикт верен только при загруженном Multiplayer Compatibility: синхронизацию
		/// мода делают его патчи, а не мы. Без него вердикт понижается до риска.
		/// </summary>
		public bool needsMpCompat;

		public Entry(string id, Verdict verdict, string reason, bool needsMpCompat = false)
		{
			this.id = id;
			this.verdict = verdict;
			this.reason = reason;
			this.needsMpCompat = needsMpCompat;
		}
	}

	private const string MpCompatId = "rwmt.multiplayercompatibility";

	// Только моды с известным вердиктом. Безопасные перечислены, чтобы было
	// видно: их проверяли (и чтобы не путать с «неизвестными»).
	private static readonly Entry[] Table = new Entry[]
	{
		new Entry("rimexodus.seamlessworld", Verdict.Mitigated, "RAF_Safety_rimexodus_seamlessworld"),
		new Entry("ferny.perspectiveshift", Verdict.Incompatible, "RAF_Safety_ferny_perspectiveshift"),
		new Entry("memegoddess.replacestuff", Verdict.Mitigated, "RAF_Safety_memegoddess_replacestuff"),
		new Entry("jellycreative.isekaileveling", Verdict.Mitigated, "RAF_Safety_jellycreative_isekaileveling", needsMpCompat: true),
		new Entry("mehni.pickupandhaul", Verdict.Risky, "RAF_Safety_mehni_pickupandhaul"),
		new Entry("m00nl1ght.mappreview", Verdict.Mitigated, "RAF_Safety_m00nl1ght_mappreview"),
		new Entry("co.uk.epicguru.meleeanimation", Verdict.Mitigated, "RAF_Safety_co_uk_epicguru_meleeanimation"),
		new Entry("nals.facialanimation", Verdict.Mitigated, "RAF_Safety_nals_facialanimation"),
		new Entry("fuu.bloodanimations", Verdict.Mitigated, "RAF_Safety_fuu_bloodanimations"),
		new Entry("com.yayo.yayoani.continued", Verdict.Safe, "RAF_Safety_com_yayo_yayoani_continued"),
		new Entry("unlimitedhugs.allowtool", Verdict.Safe, "RAF_Safety_unlimitedhugs_allowtool", needsMpCompat: true),
		new Entry("unlimitedhugs.hugslib", Verdict.Safe, "RAF_Safety_unlimitedhugs_hugslib", needsMpCompat: true),
		new Entry("andromeda.usefulmarks", Verdict.Safe, "RAF_Safety_andromeda_usefulmarks"),
		new Entry("jaxe.bubbles", Verdict.Safe, "RAF_Safety_jaxe_bubbles"),
		new Entry("brrainz.cameraplus", Verdict.Safe, "RAF_Safety_brrainz_cameraplus"),
		new Entry("ray1203.simplecamerasetting", Verdict.Safe, "RAF_Safety_ray1203_simplecamerasetting"),
		new Entry("tixiv.whoshotmylegoff", Verdict.Safe, "RAF_Safety_tixiv_whoshotmylegoff"),
		new Entry("ferny.resourcedeliveryhelper", Verdict.Safe, "RAF_Safety_ferny_resourcedeliveryhelper"),
		new Entry("frozensnowfox.filthvanisheswithrainandtime", Verdict.Safe, "RAF_Safety_frozensnowfox_filthvanisheswithrainandtime"),
		new Entry("kilo.plaguedoc", Verdict.Safe, "RAF_Safety_kilo_plaguedoc"),
		new Entry("smashphil.vehicleframework", Verdict.Mitigated, "RAF_Safety_smashphil_vehicleframework"),
		new Entry("oskarpotocki.vanillavehiclesexpanded", Verdict.Mitigated, "RAF_Safety_smashphil_vehicleframework"),
		new Entry("ceteam.combatextended", Verdict.Mitigated, "RAF_Safety_ceteam_combatextended"),
		new Entry("cemp.desyncfix", Verdict.Safe, "RAF_Safety_cemp_desyncfix"),
		new Entry("memegoddess.runandgun", Verdict.Mitigated, "RAF_Safety_memegoddess_runandgun"),
		new Entry("memegoddess.giddyup", Verdict.Mitigated, "RAF_Safety_memegoddess_giddyup"),
		new Entry("vanillaexpanded.gravship2", Verdict.Mitigated, "RAF_Safety_vanillaexpanded_gravship2"),
		new Entry("ancot.milirarace", Verdict.Risky, "RAF_Safety_ancot_milirarace"),
		new Entry("redmattis.betterprerequisites", Verdict.Mitigated, "RAF_Safety_redmattis_betterprerequisites")
	};

	private static bool _shown;

	public static void Run()
	{
		if (_shown)
		{
			return;
		}
		_shown = true;
		HashSet<string> active = new HashSet<string>();
		foreach (ModContentPack runningMod in LoadedModManager.RunningMods)
		{
			if (runningMod.PackageIdPlayerFacing != null)
			{
				active.Add(runningMod.PackageIdPlayerFacing.ToLowerInvariant());
			}
		}
		// MP Compat — набор патчей под конкретные моды, ванилу он не трогает. Если его
		// нет, моды, которые синхронизирует только он, молча рассинхронятся.
		bool hasMpCompat = active.Contains(MpCompatId);
		StringBuilder bad = new StringBuilder();
		StringBuilder info = new StringBuilder();
		int worst = 0;
		foreach (Entry entry in Table)
		{
			if (!active.Contains(entry.id))
			{
				continue;
			}
			string name = NameOf(entry.id);
			Verdict verdict = entry.verdict;
			string reason = entry.reason;
			if (entry.needsMpCompat && !hasMpCompat && verdict < Verdict.Risky)
			{
				verdict = Verdict.Risky;
				reason = "RAF_Safety_NeedsMpCompat";
			}
			switch (verdict)
			{
			case Verdict.Incompatible:
				bad.AppendLine("✖ " + name + ": " + reason.Translate().Resolve());
				worst = Math.Max(worst, 2);
				break;
			case Verdict.Risky:
				bad.AppendLine("⚠ " + name + ": " + reason.Translate().Resolve());
				worst = Math.Max(worst, 1);
				break;
			case Verdict.Mitigated:
				info.AppendLine("✔ " + name + ": " + reason.Translate().Resolve());
				break;
			}
		}
		string text = bad.ToString() + ((info.Length > 0) ? ("\n" + info) : "");
		if (text.Trim().Length > 0)
		{
			Log.Message("[RaidAnimalFix] Mod safety check:\n" + text);
		}
		if (worst == 0)
		{
			return;
		}
		if (MpHelper.IsHost())
		{
			string title = ((worst >= 2) ? "RAF_Safety_TitleBreak" : "RAF_Safety_TitleRisk").Translate().Resolve();
			LongEventHandler.ExecuteWhenFinished(delegate
			{
				Find.WindowStack.Add(new Dialog_MessageBox("[Desync Fix] " + title + "\n\n" + text));
			});
		}
		else
		{
			Messages.Message("[Desync Fix] " + ((worst >= 2) ? "RAF_Safety_ClientBreak" : "RAF_Safety_ClientRisk").Translate().Resolve(), MessageTypeDefOf.CautionInput, historical: false);
		}
	}

	private static string NameOf(string id)
	{
		foreach (ModContentPack runningMod in LoadedModManager.RunningMods)
		{
			if (string.Equals(runningMod.PackageIdPlayerFacing, id, StringComparison.OrdinalIgnoreCase))
			{
				return runningMod.Name;
			}
		}
		return id;
	}
}
