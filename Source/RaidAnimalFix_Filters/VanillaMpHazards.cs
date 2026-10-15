using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RaidAnimalFix;

/// <summary>
/// Доступ к контексту симуляции MP через рефлексию (Ticking / ExecutingCmds /
/// MapContext, PushFaction/PopFaction, TimeSnapshot).
/// </summary>
internal static class MpSim
{
	private static bool _resolved;

	private static PropertyInfo _ticking;

	private static PropertyInfo _executingCmds;

	private static PropertyInfo _mapContext;

	private static MethodInfo _pushFaction;

	private static MethodInfo _popFaction;

	private static MethodInfo _timeFromMap;

	private static MethodInfo _timeSet;

	private static Func<bool> _tickingGet;

	private static Func<bool> _executingCmdsGet;

	private static Func<Map> _mapContextGet;

	private static T Getter<T>(PropertyInfo prop) where T : class
	{
		try
		{
			MethodInfo methodInfo = prop?.GetGetMethod();
			return (methodInfo == null) ? null : (Delegate.CreateDelegate(typeof(T), methodInfo, throwOnBindFailure: false) as T);
		}
		catch
		{
			return null;
		}
	}

	private static void Resolve()
	{
		if (_resolved)
		{
			return;
		}
		_resolved = true;
		try
		{
			Type mp = AccessTools.TypeByName("Multiplayer.Client.Multiplayer");
			if (mp != null)
			{
				_ticking = AccessTools.Property(mp, "Ticking");
				_executingCmds = AccessTools.Property(mp, "ExecutingCmds");
				_mapContext = AccessTools.Property(mp, "MapContext");
				_tickingGet = Getter<Func<bool>>(_ticking);
				_executingCmdsGet = Getter<Func<bool>>(_executingCmds);
				_mapContextGet = Getter<Func<Map>>(_mapContext);
			}
			Type fe = AccessTools.TypeByName("Multiplayer.Client.Factions.FactionExtensions");
			if (fe != null)
			{
				_pushFaction = AccessTools.Method(fe, "PushFaction", new Type[3]
				{
					typeof(Map),
					typeof(Faction),
					typeof(bool)
				});
				_popFaction = AccessTools.Method(fe, "PopFaction", new Type[1] { typeof(Map) });
			}
			Type ts = AccessTools.TypeByName("Multiplayer.Client.TimeSnapshot");
			if (ts != null)
			{
				_timeFromMap = AccessTools.Method(ts, "GetAndSetFromMap", new Type[1] { typeof(Map) });
				_timeSet = AccessTools.Method(ts, "Set");
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] MpSim resolve failed: " + ex.Message);
		}
	}

	public static bool CanRestoreContext
	{
		get
		{
			Resolve();
			return _pushFaction != null && _popFaction != null && _timeFromMap != null && _timeSet != null;
		}
	}

	/// <summary>Идёт тик или выполнение команды — то есть синхронная симуляция.</summary>
	public static bool InSim()
	{
		Resolve();
		try
		{
			if (_tickingGet != null && _executingCmdsGet != null)
			{
				return _tickingGet() || _executingCmdsGet();
			}
			return (_ticking != null && (bool)_ticking.GetValue(null, null)) || (_executingCmds != null && (bool)_executingCmds.GetValue(null, null));
		}
		catch
		{
			return false;
		}
	}

	public static Map MapContext()
	{
		Resolve();
		try
		{
			if (_mapContextGet != null)
			{
				return _mapContextGet();
			}
			return (_mapContext == null) ? null : (_mapContext.GetValue(null, null) as Map);
		}
		catch
		{
			return null;
		}
	}

	public static void PushFaction(Map map, Faction f)
	{
		_pushFaction.Invoke(null, new object[3] { map, f, true });
	}

	/// <summary>Возвращает фракцию, на которую вернулся контекст (или null, если пуш был пустым).</summary>
	public static Faction PopFaction(Map map)
	{
		return _popFaction.Invoke(null, new object[1] { map }) as Faction;
	}

	private static MethodInfo _mpComp;

	private static MethodInfo _mapSetFaction;

