using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RaidAnimalFix;

// ───────────────────────── 1. Гравикорабль и чужие колонии ─────────────────────────

/// <summary>
/// Settlement.GravShipCanLandOn => Faction != Faction.OfPlayer: чужое поселение
/// игрока для ванили — обычная клетка, туда можно посадить корабль. В MP
/// посадка на поселение другого игрока запрещена.
/// </summary>
[HarmonyPatch(typeof(Settlement), "GravShipCanLandOn", MethodType.Getter)]
public static class GravshipNoLandingOnOtherPlayers
{
	private static void Postfix(Settlement __instance, ref bool __result)
	{
		if (!__result || !MpHelper.IsMultiplayer())
		{
			return;
		}
		Faction faction = __instance.Faction;
		if (MpContext.IsRealPlayerFaction(faction) && faction != Faction.OfPlayer)
		{
			__result = false;
		}
	}
}

/// <summary>
/// Взлёт без гравиякоря бросает карту целиком (все пешки → «потерян», поселение
/// уничтожается). Если корабль взлетает с карты ДРУГОГО игрока, бросить её —
/// значит уничтожить чужую колонию. В MP бросаем карту только её владельцу.
/// </summary>
[HarmonyPatch(typeof(GravshipUtility), "AbandonMap")]
public static class GravshipNoAbandonOtherPlayersMap
{
	private static readonly FieldInfo GravshipField = AccessTools.Field(typeof(WorldComponent_GravshipController), "gravship");

