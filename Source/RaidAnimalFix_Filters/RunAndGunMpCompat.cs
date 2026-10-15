using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RaidAnimalFix;

// RUNANDGUN — CONTINUED (MemeGoddess.RunAndGun) + Multiplayer.
// Таблица совместимости MP (08.2026): «рассинхрон, когда дрефтованные пешки стреляют
// на ходу; если выключить run-and-gun — проходит». Переключатель синхронизирован
// Multiplayer Compatibility правильно. Настоящая причина — настройки:
//  * Списки «лёгкого» и запрещённого оружия (selectedWeapons / forbiddenWeapons)
//    заполняются ТОЛЬКО когда игрок открывает окно настроек мода. У того, кто окно
//    не открывал, списки пустые → всё оружие считается тяжёлым → штраф к скорости
//    40% вместо 10% → пешки на ходу движутся с разной скоростью у разных игроков.
//    → в MP при загрузке игры списки заполняются так же, как это сделало бы окно
//    (детерминированно по дефам и порогу веса; ручные исключения игрока сохраняются).
//  * Штрафы скорости/точности, режим для ИИ, шанс при бегстве читает симуляция.
//    → сверка настроек (с содержимым списков) и блокировка окна во время MP-игры.

internal static class RunAndGunMp
{
	private static bool _resolved;

	private static FieldInfo _settings;

	private static MethodInfo _filter;

	private static MethodInfo _allWeapons;

	private static Type _settingsType;

	public static bool Present
	{
		get
		{
			Resolve();
			return _settings != null;
		}
	}

	private static void Resolve()
	{
		if (_resolved)
		{
			return;
		}
		_resolved = true;
		Type mod = AccessTools.TypeByName("RunAndGun.RunAndGun");
		_settings = (mod == null) ? null : AccessTools.Field(mod, "settings");
		_settingsType = AccessTools.TypeByName("RunAndGun.Settings");
		Type draw = AccessTools.TypeByName("RunAndGun.Utilities.DrawUtility");
		_filter = (draw == null) ? null : AccessTools.Method(draw, "FilterWeapons");
		Type util = AccessTools.TypeByName("RunAndGun.Utilities.WeaponUtility");
		_allWeapons = (util == null) ? null : AccessTools.Method(util, "getAllWeapons");
	}

	private static object Settings()
	{
		Resolve();
		return _settings?.GetValue(null);
	}

	private static object Get(object s, string field)
	{
		return AccessTools.Field(_settingsType, field)?.GetValue(s);
	}

	/// <summary>Заполнить списки оружия так, как это делает окно настроек.</summary>
	public static void Normalize()
	{
		object s = Settings();
		if (s == null || _filter == null || _allWeapons == null)
		{
			return;
		}
		try
		{
			object all = _allWeapons.Invoke(null, null);
			FieldInfo selected = AccessTools.Field(_settingsType, "selectedWeapons");
			FieldInfo forbidden = AccessTools.Field(_settingsType, "forbiddenWeapons");
			object limit = Get(s, "weightLimitFilter");
			object[] args = new object[3]
			{
				selected.GetValue(s),
				all,
				(limit is float f) ? ((object)f) : null
			};
			_filter.Invoke(null, args);
			selected.SetValue(s, args[0]);
			args = new object[3]
			{
				forbidden.GetValue(s),
				all,
				null
			};
			_filter.Invoke(null, args);
			forbidden.SetValue(s, args[0]);
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] RunAndGun normalize: " + (ex.InnerException ?? ex).Message);
		}
	}

	private static readonly string[] SimFields = new string[6] { "enableForAI", "enableForFleeChance", "accuracyPenalty", "movementPenaltyHeavy", "movementPenaltyLight", "weightLimitFilter" };

	/// <summary>Отпечаток настроек для сверки (MpFeatures): числа + содержимое списков оружия.</summary>
	public static void AppendPayload(StringBuilder payload)
	{
		object s = Settings();
		if (s == null)
		{
			return;
		}
		StringBuilder sb = new StringBuilder();
		foreach (string f in SimFields)
		{
			sb.Append(f).Append(':').Append(Convert.ToString(Get(s, f), System.Globalization.CultureInfo.InvariantCulture)).Append('|');
		}
		foreach (string list in new string[2] { "selectedWeapons", "forbiddenWeapons" })
		{
			sb.Append(list).Append('[');
			if (Get(s, list) is IDictionary dict)
			{
				List<string> keys = dict.Keys.Cast<object>().Select((object k) => k as string).Where((string k) => k != null).OrderBy((string k) => k, StringComparer.Ordinal).ToList();
				foreach (string key in keys)
				{
					object record = dict[key];
					object sel = (record == null) ? null : AccessTools.Field(record.GetType(), "isSelected")?.GetValue(record);
					sb.Append(key).Append('=').Append((sel is bool b && b) ? '1' : '0').Append(',');
				}
			}
			sb.Append(']');
		}
		payload.Append("memegoddess.runandgun=").Append(SettingsHashCheck.FnvHex(sb.ToString())).Append(';');
	}
}

/// <summary>Регистрация сборщика отпечатка для сверки настроек.</summary>
[StaticConstructorOnStartup]
public static class RunAndGunMpRegistration
{
	static RunAndGunMpRegistration()
	{
		if (RunAndGunMp.Present)
		{
			SettingsHashCheck.ExtraSettingsPayload.Add(RunAndGunMp.AppendPayload);
		}
	}
}

/// <summary>В MP перед загрузкой игры списки оружия заполняются одинаково у всех.</summary>
[HarmonyPatch]
public static class RunAndGunNormalizeOnLoad
{
	private static bool Prepare()
	{
		return RunAndGunMp.Present;
	}

	private static IEnumerable<MethodBase> TargetMethods()
	{
		yield return AccessTools.DeclaredMethod(typeof(Game), "LoadGame");
		yield return AccessTools.DeclaredMethod(typeof(Game), "InitNewGame");
	}

	private static void Prefix()
	{
		if (MpHelper.IsMultiplayer())
		{
			RunAndGunMp.Normalize();
		}
	}
}

/// <summary>Окно настроек RunAndGun во время MP-игры заблокировано.</summary>
[HarmonyPatch]
public static class RunAndGunSettingsLockMp
{
	private static bool Prepare()
	{
		return RunAndGunMp.Present;
	}

	private static MethodBase TargetMethod()
	{
		Type t = AccessTools.TypeByName("RunAndGun.RunAndGun");
		return (t == null) ? null : AccessTools.DeclaredMethod(t, "DoSettingsWindowContents", new Type[1] { typeof(Rect) });
	}

	private static bool Prefix(Rect inRect)
	{
		if (Current.ProgramState != ProgramState.Playing || !MpHelper.IsMultiplayer())
		{
			return true;
		}
		GameFont font = Text.Font;
		Text.Font = GameFont.Small;
		Widgets.Label(inRect, "[Desync Fix] " + "RAF_SimSettingsLocked".Translate("RunAndGun").Resolve());
		Text.Font = font;
		return false;
	}
}