	/// <summary>Как AsyncWorldTimeComp.PreContext: данные фракции на всех картах.</summary>
	public static void SetFactionOnAllMaps(Faction f)
	{
		if (f == null)
		{
			return;
		}
		if (_mpComp == null)
		{
			Type ext = AccessTools.TypeByName("Multiplayer.Client.Extensions");
			_mpComp = (ext == null) ? null : AccessTools.Method(ext, "MpComp", new Type[1] { typeof(Map) });
			_mapSetFaction = (_mpComp == null) ? null : AccessTools.Method(_mpComp.ReturnType, "SetFaction", new Type[1] { typeof(Faction) });
		}
		if (_mpComp == null || _mapSetFaction == null)
		{
			return;
		}
		List<Map> maps = Find.Maps;
		for (int i = 0; i < maps.Count; i++)
		{
			object comp = _mpComp.Invoke(null, new object[1] { maps[i] });
			if (comp != null)
			{
				_mapSetFaction.Invoke(comp, new object[1] { f });
			}
		}
	}

	/// <summary>&gt; 0, пока выполняется обёрнутый делегат длинного события из симуляции.</summary>
	public static int simEventDepth;

	/// <summary>Ставит часы карты; возвращает упакованный TimeSnapshot? для восстановления.</summary>
	public static object SetTimeFromMap(Map map)
	{
		return (map == null) ? null : _timeFromMap.Invoke(null, new object[1] { map });
	}

	public static void RestoreTime(object snapshot)
	{
		// Nullable<T> с значением упаковывается в сам T.
		if (snapshot != null)
		{
			_timeSet.Invoke(snapshot, null);
		}
	}
}

/// <summary>
/// Длинные события, поставленные из тика или команды (встреча каравана,
/// мирные переговоры, засада, прибытие каравана/капсул на сайт, новая
/// колония...), MP выполняет позже, вне тика — уже с Faction.OfPlayer
/// ЛОКАЛЬНОГО игрока и мировыми часами. Отсюда разная репутация, разные
/// очки угрозы, письмо только у части клиентов. MP добавляет лишь seed Rand.
/// Захватываем фракцию и карту контекста при постановке и восстанавливаем
/// их (и часы карты) при выполнении.
/// </summary>
[HarmonyPatch(typeof(LongEventHandler), "QueueLongEvent", new Type[]
{
	typeof(Action),
	typeof(string),
	typeof(bool),
	typeof(Action<Exception>),
	typeof(bool),
	typeof(bool),
	typeof(Action)
})]
public static class LongEventContextFix
{
	private static bool _logged;