	private static bool Prefix(Map map)
	{
		if (!MpHelper.IsMultiplayer() || map == null)
		{
			return true;
		}
		try
		{
			Faction owner = map.ParentFaction;
			if (!MpContext.IsRealPlayerFaction(owner))
			{
				return true;
			}
			Faction faction = null;
			WorldComponent_GravshipController component = Find.World.GetComponent<WorldComponent_GravshipController>();
			if (component != null && GravshipField?.GetValue(component) is Gravship gravship)
			{
				faction = gravship.Faction;
			}
			if (faction == owner)
			{
				return true;
			}
			// Корабль чужой (или неизвестно чей), а на карте ещё есть хозяева — карту не трогаем.
			if (faction != null || NotificationFilter.MapHasColonistsOf(map, owner))
			{
				Log.Message("[RaidAnimalFix] Gravship: карта " + map + " принадлежит " + owner.Name + " — взлёт чужого корабля её не бросает");
				return false;
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("GravshipNoAbandonOtherPlayersMap", ex);
		}
		return true;
	}
}

// ───────────────────────── 2. Эвакуация гостей ─────────────────────────

/// <summary>
/// Когда карта закрывается (поселение брошено, улетел корабль, конец квеста),
/// MapDeiniter.PassPawnsToWorld отправляет всех пешек в мировые пешки. Для
/// колонистов ДРУГОГО игрока ни письма, ни мыслей «потерян» нет — они просто
/// пропадают. В MP перед этим выводим гостей каждой игровой фракции караваном.
/// </summary>
[HarmonyPatch(typeof(MapDeiniter), "PassPawnsToWorld")]
public static class GuestEvacuationOnMapClose
{
	private static void Prefix(Map map)
	{
		if (!MpHelper.IsMultiplayer() || map == null || !MpSim.CanRestoreContext)
		{
			return;
		}
		try
		{
			Faction owner = map.ParentFaction;
			Dictionary<Faction, List<Pawn>> byFaction = new Dictionary<Faction, List<Pawn>>();
			List<Faction> order = new List<Faction>();
			IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
			for (int i = 0; i < pawns.Count; i++)
			{
				Pawn pawn = pawns[i];
				Faction faction = pawn?.Faction;
				if (faction == null || faction == owner || !MpContext.IsRealPlayerFaction(faction) || pawn.Dead || pawn.HostFaction != null)
				{
					continue;
				}
				if (!byFaction.TryGetValue(faction, out var list))
				{
					list = new List<Pawn>();
					byFaction[faction] = list;
					order.Add(faction);
				}
				list.Add(pawn);
			}
			foreach (Faction faction2 in order)
			{
				List<Pawn> list2 = byFaction[faction2];
				if (!list2.Any((Pawn p) => p.RaceProps.Humanlike))
				{
					continue;
				}
				MpSim.PushFaction(null, faction2);
				try
				{
					Caravan caravan = CaravanExitMapUtility.ExitMapAndCreateCaravan(list2, faction2, map.Tile, map.Tile, PlanetTile.Invalid, sendMessage: false);
					Log.Message("[RaidAnimalFix] GuestEvacuation: " + list2.Count + " пешек фракции " + faction2.Name + " выведены караваном с закрывающейся карты " + map);
					if (caravan != null && faction2 == MpHelper.GetCurrentPlayerFaction())
					{
						Messages.Message("[Desync Fix] " + "RAF_Evacuated".Translate().Resolve(), caravan, MessageTypeDefOf.CautionInput, historical: false);
					}
				}
				finally
				{
					MpSim.PopFaction(null);
				}
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("GuestEvacuationOnMapClose", ex);
		}
	}
}

// ───────────────────────── 6. Потерянные письма ─────────────────────────

/// <summary>
/// Мир тикает от имени наблюдателя, а MP (LetterStackReceiveOnlyMyFaction)
/// удаляет письма, у которых Faction.OfPlayer — не фракция игрока. В итоге
/// письма из тика мира и все отложенные письма (их выдаёт тик мира:
/// ревенант, Sightstealer, кодекс, серая мгла, пробуждение трупа,
/// преследующие механоиды...) не доходили ни до кого. Возвращаем письмо тем,
/// чьи это цели; письмо без целей (про весь мир) — всем.
/// Стек писем в MP и так у каждого свой — синхронизацию это не трогает.
/// </summary>
[HarmonyPatch(typeof(LetterStack), "ReceiveLetter", new Type[]
{
	typeof(Letter),
	typeof(string),
	typeof(int),
	typeof(bool)
})]
public static class SpectatorLetterDelivery
{
	[HarmonyPriority(Priority.Last)]
	private static void Postfix(LetterStack __instance, Letter let, int delayTicks)
	{
		if (delayTicks > 0 || let == null || __instance == null || !MpHelper.IsMultiplayer())
		{
			return;
		}
		try
		{
			Faction ofPlayer = Faction.OfPlayer;
			if (ofPlayer == null || !MpContext.IsSpectator(ofPlayer))
			{
				return;
			}
			List<Letter> letters = __instance.LettersListForReading;
			if (letters.Contains(let))
			{
				return;
			}
			Faction me = MpHelper.GetCurrentPlayerFaction();
			if (me == null || MpContext.IsSpectator(me))
			{
				return;
			}
			if (NotificationFilter.Classify(let.lookTargets, me) == NotificationFilter.Verdict.Foreign)
			{
				return;
			}
			letters.Add(let);
			if (!let.def.arriveSound.NullOrUndefined())
			{
				let.def.arriveSound.PlayOneShotOnCamera();
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("SpectatorLetterDelivery", ex);
		}
	}
}

// ───────────────────────── 7. Похищение на чужой карте ─────────────────────────

/// <summary>
/// Похищение выполняется в тике рейдера, где «игрок» — хозяин карты.
/// KidnappedPawnsTracker.Kidnap шлёт письмо, мысли «потерян» и снимает
/// счета только если pawn.Faction == Faction.OfPlayer — колонист гостя
/// пропадал молча. На время вызова ставим фракцию похищенного.
/// </summary>
[HarmonyPatch(typeof(KidnappedPawnsTracker), "Kidnap")]
public static class KidnapOwnerContextFix
{
	private static void Prefix(Pawn pawn, ref bool __state)
	{
		__state = false;
		if (!MpHelper.IsMultiplayer() || pawn == null || !MpSim.CanRestoreContext)
		{
			return;
		}
		Faction faction = pawn.Faction;
		if (faction == null || faction == Faction.OfPlayer || !MpContext.IsRealPlayerFaction(faction))
		{
			return;
		}
		try
		{
			MpSim.PushFaction(null, faction);
			__state = true;
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("KidnapOwnerContextFix", ex);
		}
	}

	private static void Finalizer(bool __state)
	{
		if (!__state)
		{
			return;
		}
		try
		{
			MpSim.PopFaction(null);
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("KidnapOwnerContextFix.pop", ex);
		}
	}
}

// ───────────────────────── 15. Сверка настроек модов ─────────────────────────

/// <summary>
/// Pick Up And Haul, ISEKAI, Replace Stuff, Melee Animation читают свои
/// настройки прямо в симуляции. MP копирует настройки хоста только при входе
/// (Sync configs); изменение посреди игры — рассинхрон. Каждый клиент
/// считает отпечаток настроек этих модов и отправляет его синхронной командой;
/// у всех отпечатки сравниваются, при расхождении — сообщение с именами
/// игроков и модов. Состояние игры не меняется.
/// </summary>
[StaticConstructorOnStartup]
public static class SettingsHashCheck
{
	private static readonly string[] SimSettingsMods = new string[9] { "mehni.pickupandhaul", "jellycreative.isekaileveling", "memegoddess.replacestuff", "co.uk.epicguru.meleeanimation", "rimexodus.seamlessworld", "ceteam.combatextended", "vanillaexpanded.vfepropsanddecor", "ancot.milirarace", "ancot.ancotlibrary" };

