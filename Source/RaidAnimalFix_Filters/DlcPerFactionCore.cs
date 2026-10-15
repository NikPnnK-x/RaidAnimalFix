using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RaidAnimalFix;

/// <summary>
/// Общие помощники фичи DlcPerFaction. В MP multifaction тик мира идёт с
/// Faction.OfPlayer = наблюдатель (spectator), а у DLC много логики, которая
/// в тике мира смотрит на «игрока» или хранит один кулдаун на всех. Здесь —
/// проверка режима (MP + multifaction), выбор фракции контекста, перенос
/// контекста фракции (как MpSim/LongEventContextFix) и подмена ванильных
/// полей значениями конкретной фракции.
/// </summary>
internal static class DlcPf
{
	private static bool _resolved;

	private static FieldInfo _gameField;

	private static FieldInfo _gameCompField;

	private static FieldInfo _worldCompField;

	private static FieldInfo _multifactionField;

	private static FieldInfo _factionDataField;

	private static FieldInfo _fwdStorytellerField;

	private static void Resolve()
	{
		if (_resolved)
		{
			return;
		}
		_resolved = true;
		try
		{
			Type mp = RafTypes.Find("Multiplayer.Client.Multiplayer");
			_gameField = (mp == null) ? null : mp.GetField("game", BindingFlags.Static | BindingFlags.Public);
			Type gameType = _gameField?.FieldType;
			if (gameType != null)
			{
				_gameCompField = gameType.GetField("gameComp", BindingFlags.Instance | BindingFlags.Public);
				_worldCompField = gameType.GetField("worldComp", BindingFlags.Instance | BindingFlags.Public);
			}
			_multifactionField = _gameCompField?.FieldType.GetField("multifaction", BindingFlags.Instance | BindingFlags.Public);
			_factionDataField = _worldCompField?.FieldType.GetField("factionData", BindingFlags.Instance | BindingFlags.Public);
			Type fwd = RafTypes.Find("Multiplayer.Client.FactionWorldData");
			_fwdStorytellerField = (fwd == null) ? null : fwd.GetField("storyteller", BindingFlags.Instance | BindingFlags.Public);
			Log.Message("[RaidAnimalFix] DlcPerFaction: multifaction=" + (_multifactionField != null) + " factionData=" + (_factionDataField != null) + " storyteller=" + (_fwdStorytellerField != null));
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] DlcPerFaction resolve failed: " + ex.Message);
		}
	}

	/// <summary>Multiplayer.GameComp.multifaction (синхронное поле сейва MP).</summary>
	public static bool Multifaction()
	{
		Resolve();
		if (_gameField == null || _gameCompField == null || _multifactionField == null)
		{
			return false;
		}
		try
		{
			object game = _gameField.GetValue(null);
			object comp = (game == null) ? null : _gameCompField.GetValue(game);
			return comp != null && (bool)_multifactionField.GetValue(comp);
		}
		catch
		{
			return false;
		}
	}

	/// <summary>Фича работает только в MP с несколькими фракциями игроков.</summary>
	public static bool Active
	{
		get
		{
			try
			{
				if (!MP.IsInMultiplayer)
				{
					return false;
				}
			}
			catch
			{
				if (!MpHelper.IsMultiplayer())
				{
					return false;
				}
			}
			return Multifaction();
		}
	}

	/// <summary>Storyteller фракции из MultiplayerWorldComp.factionData (без переключения контекста).</summary>
	public static Storyteller StorytellerOf(Faction f)
	{
		Resolve();
		if (f == null || _gameField == null || _worldCompField == null || _factionDataField == null || _fwdStorytellerField == null)
		{
			return null;
		}
		try
		{
			object game = _gameField.GetValue(null);
			object wc = (game == null) ? null : _worldCompField.GetValue(game);
			IDictionary dict = (wc == null) ? null : (_factionDataField.GetValue(wc) as IDictionary);
			if (dict == null || !dict.Contains(f.loadID))
			{
				return null;
			}
			object data = dict[f.loadID];
			return (data == null) ? null : (_fwdStorytellerField.GetValue(data) as Storyteller);
		}
		catch
		{
			return null;
		}
	}

	public static bool IsReal(Faction f)
	{
		return f != null && MpContext.IsRealPlayerFaction(f);
	}

	/// <summary>Контекст наблюдателя: тик мира (или любой контекст без реальной фракции игрока).</summary>
	public static bool InSpectatorContext()
	{
		return !IsReal(Faction.OfPlayerSilentFail);
	}

	/// <summary>Faction.OfPlayer, если это реальный игрок (карта/команда/UI) — иначе null.</summary>
	public static Faction ContextFaction()
	{
		Faction f = Faction.OfPlayerSilentFail;
		return IsReal(f) ? f : null;
	}

	/// <summary>Первый реальный кандидат, иначе фракция контекста.</summary>
	public static Faction Prefer(Faction first)
	{
		return IsReal(first) ? first : ContextFaction();
	}

	/// <summary>Фракция контекста, иначе запасной кандидат.</summary>
	public static Faction ContextOr(Faction fallback)
	{
		Faction f = ContextFaction();
		if (f != null)
		{
			return f;
		}
		return IsReal(fallback) ? fallback : null;
	}

	/// <summary>Реальная фракция игрока с наименьшим loadID (детерминированно на всех клиентах).</summary>
	public static Faction FirstRealPlayerFaction()
	{
		Faction best = null;
		List<Faction> all = Find.FactionManager.AllFactionsListForReading;
		for (int i = 0; i < all.Count; i++)
		{
			Faction f = all[i];
			if (IsReal(f) && (best == null || f.loadID < best.loadID))
			{
				best = f;
			}
		}
		return best;
	}

	public static Faction FactionById(int id)
	{
		if (id < 0)
		{
			return null;
		}
		List<Faction> all = Find.FactionManager.AllFactionsListForReading;
		for (int i = 0; i < all.Count; i++)
		{
			if (all[i].loadID == id)
			{
				return all[i];
			}
		}
		return null;
	}

	/// <summary>
	/// Фракция игрока, «отвечающая» за карту: владелец карты, иначе реальная
	/// фракция с наименьшим loadID среди гуманоидов на карте (караван на чужом
	/// сайте). Без Faction.OfPlayer — работает и в тике мира.
	/// </summary>
	public static Faction PlayerFactionForMap(Map map)
	{
		if (map == null)
		{
			return null;
		}
		Faction parent = map.ParentFaction;
		if (IsReal(parent))
		{
			return parent;
		}
		Faction best = null;
		IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
		for (int i = 0; i < pawns.Count; i++)
		{
			Pawn p = pawns[i];
			Faction f = p.Faction;
			if (f != null && p.RaceProps != null && p.RaceProps.Humanlike && IsReal(f) && (best == null || f.loadID < best.loadID))
			{
				best = f;
			}
		}
		return best;
	}

	/// <summary>
	/// Переключить контекст мира на фракцию f: Faction.OfPlayer, данные фракции
	/// мира (storyteller, research, history...) и данные фракции на всех картах.
	/// Как LongEventContextFix для мирового контекста. Пара — PopWorld.
	/// </summary>
	public static bool PushWorld(Faction f)
	{
		if (f == null || !MpSim.CanRestoreContext)
		{
			return false;
		}
		try
		{
			MpSim.PushFaction(null, f);
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("DlcPerFaction.push", ex);
			return false;
		}
		try
		{
			MpSim.SetFactionOnAllMaps(f);
		}
		catch (Exception ex2)
		{
			CaravanLeaveHelper.LogErrorThrottled("DlcPerFaction.pushMaps", ex2);
		}
		return true;
	}

	public static void PopWorld()
	{
		try
		{
			Faction restored = MpSim.PopFaction(null);
			MpSim.SetFactionOnAllMaps(restored ?? Faction.OfPlayer);
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("DlcPerFaction.pop", ex);
		}
	}

	/// <summary>
	/// Сдвиг текущих часов (часы карты в тике карты/команде карты) относительно
	/// мировых. Значения храним в мировых тиках, в ванильное поле кладём
	/// значение + сдвиг — разница «сейчас − тогда» остаётся верной в любом
	/// контексте (ваниль смешивает часы карты и мира).
	/// </summary>
	public static int TickOffset()
	{
		if (MpMapTime.TryGetWorldTicks(out var world))
		{
			return Find.TickManager.TicksGame - world;
		}
		return 0;
	}

	public static int WorldNow()
	{
		if (MpMapTime.TryGetWorldTicks(out var world))
		{
			return world;
		}
		return Find.TickManager.TicksGame;
	}

	/// <summary>Состояние подмены одного int-поля (передаётся через __state Harmony).</summary>
	public struct IntSwap
	{
		public bool on;

		public string key;

		public int saved;

		public int set;

		public int offset;
	}

	/// <summary>
	/// Подставить в ванильное поле значение фракции. Если у фракции записи ещё
	/// нет — берём общее ванильное значение (миграция старых сейвов: все
	/// фракции начинают с прежнего общего значения).
	/// </summary>
	public static IntSwap BeginInt(string key, ref int field, bool isTick)
	{
		IntSwap s = default(IntSwap);
		DlcPerFactionData data = DlcPerFactionData.Get();
		if (data == null || key == null)
		{
			return s;
		}
		s.on = true;
		s.key = key;
		s.saved = field;
		s.offset = isTick ? TickOffset() : 0;
		s.set = data.ints.TryGetValue(key, out var v) ? (v + s.offset) : s.saved;
		field = s.set;
		return s;
	}

	/// <summary>
	/// Вернуть ванильное поле; изменение (если метод его записал) сохранить
	/// за фракцией. Чтение без записи ничего не сохраняет — вызовы из UI не
	/// меняют синхронное состояние.
	/// </summary>
	public static void EndInt(IntSwap s, ref int field)
	{
		if (!s.on)
		{
			return;
		}
		int cur = field;
		field = s.saved;
		if (cur != s.set)
		{
			DlcPerFactionData data = DlcPerFactionData.Get();
			if (data != null)
			{
				data.ints[s.key] = cur - s.offset;
			}
		}
	}
}