	private static void Prefix(ref Action action, string textKey, ref bool doAsynchronously, ref Action callback)
	{
		try
		{
			if (!MpHelper.IsMultiplayer() || !MpSim.InSim() || !MpSim.CanRestoreContext)
			{
				return;
			}
			// Собственные события MP (точка входа, автосейв: «MpSaving» и т.п.)
			// перезагружают игру и чистят стек фракций — их не трогаем.
			if (textKey != null && textKey.StartsWith("Mp", StringComparison.Ordinal))
			{
				return;
			}
			// Фракцию/часы подменяем глобально — из рабочего потока это гонка
			// с главным. MP так же делает для команд (LongEventAlwaysSync).
			doAsynchronously = false;
			Faction faction = Faction.OfPlayer;
			Map map = MpSim.MapContext();
			Game game = Current.Game;
			if (action != null)
			{
				action = Wrap(action, faction, map, game);
			}
			if (callback != null)
			{
				callback = Wrap(callback, faction, map, game);
			}
			if (!_logged)
			{
				_logged = true;
				Log.Message("[RaidAnimalFix] LongEventContext: длинные события из симуляции выполняются в фракции и часах своего контекста");
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("LongEventContextFix", ex);
		}
	}

	// MP узнаёт «тиковое» событие по делегату MarkLongEvents.Marker в списке
	// вызова (IsTickMarked → ClientFreezePacket). Сворачивать всё в одно
	// замыкание нельзя — маркер исчезнет. Оборачиваем каждый делегат
	// списка по отдельности; маркер и делегаты самого MP оставляем как есть.
	private static Action Wrap(Action action, Faction faction, Map map, Game game)
	{
		Delegate[] list = action.GetInvocationList();
		Action result = null;
		for (int i = 0; i < list.Length; i++)
		{
			Action one = (Action)list[i];
			Type declaring = one.Method?.DeclaringType;
			string ns = declaring?.Namespace ?? "";
			bool mpOwn = ns.StartsWith("Multiplayer", StringComparison.Ordinal) && !ns.StartsWith("Multiplayer.Compat", StringComparison.Ordinal);
			result = (Action)Delegate.Combine(result, mpOwn ? one : WrapOne(one, faction, map, game));
		}
		return result;
	}

	// Зерно генератора для события: тик постановки + номер события в этом тике.
	// Событие выполняется вне тика, где Rand — «интерфейсный» и у каждого клиента свой
	// (VGE Chapter 2: экипаж вражеской платформы генерировался по-разному).
	private static int _seqTick = int.MinValue;

	private static int _seq;

	private static int NextSeed()
	{
		int tick = Find.TickManager?.TicksGame ?? 0;
		if (tick != _seqTick)
		{
			_seqTick = tick;
			_seq = 0;
		}
		return Gen.HashCombineInt(tick, ++_seq);
	}

	private static Action WrapOne(Action inner, Faction capturedFaction, Map map, Game game)
	{
		int factionId = capturedFaction?.loadID ?? (-1);
		int seed = NextSeed();
		return delegate
		{
			// Игру успели перезагрузить (точка входа, перезаход) — старые
			// фракция и карта принадлежат выброшенной игре.
			if (Current.Game != game || game == null)
			{
				inner();
				return;
			}
			Faction faction = (factionId >= 0) ? Find.FactionManager.AllFactionsListForReading.Find((Faction f) => f.loadID == factionId) : null;
			Map ctx = (map != null && !map.Disposed && Find.Maps.Contains(map)) ? map : null;
			object snapshot = null;
			bool pushed = false;
			bool allMaps = false;
			try
			{
				if (faction != null)
				{
					MpSim.PushFaction(ctx, faction);
					pushed = true;
					if (ctx == null)
					{
						// Мировой контекст: MP переключает данные фракции на всех картах.
						MpSim.SetFactionOnAllMaps(faction);
						allMaps = true;
					}
				}
				snapshot = MpSim.SetTimeFromMap(ctx);
			}
			catch (Exception ex)
			{
				CaravanLeaveHelper.LogErrorThrottled("LongEventContextFix.push", ex);
			}
			MpSim.simEventDepth++;
			Rand.PushState(seed);
			try
			{
				inner();
			}
			finally
			{
				Rand.PopState();
				MpSim.simEventDepth--;
				try
				{
					if (Current.Game == game)
					{
						MpSim.RestoreTime(snapshot);
						if (pushed)
						{
							Map popMap = (ctx != null && !ctx.Disposed && Find.Maps.Contains(ctx)) ? ctx : null;
							Faction restored = MpSim.PopFaction(popMap);
							if (allMaps)
							{
								MpSim.SetFactionOnAllMaps(restored ?? Faction.OfPlayer);
							}
						}
					}
				}
				catch (Exception ex2)
				{
					CaravanLeaveHelper.LogErrorThrottled("LongEventContextFix.pop", ex2);
				}
			}
		};
	}
}

/// <summary>
/// PlayerItemAccessibilityUtility кэширует доступные вещи по (тайл, номер
/// КАДРА), а пересборка тратит Rand (GenMath.RoundRandom). Сколько тиков
/// влезает в кадр — у каждого своё, поэтому один клиент пересобирал (и
/// тратил Rand), а другой брал кэш (возможно, собранный UI в другой фракции).
/// Зовётся из квестов торговых запросов и нищих. В симуляции: всегда
/// пересобираем, Rand изолируем.
/// </summary>
[HarmonyPatch(typeof(PlayerItemAccessibilityUtility), "CacheAccessibleThings")]
public static class PlayerItemAccessibilityCacheFix
{
	private static readonly FieldInfo FrameField = AccessTools.Field(typeof(PlayerItemAccessibilityUtility), "cachedAccessibleThingsForFrame");

	// Ключ «кто и когда собрал кэш». Пересобираем один раз на ключ, а не на
	// каждый вызов (генерация квеста зовёт утилиту сотни раз подряд).
	// Кэш, собранный UI (другая фракция, другое состояние Rand), в симуляцию
	// не попадает: флаг sim входит в ключ.
	private static long _key = long.MinValue;