	/// <summary>Сборщики отпечатков для модов, чьи настройки не укладываются в общий дамп.</summary>
	internal static readonly List<Action<StringBuilder>> ExtraSettingsPayload = new List<Action<StringBuilder>>();

	internal static string FnvHex(string s)
	{
		return Fnv(s).ToString("x8");
	}

	// Поля, которые симуляция не читает (логи, профайлер, интерфейс) или которые мы
	// переопределяем в MP сами — их различие рассинхрона не даёт, не предупреждаем.
	private static readonly Dictionary<string, HashSet<string>> SimSettingsIgnore = new Dictionary<string, HashSet<string>>
	{
		{
			"rimexodus.seamlessworld",
			new HashSet<string>
			{
				"verboseLogging", "logGeneration", "logTransfer", "logCaravanExit", "logWeather", "logDormancy", "logCombat", "logCombatExtended", "logCompat", "logSettlement",
				"logCore", "tickProfilingEnabled", "tickProfileIntervalTicks", "hideEmptyColonistBarGroups", "incrementalGenerationEnabled", "generationBatchSize"
			}
		},
		{
			"ceteam.combatextended",
			new HashSet<string>
			{
				"showCasings", "recoilAnim", "showTaunts", "showBackpacks", "showTacticalVests", "realWeaponNames", "showExtraTooltips", "detailedMeleeTooltip", "nonPlayerMeleeTooltip", "showExtraStats",
				"ShowTutorialPopup", "rightClickAmmoSelect", "showCaliberOnGuns", "logUnpatchedDefs", "debugVerbose", "debugDrawPartialLoSChecks", "debugDrawTargetCoverChecks", "debugMuzzleFlash", "debugShowTreeCollisionChance", "debugShowSuppressionBuildup",
				"debugDrawInterceptChecks", "debugDisplayDangerBuildup", "debugDisplayCellCoverRating", "debugDisplayAttritionInfo", "debugAutopatcherLogger", "lastAmmoSystemStatus", "enableExtraEffects"
			}
		},
		{
			"ancot.milirarace",
			new HashSet<string> { "MiliraRace_ModSetting_MilianConfigTab", "TabAvailable_MilianConfig", "TabAvailable_MilianWork", "MiliraRace_ModSetting_MilianHairColor", "MiliraRace_ModSetting_MilianHairColor_PlayerColorOverride", "MiliraRace_ModSetting_MilianHairColorOffset", "MiliraRace_ModSetting_MilianDrawHeadgear" }
		},
		{
			"ancot.ancotlibrary",
			new HashSet<string>
			{
				"color_Awful", "color_Poor", "color_Normal", "color_Good", "color_Excellent", "color_Masterwork", "color_Legendary", "turretSystem_AimingIndicator", "turretSystem_AimingIndicator_NonPlayer", "turretSystem_IndicatorOnlyDrawSelected",
				"turretSystem_IndicatorOnlyDrawSelected_NonPlayer", "turretSystem_IndicatorOnlyDrawForced", "turretSystem_IndicatorColor_Player", "turretSystem_IndicatorColor_NonPlayer", "weaponExtraRender_Draw", "drone_TabAvailable"
			}
		}
	};

	private static bool _registered;

	private static float _nextCheck;

	private static string _lastSent;

	private static Game _game;

	private static readonly Dictionary<string, Dictionary<string, string>> Reports = new Dictionary<string, Dictionary<string, string>>();

	private static string _lastWarning;

	static SettingsHashCheck()
	{
		try
		{
			Type type = AccessTools.TypeByName("Multiplayer.API.MP");
			FieldInfo fieldInfo = type?.GetField("enabled", BindingFlags.Static | BindingFlags.Public);
			if (fieldInfo == null || !(bool)fieldInfo.GetValue(null))
			{
				return;
			}
			Type type2 = AccessTools.TypeByName("Multiplayer.API.SyncType");
			MethodInfo methodInfo = (type2 == null) ? null : type.GetMethod("RegisterSyncMethod", BindingFlags.Static | BindingFlags.Public, null, new Type[2]
			{
				typeof(MethodInfo),
				type2.MakeArrayType()
			}, null);
			if (methodInfo != null)
			{
				methodInfo.Invoke(null, new object[2]
				{
					AccessTools.Method(typeof(SettingsHashCheck), "Report"),
					null
				});
				_registered = true;
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] SettingsHashCheck registration failed: " + ex.Message);
		}
	}