/// <summary>
/// Сохраняемые значения DLC «по фракциям» (ключи — loadID фракций и defName).
/// Тики хранятся в мировых тиках.
/// </summary>
public class DlcPerFactionData : WorldComponent
{
	/// <summary>
	/// Скалярные значения: "resettle:fid", "psyRitual:fid", "bossLast:fid",
	/// "orbital:fid", "aid:empireId:fid".
	/// </summary>
	public Dictionary<string, int> ints = new Dictionary<string, int>();

	/// <summary>Кулдауны психических ритуалов: fid → (ритуал → мировой тик доступности).</summary>
	public Dictionary<int, Dictionary<PsychicRitualDef, int>> psyCooldowns = new Dictionary<int, Dictionary<PsychicRitualDef, int>>();

	/// <summary>Счётчики вызовов боссгрупп: fid → (боссгруппа → число вызовов).</summary>
	public Dictionary<int, Dictionary<BossgroupDef, int>> bossCalls = new Dictionary<int, Dictionary<BossgroupDef, int>>();

	private Dictionary<string, int> psyFlat;

	private Dictionary<string, int> bossFlat;

	public DlcPerFactionData(World world)
		: base(world)
	{
	}

	public static DlcPerFactionData Get()
	{
		World world = Find.World;
		if (world == null)
		{
			return null;
		}
		try
		{
			return world.GetComponent<DlcPerFactionData>();
		}
		catch
		{
			return null;
		}
	}