	private static void Prefix(RimWorld.Planet.PlanetTile nearTile, ref bool __state)
	{
		__state = false;
		if (!MpHelper.IsMultiplayer())
		{
			return;
		}
		bool sim = MpSim.InSim() || MpSim.simEventDepth > 0;
		Map ctx = MpSim.MapContext();
		long key = ((long)Find.TickManager.TicksGame << 24) ^ ((long)(Faction.OfPlayer?.loadID ?? (-1)) << 12) ^ ((long)nearTile.tileId << 1) ^ (long)(((ctx?.uniqueID ?? (-1)) + 1) << 40) ^ (sim ? 1 : 0);
		if (key != _key)
		{
			_key = key;
			try
			{
				if (FrameField != null)
				{
					FrameField.SetValue(null, -1);
				}
			}
			catch
			{
			}
		}
		// В симуляции — seed от ключа: пересборка даёт одно и то же содержимое,
		// даже если у одного клиента её спровоцировал вклинившийся вызов из UI.
		if (sim)
		{
			Rand.PushState(Gen.HashCombineInt((int)key, (int)(key >> 32)));
		}
		else
		{
			Rand.PushState();
		}
		__state = true;
	}

	private static void Finalizer(bool __state)
	{
		if (__state)
		{
			Rand.PopState();
		}
	}
}

/// <summary>
/// Требование выкупа исключает пешек, по которым уже лежит письмо в
/// Find.LetterStack. В MP стек писем у каждого клиента свой (чужие письма
/// MP удаляет, закрытие — локально), поэтому список кандидатов и
/// TryRandomElement расходились. Ведём синхронную запись в RafWorldData.
/// </summary>
[HarmonyPatch(typeof(IncidentWorker_RansomDemand), "RandomKidnappedColonist")]
public static class RansomDemandCandidatesFix
{
	private const int DemandTicks = 60000;

	private static readonly List<Pawn> Candidates = new List<Pawn>();

	private static bool Prefix(ref Pawn __result)
	{
		if (!MpHelper.IsMultiplayer())
		{
			return true;
		}
		try
		{
			int now = WorldNow();
			Candidates.Clear();
			List<Faction> factions = Find.FactionManager.AllFactionsListForReading;
			for (int i = 0; i < factions.Count; i++)
			{
				List<Pawn> kidnapped = factions[i].kidnapped.KidnappedPawnsListForReading;
				for (int j = 0; j < kidnapped.Count; j++)
				{
					Pawn pawn = kidnapped[j];
					if (pawn.Faction == Faction.OfPlayer && pawn.RaceProps.Humanlike && !RafWorldData.RansomDemandActive(pawn, now))
					{
						Candidates.Add(pawn);
					}
				}
			}
			if (!Candidates.TryRandomElement(out __result))
			{
				__result = null;
			}
			Candidates.Clear();
			return false;
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("RansomDemandCandidatesFix", ex);
			return true;
		}
	}

	internal static int WorldNow()
	{
		int ticks;
		return MpMapTime.TryGetWorldTicks(out ticks) ? ticks : Find.TickManager.TicksGame;
	}

	internal static void Record(Pawn kidnapped)
	{
		RafWorldData.RecordRansomDemand(kidnapped, WorldNow() + DemandTicks);
	}
}

/// <summary>Запись о выкупе — в момент получения письма (симуляция, у всех).</summary>
[HarmonyPatch(typeof(LetterStack), "ReceiveLetter", new Type[]
{
	typeof(Letter),
	typeof(string),
	typeof(int),
	typeof(bool)
})]
public static class RansomDemandRecordFix
{
	private static void Prefix(Letter let, int delayTicks)
	{
		if (delayTicks > 0 || !(let is ChoiceLetter_RansomDemand choiceLetter_RansomDemand) || !MpHelper.IsMultiplayer())
		{
			return;
		}
		try
		{
			RansomDemandCandidatesFix.Record(choiceLetter_RansomDemand.kidnapped);
		}
		catch
		{
		}
	}
}

/// <summary>
/// CompSightstealer.Notify_BecameVisible выбирает «письмо или сообщение» по
/// static float реального времени. Письмо берёт номер письма, а MP ищет
/// письма для синхронного выбора по номеру — счётчики расходились. В MP
/// решаем по синхронному мировому тику из RafWorldData (60 c → 3600 тиков):
/// подставляем ванильному static такое значение, чтобы его сравнение дало
/// тот же ответ на всех машинах.
/// </summary>
[HarmonyPatch(typeof(CompSightstealer), "Notify_BecameVisible")]
public static class SightstealerNotifyRealTimeFix
{
	private const int GapTicks = 3600;

	private static readonly FieldInfo LastNotifiedField = AccessTools.Field(typeof(CompSightstealer), "lastNotified");