	/// <summary>Зовётся каждый кадр из UI. Раз в 5 с пересчитывает отпечаток; при изменении — отправляет.</summary>
	public static void UiTick()
	{
		if (!_registered || !MpHelper.IsMultiplayer() || Current.ProgramState != ProgramState.Playing)
		{
			return;
		}
		if (_game != Current.Game)
		{
			_game = Current.Game;
			_lastSent = null;
			_nextCheck = Time.realtimeSinceStartup + 10f;
			Reports.Clear();
			_lastWarning = null;
		}
		if (Time.realtimeSinceStartup < _nextCheck || MpJoinSession.IsLocalSimulating() || !UiOnly.InInterface())
		{
			return;
		}
		_nextCheck = Time.realtimeSinceStartup + 5f;
		string text = BuildPayload();
		if (text.Length == 0 || text == _lastSent)
		{
			return;
		}
		_lastSent = text;
		string text2 = MpJoinSession.LocalUsername();
		if (!string.IsNullOrEmpty(text2))
		{
			Report(text2, text);
		}
	}

	/// <summary>Синхронный метод MP: выполняется у всех. Меняет только статический словарь отчётов.</summary>
	public static void Report(string user, string payload)
	{
		if (user == null || payload == null)
		{
			return;
		}
		Dictionary<string, string> dictionary = new Dictionary<string, string>();
		string[] array = payload.Split(new char[1] { ';' }, StringSplitOptions.RemoveEmptyEntries);
		foreach (string text in array)
		{
			int num = text.IndexOf('=');
			if (num > 0)
			{
				dictionary[text.Substring(0, num)] = text.Substring(num + 1);
			}
		}
		Reports[user] = dictionary;
		Compare();
	}

	private static void Compare()
	{
		if (Reports.Count < 2)
		{
			return;
		}
		StringBuilder stringBuilder = new StringBuilder();
		HashSet<string> hashSet = new HashSet<string>();
		foreach (Dictionary<string, string> value in Reports.Values)
		{
			foreach (string key in value.Keys)
			{
				hashSet.Add(key);
			}
		}
		foreach (string item in hashSet.OrderBy((string k) => k))
		{
			Dictionary<string, List<string>> dictionary = new Dictionary<string, List<string>>();
			foreach (KeyValuePair<string, Dictionary<string, string>> item2 in Reports.OrderBy((KeyValuePair<string, Dictionary<string, string>> r) => r.Key))
			{
				string key2 = (item2.Value.TryGetValue(item, out var value2) ? value2 : "RAF_SettingsNoMod".Translate().Resolve());
				if (!dictionary.TryGetValue(key2, out var value3))
				{
					value3 = (dictionary[key2] = new List<string>());
				}
				value3.Add(item2.Key);
			}
			if (dictionary.Count > 1)
			{
				stringBuilder.Append(ModName(item)).Append(": ").Append(string.Join(" | ", dictionary.Values.Select((List<string> g) => string.Join(", ", g)).ToArray()))
					.Append("; ");
			}
		}
		string text = stringBuilder.ToString();
		if (text.Length > 0 && text != _lastWarning)
		{
			_lastWarning = text;
			Log.Warning("[RaidAnimalFix] Настройки модов различаются между игроками (будет рассинхрон): " + text);
			Messages.Message("[Desync Fix] " + "RAF_SettingsMismatch".Translate().Resolve() + " " + text, MessageTypeDefOf.NegativeEvent, historical: false);
		}
		else if (text.Length == 0)
		{
			_lastWarning = null;
		}
	}

	private static string ModName(string id)
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

	private static readonly FieldInfo ModSettingsField = AccessTools.Field(typeof(Mod), "modSettings");

	// Allow Tool хранит настройки в HugsLib (SettingHandle<T>), не в ModSettings.
	// Берём только те, что читает симуляция; визуальные (иконки, горячие клавиши) — нет.
	private static readonly string[] AllowToolSimHandles = new string[5] { "FinishOffSkillRequirement", "FinishOffUnforbidsSetting", "HaulWorktypeSetting", "FinishOffWorktypeSetting", "PartyHuntSetting" };

