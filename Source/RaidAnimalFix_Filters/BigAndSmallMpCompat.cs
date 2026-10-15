using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace RaidAnimalFix;

// BIG AND SMALL — FRAMEWORK (RedMattis.BetterPrerequisites) + Multiplayer.
// Таблица совместимости MP: «рейды и караваны фракций с кастомными ксенотипами
// ломаются». Найденные причины:
//  1. У B&S свой счётчик тиков BS.Tick (static): не сохраняется и не сбрасывается —
//     считает тики с запуска ПРОЦЕССА. У хоста он большой (плюс одиночная игра до
//     хостинга), у подключившегося — с нуля. От него зависит «глобальное случайное
//     число» (SlowUpdate: Rand.PushState(BS.Tick / 100)), которое решает, станет ли
//     пешка в рейде инфильтратором с подменённым ксенотипом → разные рейдеры.
//     → в MP после загрузки игры счётчик = игровое время, у всех одинаково.
//  2. Очереди и расписания пересчёта кэша (static) переживали перезагрузку у старых
//     клиентов. → сбрасываются перед загрузкой.
//  3. Кэш размеров и статов (BSCache) читает симуляция, а создавать и пересчитывать его
//     мог и интерфейс (отрисовка пешки, окна): у того, кто смотрит на пешку, кэш
//     появлялся и пересчитывался в другой момент. → в интерфейсе MP кэш только
//     читается; создание, пересчёт и планирование — только в симуляции.

internal static class BigAndSmallMp
{
	public static bool Present => AccessTools.TypeByName("BigAndSmall.HumanoidPawnScaler") != null;

	private static bool _resolved;

	private static FieldInfo _cacheField;

	private static FieldInfo _default;

	private static void Resolve()
	{
		if (_resolved)
		{
			return;
		}
		_resolved = true;
		try
		{
			Type bsCache = AccessTools.TypeByName("BigAndSmall.BSCache");
			Type dict = AccessTools.TypeByName("BigAndSmall.DictCache`2");
			if (bsCache != null && dict != null)
			{
				Type closed = dict.MakeGenericType(typeof(Pawn), bsCache);
				_cacheField = AccessTools.Field(closed, "Cache");
				_default = AccessTools.Field(bsCache, "defaultCache");
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] Big and Small resolve: " + ex.Message);
		}
	}

	public static bool HasCache(Pawn pawn)
	{
		Resolve();
		// Поле Cache могут заменить целиком («сбросить кэш» в настройках) — читаем каждый раз.
		return _cacheField?.GetValue(null) is IDictionary cache && cache.Contains(pawn);
	}

	public static object DefaultCache()
	{
		Resolve();
		return _default?.GetValue(null);
	}

	/// <summary>Чистый интерфейс MP (не тик, не команда, не длинное событие).</summary>
	public static bool InUi()
	{
		try
		{
			return MpHelper.IsMultiplayer() && MP.InInterface;
		}
		catch
		{
			return false;
		}
	}
}

/// <summary>1 + 2. Счётчик тиков B&S = игровое время; очереди пересчёта сброшены.</summary>
[HarmonyPatch]
public static class BigAndSmallTickSyncOnLoad
{
	private static readonly string[] Queues = new string[6] { "refreshQueue", "frequentUpdateGenes", "queuedJobs", "schedulePostUpdate", "scheduleFullUpdate", "currentlyQueued" };

	private static MethodInfo _setTick;

	private static bool Prepare()
	{
		_setTick = AccessTools.Method("BigAndSmall.BS:SetTick", new Type[1] { typeof(int) });
		return BigAndSmallMp.Present && _setTick != null;
	}

	private static IEnumerable<MethodBase> TargetMethods()
	{
		yield return AccessTools.DeclaredMethod(typeof(Game), "LoadGame");
		yield return AccessTools.DeclaredMethod(typeof(Game), "InitNewGame");
	}

	private static void Prefix()
	{
		if (!MpHelper.IsMultiplayer())
		{
			return;
		}
		foreach (string q in Queues)
		{
			IsekaiMp.ResetStatic("BigAndSmall.BigAndSmallCache", q);
		}
	}

	private static void Postfix()
	{
		if (!MpHelper.IsMultiplayer())
		{
			return;
		}
		try
		{
			_setTick.Invoke(null, new object[1] { Find.TickManager?.TicksGame ?? 0 });
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] Big and Small tick sync: " + (ex.InnerException ?? ex).Message);
		}
	}
}

/// <summary>3a. В интерфейсе MP кэш только читается: без создания и пересчёта.</summary>
[HarmonyPatch]
public static class BigAndSmallCacheReadOnlyInUi
{
	private static bool Prepare()
	{
		return BigAndSmallMp.Present;
	}

	private static MethodBase TargetMethod()
	{
		Type t = AccessTools.TypeByName("BigAndSmall.HumanoidPawnScaler");
		if (t == null)
		{
			return null;
		}
		foreach (MethodInfo m in AccessTools.GetDeclaredMethods(t))
		{
			ParameterInfo[] ps = m.GetParameters();
			if (m.Name == "GetCache" && ps.Length >= 2 && ps[0].ParameterType == typeof(Pawn) && ps[1].Name == "forceRefresh")
			{
				return m;
			}
		}
		return null;
	}

	private static bool Prefix(Pawn pawn, ref bool forceRefresh, ref object __result)
	{
		if (pawn == null || !BigAndSmallMp.InUi())
		{
			return true;
		}
		forceRefresh = false;
		if (BigAndSmallMp.HasCache(pawn))
		{
			return true;
		}
		object def = BigAndSmallMp.DefaultCache();
		if (def == null)
		{
			return true;
		}
		__result = def;
		return false;
	}
}

/// <summary>3b. В интерфейсе MP пересчёт кэша не планируется (расписание живёт в симуляции).</summary>
[HarmonyPatch]
public static class BigAndSmallNoUiScheduling
{
	private static bool Prepare()
	{
		return BigAndSmallMp.Present;
	}

	private static MethodBase TargetMethod()
	{
		Type t = AccessTools.TypeByName("BigAndSmall.HumanoidPawnScaler");
		Type c = AccessTools.TypeByName("BigAndSmall.BSCache");
		return (t == null || c == null) ? null : AccessTools.DeclaredMethod(t, "ShedueleForceRegenerate", new Type[2]
		{
			c,
			typeof(int)
		});
	}

	private static bool Prefix()
	{
		return !BigAndSmallMp.InUi();
	}
}