	private static void Prefix(ref bool __state)
	{
		__state = false;
		SightstealerLetterSent.Value = false;
		if (!MpHelper.IsMultiplayer() || LastNotifiedField == null)
		{
			return;
		}
		try
		{
			__state = RansomDemandCandidatesFix.WorldNow() - RafWorldData.SightstealerLetterTick > GapTicks;
			// letter: LastRealTime > -99999+60 → true; message: LastRealTime > LastRealTime+60 → false.
			LastNotifiedField.SetValue(null, __state ? (-99999f) : RealTime.LastRealTime);
		}
		catch
		{
		}
	}

	private static void Postfix(bool __state)
	{
		// Метку ставим только если письмо действительно ушло (ранний return,
		// когда рядом видна другая пешка-Sightstealer, письма не даёт).
		if (__state && SightstealerLetterSent.Value)
		{
			RafWorldData.SightstealerLetterTick = RansomDemandCandidatesFix.WorldNow();
		}
		SightstealerLetterSent.Value = false;
	}
}

/// <summary>Флаг «внутри Notify_BecameVisible ушло письмо Sightstealer».</summary>
internal static class SightstealerLetterSent
{
	public static bool Value;
}

[HarmonyPatch(typeof(LetterStack), "ReceiveLetter", new Type[]
{
	typeof(Letter),
	typeof(string),
	typeof(int),
	typeof(bool)
})]
public static class SightstealerLetterSentProbe
{
	private static void Prefix(Letter let)
	{
		if (let != null && let.def == LetterDefOf.ThreatBig && let.lookTargets != null && let.lookTargets.PrimaryTarget.Thing is Pawn pawn && pawn.kindDef == PawnKindDefOf.Sightstealer)
		{
			SightstealerLetterSent.Value = true;
		}
	}
}

/// <summary>
/// PitGate.Tick: «Find.CurrentMap == Map &amp;&amp; Rand.MTBEventOccurs(...)» — Rand
/// тратится только у тех, кто смотрит на эту карту. Изолируем этот вызов.
/// </summary>
[HarmonyPatch(typeof(PitGate), "Tick")]
public static class PitGateCameraRandFix
{
	private static readonly MethodInfo Mtb = AccessTools.Method(typeof(Rand), "MTBEventOccurs", new Type[3]
	{
		typeof(float),
		typeof(float),
		typeof(float)
	});

	private static readonly MethodInfo Isolated = AccessTools.Method(typeof(PitGateCameraRandFix), "IsolatedMtb");

	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
	{
		// В Tick два MTBEventOccurs: первый — тряска камеры (только у смотрящих),
		// второй — предупреждение о выходе существ (логика симуляции, не трогаем).
		int num = 0;
		foreach (CodeInstruction inst in insts)
		{
			if (inst.Calls(Mtb))
			{
				if (num == 0)
				{
					inst.operand = Isolated;
				}
				num++;
			}
			yield return inst;
		}
		if (num != 2)
		{
			Log.Warning("[RaidAnimalFix] PitGateCameraRand: ожидалось 2 вызова MTBEventOccurs в PitGate.Tick, найдено " + num + " — проверьте версию игры");
		}
	}

	public static bool IsolatedMtb(float mtb, float mtbUnit, float checkDuration)
	{
		Rand.PushState();
		try
		{
			return Rand.MTBEventOccurs(mtb, mtbUnit, checkDuration);
		}
		finally
		{
			Rand.PopState();
		}
	}
}

/// <summary>
/// CompAbilityEffect_Chunkskip кэширует найденные куски по Time.frameCount;
/// кэш заполняет и превью прицеливания (UI, туман своей фракции), а читает
/// Apply (симуляция). Перед Apply кэш сбрасываем.
/// </summary>
[HarmonyPatch(typeof(CompAbilityEffect_Chunkskip), "Apply", new Type[2]
{
	typeof(LocalTargetInfo),
	typeof(LocalTargetInfo)
})]
public static class ChunkskipFrameCacheFix
{
	private static readonly FieldInfo FrameField = AccessTools.Field(typeof(CompAbilityEffect_Chunkskip), "lastChunkUpdateFrame");

	private static void Prefix(CompAbilityEffect_Chunkskip __instance)
	{
		if (FrameField == null || __instance == null || !MpHelper.IsMultiplayer())
		{
			return;
		}
		try
		{
			FrameField.SetValue(__instance, -1);
		}
		catch
		{
		}
	}
}