	public override void ExposeData()
	{
		base.ExposeData();
		if (Scribe.mode == LoadSaveMode.Saving)
		{
			psyFlat = Flatten(psyCooldowns);
			bossFlat = Flatten(bossCalls);
		}
		Scribe_Collections.Look(ref ints, "rafPfInts", LookMode.Value, LookMode.Value);
		Scribe_Collections.Look(ref psyFlat, "rafPfPsyCooldowns", LookMode.Value, LookMode.Value);
		Scribe_Collections.Look(ref bossFlat, "rafPfBossCalls", LookMode.Value, LookMode.Value);
		if (Scribe.mode == LoadSaveMode.Saving)
		{
			psyFlat = null;
			bossFlat = null;
		}
		if (Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			if (ints == null)
			{
				ints = new Dictionary<string, int>();
			}
			psyCooldowns = Unflatten<PsychicRitualDef>(psyFlat);
			bossCalls = Unflatten<BossgroupDef>(bossFlat);
			psyFlat = null;
			bossFlat = null;
		}
	}

	// Ключ "fid|defName": Scribe не умеет вложенные словари с Def-ключами.
	private static Dictionary<string, int> Flatten<T>(Dictionary<int, Dictionary<T, int>> src) where T : Def
	{
		Dictionary<string, int> result = new Dictionary<string, int>();
		if (src == null)
		{
			return result;
		}
		List<int> ids = new List<int>(src.Keys);
		ids.Sort();
		foreach (int id in ids)
		{
			Dictionary<T, int> inner = src[id];
			if (inner == null)
			{
				continue;
			}
			foreach (KeyValuePair<T, int> kv in inner)
			{
				if (kv.Key != null)
				{
					result[id + "|" + kv.Key.defName] = kv.Value;
				}
			}
		}
		return result;
	}

	private static Dictionary<int, Dictionary<T, int>> Unflatten<T>(Dictionary<string, int> flat) where T : Def
	{
		Dictionary<int, Dictionary<T, int>> result = new Dictionary<int, Dictionary<T, int>>();
		if (flat == null)
		{
			return result;
		}
		List<string> keys = new List<string>(flat.Keys);
		keys.Sort(StringComparer.Ordinal);
		foreach (string key in keys)
		{
			int bar = key.IndexOf('|');
			if (bar <= 0 || !int.TryParse(key.Substring(0, bar), out var id))
			{
				continue;
			}
			T def = DefDatabase<T>.GetNamedSilentFail(key.Substring(bar + 1));
			if (def == null)
			{
				continue;
			}
			if (!result.TryGetValue(id, out var inner))
			{
				inner = new Dictionary<T, int>();
				result[id] = inner;
			}
			inner[def] = flat[key];
		}
		return result;
	}
}