	private static void AppendAllowTool(StringBuilder payload)
	{
		Type type = AccessTools.TypeByName("AllowTool.AllowToolController");
		object obj = (type == null) ? null : AccessTools.Property(type, "Instance")?.GetValue(null, null);
		object obj2 = (obj == null) ? null : (AccessTools.Property(type, "Handles")?.GetValue(obj, null) ?? AccessTools.Field(type, "Handles")?.GetValue(obj));
		if (obj2 == null)
		{
			return;
		}
		StringBuilder stringBuilder = new StringBuilder();
		foreach (string text in AllowToolSimHandles)
		{
			object v = AccessTools.Property(obj2.GetType(), text)?.GetValue(obj2, null) ?? AccessTools.Field(obj2.GetType(), text)?.GetValue(obj2);
			stringBuilder.Append(text).Append(':');
			AppendValue(v, stringBuilder, 0);
			stringBuilder.Append('|');
		}
		payload.Append("unlimitedhugs.allowtool=").Append(Fnv(stringBuilder.ToString()).ToString("x8")).Append(';');
	}

	private static string BuildPayload()
	{
		StringBuilder stringBuilder = new StringBuilder();
		try
		{
			AppendAllowTool(stringBuilder);
		}
		catch
		{
		}
		foreach (Action<StringBuilder> extra in ExtraSettingsPayload)
		{
			try
			{
				extra(stringBuilder);
			}
			catch
			{
			}
		}
		foreach (Mod modHandle in LoadedModManager.ModHandles)
		{
			string text = modHandle.Content?.PackageIdPlayerFacing?.ToLowerInvariant();
			if (text == null || Array.IndexOf(SimSettingsMods, text) < 0)
			{
				continue;
			}
			object obj = null;
			try
			{
				obj = ModSettingsField?.GetValue(modHandle);
			}
			catch
			{
			}
			if (obj == null)
			{
				continue;
			}
			StringBuilder stringBuilder2 = new StringBuilder();
			SimSettingsIgnore.TryGetValue(text, out HashSet<string> ignore);
			Dump(obj.GetType(), obj, stringBuilder2, ignore);
			stringBuilder.Append(text).Append('=').Append(Fnv(stringBuilder2.ToString()).ToString("x8"))
				.Append(';');
		}
		return stringBuilder.ToString();
	}

	private static void Dump(Type type, object instance, StringBuilder sb, HashSet<string> ignore = null)
	{
		foreach (FieldInfo item in from f in type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
			orderby f.Name
			select f)
		{
			if (item.IsLiteral || (item.IsStatic && item.IsInitOnly && !item.FieldType.IsPrimitive) || (ignore != null && ignore.Contains(item.Name)))
			{
				continue;
			}
			object value;
			try
			{
				value = item.GetValue(item.IsStatic ? null : instance);
			}
			catch
			{
				continue;
			}
			sb.Append(item.Name).Append(':');
			AppendValue(value, sb, 0);
			sb.Append('|');
		}
	}

	private static void AppendValue(object v, StringBuilder sb, int depth)
	{
		if (v == null)
		{
			sb.Append("null");
			return;
		}
		Type type = v.GetType();
		if (type.IsPrimitive || type.IsEnum || v is string || v is decimal)
		{
			sb.Append(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture));
			return;
		}
		if (v is Def def)
		{
			sb.Append(def.defName);
			return;
		}
		if (depth < 2 && v is IEnumerable enumerable)
		{
			sb.Append('[');
			foreach (object item in enumerable)
			{
				AppendValue(item, sb, depth + 1);
				sb.Append(',');
			}
			sb.Append(']');
			return;
		}
		// Обёртки вида Entry<T>/SettingHandle<T> — берём поле/свойство Value.
		FieldInfo field = type.GetField("Value", BindingFlags.Instance | BindingFlags.Public);
		PropertyInfo property = (field == null) ? type.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public) : null;
		if (depth < 2 && (field != null || (property != null && property.GetIndexParameters().Length == 0)))
		{
			object v2;
			try
			{
				v2 = (field != null) ? field.GetValue(v) : property.GetValue(v, null);
			}
			catch
			{
				v2 = null;
			}
			AppendValue(v2, sb, depth + 1);
			return;
		}
		sb.Append(type.Name);
	}

	private static uint Fnv(string s)
	{
		uint num = 2166136261u;
		foreach (char c in s)
		{
			num ^= c;
			num *= 16777619;
		}
		return num;
	}
}

[HarmonyPatch(typeof(UIRoot_Play), "UIRootUpdate")]
public static class SettingsHashCheckUiHook
{
	private static void Postfix()
	{
		try
		{
			SettingsHashCheck.UiTick();
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("SettingsHashCheck", ex);
		}
	}
}
