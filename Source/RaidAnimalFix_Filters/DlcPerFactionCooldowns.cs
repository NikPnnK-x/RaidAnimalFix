using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace RaidAnimalFix;

// ───────────────────────── Подмена словаря «на фракцию» ─────────────────────────

/// <summary>
/// Подмена ванильного словаря Def → int словарём фракции на время вызова
/// метода. Значения-тики хранятся в мировых тиках, в подставленную копию
/// кладутся со сдвигом текущих часов. После вызова копия сравнивается с
/// исходной: если метод ничего не менял (чтение, в том числе из UI) —
/// ничего не сохраняем.
/// </summary>
internal sealed class DlcPfDictSwap<K>
{
	public Dictionary<K, int> saved;

	public Dictionary<K, int> temp;

	private Dictionary<K, int> baseline;

	private int fid;

	private int offset;

	private bool dropZeros;

	public static DlcPfDictSwap<K> Begin(Dictionary<int, Dictionary<K, int>> store, Dictionary<K, int> current, Faction f, int offset, bool dropZeros)
	{
		DlcPfDictSwap<K> s = new DlcPfDictSwap<K>
		{
			saved = current,
			fid = f.loadID,
			offset = offset,
			dropZeros = dropZeros
		};
		// Нет записи у фракции — стартуем с общего ванильного словаря (миграция).
		if (!store.TryGetValue(s.fid, out s.baseline) || s.baseline == null)
		{
			s.baseline = current ?? new Dictionary<K, int>();
		}
		s.temp = new Dictionary<K, int>(s.baseline.Count);
		foreach (KeyValuePair<K, int> kv in s.baseline)
		{
			s.temp[kv.Key] = kv.Value + offset;
		}
		return s;
	}

	public void End(Dictionary<int, Dictionary<K, int>> store)
	{
		Dictionary<K, int> result = new Dictionary<K, int>(temp.Count);
		foreach (KeyValuePair<K, int> kv in temp)
		{
			int v = kv.Value - offset;
			if (!dropZeros || v != 0)
			{
				result[kv.Key] = v;
			}
		}
		int baseCount = 0;
		bool changed = false;
		foreach (KeyValuePair<K, int> kv2 in baseline)
		{
			if (dropZeros && kv2.Value == 0)
			{
				continue;
			}
			baseCount++;
			if (!result.TryGetValue(kv2.Key, out var v2) || v2 != kv2.Value)
			{
				changed = true;
				break;
			}
		}
		if (changed || baseCount != result.Count)
		{
			store[fid] = result;
		}
	}
}

// ───────────────────────── IdeoManager: переселение / психические ритуалы ─────────────────────────

/// <summary>
/// IdeoManager.lastResettledTick и lastPsychicRitualPerformedTick — одно
/// значение на всех: переселение одного игрока снимало «давно не
/// переселялись» у всех, ритуал одного — «нет психических ритуалов» у всех.
/// Храним по фракциям; фракция — фракция пешки (мысли) или контекста (запись).
/// </summary>
internal static class DlcPfIdeoFields
{
	public static DlcPf.IntSwap BeginResettle(Faction f)
	{
		IdeoManager im = Find.IdeoManager;
		if (f == null || im == null || !ModsConfig.OdysseyActive || !DlcPf.Active)
		{
			return default(DlcPf.IntSwap);
		}
		return DlcPf.BeginInt("resettle:" + f.loadID, ref im.lastResettledTick, isTick: true);
	}

	public static void EndResettle(DlcPf.IntSwap s)
	{
		IdeoManager im = Find.IdeoManager;
		if (s.on && im != null)
		{
			DlcPf.EndInt(s, ref im.lastResettledTick);
		}
	}

	public static DlcPf.IntSwap BeginPsyRitual(Faction f)
	{
		IdeoManager im = Find.IdeoManager;
		if (f == null || im == null || !ModsConfig.AnomalyActive || !DlcPf.Active)
		{
			return default(DlcPf.IntSwap);
		}
		return DlcPf.BeginInt("psyRitual:" + f.loadID, ref im.lastPsychicRitualPerformedTick, isTick: true);
	}

	public static void EndPsyRitual(DlcPf.IntSwap s)
	{
		IdeoManager im = Find.IdeoManager;
		if (s.on && im != null)
		{
			DlcPf.EndInt(s, ref im.lastPsychicRitualPerformedTick);
		}
	}
}

[HarmonyPatch(typeof(ThoughtWorker_Precept_NoPsychicRituals), "ShouldHaveThought")]
public static class DlcPfNoPsychicRitualsThought
{
	private static void Prefix(Pawn p, out DlcPf.IntSwap __state)
	{
		__state = default(DlcPf.IntSwap);
		try
		{
			__state = DlcPfIdeoFields.BeginPsyRitual(DlcPf.Prefer(p?.Faction));
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("DlcPfNoPsychicRitualsThought", ex);
		}
	}

	private static void Finalizer(DlcPf.IntSwap __state)
	{
		DlcPfIdeoFields.EndPsyRitual(__state);
	}
}

[HarmonyPatch(typeof(LordToil_PsychicRitual), "RitualCompleted")]
public static class DlcPfPsychicRitualPerformedWrite
{
	private static void Prefix(LordToil_PsychicRitual __instance, out DlcPf.IntSwap __state)
	{
		__state = default(DlcPf.IntSwap);
		try
		{
			__state = DlcPfIdeoFields.BeginPsyRitual(DlcPf.ContextOr(__instance.lord?.faction));
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("DlcPfPsychicRitualPerformedWrite", ex);
		}
	}

	private static void Finalizer(DlcPf.IntSwap __state)
	{
		DlcPfIdeoFields.EndPsyRitual(__state);
	}
}

[HarmonyPatch(typeof(ThoughtWorker_Precept_ResettledRecently), "ShouldHaveThought")]
public static class DlcPfResettledThought
{
	private static void Prefix(Pawn p, out DlcPf.IntSwap __state)
	{
		__state = default(DlcPf.IntSwap);
		try
		{
			__state = DlcPfIdeoFields.BeginResettle(DlcPf.Prefer(p?.Faction));
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("DlcPfResettledThought", ex);
		}
	}

	private static void Finalizer(DlcPf.IntSwap __state)
	{
		DlcPfIdeoFields.EndResettle(__state);
	}
}

/// <summary>CurrentStateInternal читает TicksSinceLastResettle (стадия мысли).</summary>
[HarmonyPatch(typeof(ThoughtWorker_Precept_ResettledRecently), "CurrentStateInternal")]
public static class DlcPfResettledThoughtStage
{
	private static void Prefix(Pawn p, out DlcPf.IntSwap __state)
	{
		__state = default(DlcPf.IntSwap);
		try
		{
			__state = DlcPfIdeoFields.BeginResettle(DlcPf.Prefer(p?.Faction));
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("DlcPfResettledThoughtStage", ex);
		}
	}

	private static void Finalizer(DlcPf.IntSwap __state)
	{
		DlcPfIdeoFields.EndResettle(__state);
	}
}

[HarmonyPatch(typeof(SettleUtility), "AddNewHome")]
public static class DlcPfResettleWriteSettle
{
	private static void Prefix(Faction faction, out DlcPf.IntSwap __state)
	{
		__state = default(DlcPf.IntSwap);
		try
		{
			__state = DlcPfIdeoFields.BeginResettle(DlcPf.Prefer(faction));
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("DlcPfResettleWriteSettle", ex);
		}
	}

	private static void Finalizer(DlcPf.IntSwap __state)
	{
		DlcPfIdeoFields.EndResettle(__state);
	}
}

[HarmonyPatch(typeof(GravshipUtility), "ArriveNewMap")]
public static class DlcPfResettleWriteGravship
{
	private static void Prefix(Gravship gravship, out DlcPf.IntSwap __state)
	{
		__state = default(DlcPf.IntSwap);
		try
		{
			__state = DlcPfIdeoFields.BeginResettle(DlcPf.ContextOr(gravship?.Faction));
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("DlcPfResettleWriteGravship", ex);
		}
	}

	private static void Finalizer(DlcPf.IntSwap __state)
	{
		DlcPfIdeoFields.EndResettle(__state);
	}
}

// ───────────────────────── Психические ритуалы: кулдауны ─────────────────────────

/// <summary>
/// GameComponent_PsychicRitualManager.ritualCooldowns — один словарь на всех:
/// ритуал одного игрока ставил кулдаун всем. Подменяем словарь словарём
/// фракции контекста (команда/карта/UI) на время каждого метода менеджера.
/// </summary>
internal static class DlcPfPsyCooldowns
{
	public static readonly AccessTools.FieldRef<GameComponent_PsychicRitualManager, Dictionary<PsychicRitualDef, int>> CdRef = AccessTools.FieldRefAccess<GameComponent_PsychicRitualManager, Dictionary<PsychicRitualDef, int>>("ritualCooldowns");

	public static DlcPfDictSwap<PsychicRitualDef> Begin(GameComponent_PsychicRitualManager m, Faction f)
	{
		if (m == null || f == null || !ModsConfig.AnomalyActive || !DlcPf.Active)
		{
			return null;
		}
		DlcPerFactionData data = DlcPerFactionData.Get();
		if (data == null)
		{
			return null;
		}
		DlcPfDictSwap<PsychicRitualDef> s = DlcPfDictSwap<PsychicRitualDef>.Begin(data.psyCooldowns, CdRef(m), f, DlcPf.TickOffset(), dropZeros: false);
		CdRef(m) = s.temp;
		return s;
	}

	public static void End(GameComponent_PsychicRitualManager m, DlcPfDictSwap<PsychicRitualDef> s)
	{
		if (s == null || m == null)
		{
			return;
		}
		CdRef(m) = s.saved;
		DlcPerFactionData data = DlcPerFactionData.Get();
		if (data != null)
		{
			s.End(data.psyCooldowns);
		}
	}
}

[HarmonyPatch]
public static class DlcPfPsyCooldownContext
{
	private static bool Prepare()
	{
		return true;
	}

	private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
	{
		Type t = typeof(GameComponent_PsychicRitualManager);
		string[] names = new string[4] { "RegisterCooldown", "ClearCooldown", "GetAvailableTick", "ClearAllCooldowns" };
		foreach (string name in names)
		{
			System.Reflection.MethodInfo m = AccessTools.Method(t, name);
			if (m != null)
			{
				yield return m;
			}
		}
	}

	private static void Prefix(GameComponent_PsychicRitualManager __instance, out DlcPfDictSwap<PsychicRitualDef> __state)
	{
		__state = null;
		try
		{
			__state = DlcPfPsyCooldowns.Begin(__instance, DlcPf.ContextFaction());
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("DlcPfPsyCooldownContext", ex);
		}
	}

	private static void Finalizer(GameComponent_PsychicRitualManager __instance, DlcPfDictSwap<PsychicRitualDef> __state)
	{
		DlcPfPsyCooldowns.End(__instance, __state);
	}
}

[HarmonyPatch(typeof(GameComponent_PsychicRitualManager), "CanInvoke")]
public static class DlcPfPsyCooldownCanInvoke
{
	private static void Prefix(GameComponent_PsychicRitualManager __instance, Map map, out DlcPfDictSwap<PsychicRitualDef> __state)
	{
		__state = null;
		try
		{
			__state = DlcPfPsyCooldowns.Begin(__instance, DlcPf.ContextOr(map?.ParentFaction));
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("DlcPfPsyCooldownCanInvoke", ex);
		}
	}

	private static void Finalizer(GameComponent_PsychicRitualManager __instance, DlcPfDictSwap<PsychicRitualDef> __state)
	{
		DlcPfPsyCooldowns.End(__instance, __state);
	}
}

/// <summary>
/// Истечение кулдаунов фракций (ваниль чистит только общий словарь в тике
/// мира). Удаление — синхронно у всех; сообщение «ритуал снова доступен»
/// показываем только локальному игроку этой фракции (historical:false, без
/// изменения состояния игры), исследование проверяем в контексте фракции.
/// </summary>
[HarmonyPatch(typeof(GameComponent_PsychicRitualManager), "GameComponentTick")]
public static class DlcPfPsyCooldownExpiry
{
	private static readonly List<int> TmpIds = new List<int>();

	private static readonly List<PsychicRitualDef> TmpDefs = new List<PsychicRitualDef>();

	private static void Postfix()
	{
		try
		{
			if (!ModsConfig.AnomalyActive || !DlcPf.Active)
			{
				return;
			}
			DlcPerFactionData data = DlcPerFactionData.Get();
			if (data == null || data.psyCooldowns.Count == 0)
			{
				return;
			}
			int now = DlcPf.WorldNow();
			TmpIds.Clear();
			TmpIds.AddRange(data.psyCooldowns.Keys);
			TmpIds.Sort();
			Faction local = null;
			bool localResolved = false;
			foreach (int fid in TmpIds)
			{
				Dictionary<PsychicRitualDef, int> cds = data.psyCooldowns[fid];
				if (cds == null)
				{
					data.psyCooldowns.Remove(fid);
					continue;
				}
				TmpDefs.Clear();
				foreach (KeyValuePair<PsychicRitualDef, int> kv in cds)
				{
					if (kv.Value <= now)
					{
						TmpDefs.Add(kv.Key);
					}
				}
				if (TmpDefs.Count == 0)
				{
					continue;
				}
				for (int i = 0; i < TmpDefs.Count; i++)
				{
					cds.Remove(TmpDefs[i]);
				}
				if (!localResolved)
				{
					localResolved = true;
					local = MpHelper.GetCurrentPlayerFaction();
				}
				Faction f = DlcPf.FactionById(fid);
				if (f != null && f == local)
				{
					NotifyLocal(f, TmpDefs);
				}
			}
			TmpDefs.Clear();
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("DlcPfPsyCooldownExpiry", ex);
		}
	}

	private static void NotifyLocal(Faction f, List<PsychicRitualDef> defs)
	{
		if (!DlcPf.PushWorld(f))
		{
			return;
		}
		List<string> labels = new List<string>();
		try
		{
			for (int i = 0; i < defs.Count; i++)
			{
				if (defs[i].researchPrerequisite != null && defs[i].researchPrerequisite.IsFinished)
				{
					labels.Add(defs[i].label);
				}
			}
		}
		finally
		{
			DlcPf.PopWorld();
		}
		for (int j = 0; j < labels.Count; j++)
		{
			Messages.Message("PsychicRitualCanBeCastAgain".Translate(labels[j]), MessageTypeDefOf.PositiveEvent, historical: false);
		}
	}
}

// ───────────────────────── Боссгруппы ─────────────────────────

/// <summary>
/// GameComponent_Bossgroup.lastBossgroupCalled и timesCalledBossgroups —
/// общие: вызов боссгруппы одним игроком блокировал вызов у всех на 2 дня и
/// повышал волну всем. Подменяем на значения фракции контекста.
/// killedBosses (побеждённые боссы) остаются общими.
/// </summary>
internal static class DlcPfBossgroup
{
	public static readonly AccessTools.FieldRef<GameComponent_Bossgroup, Dictionary<BossgroupDef, int>> CountsRef = AccessTools.FieldRefAccess<GameComponent_Bossgroup, Dictionary<BossgroupDef, int>>("timesCalledBossgroups");

	public sealed class State
	{
		public GameComponent_Bossgroup m;

		public DlcPf.IntSwap last;

		public DlcPfDictSwap<BossgroupDef> counts;
	}

	public static State Begin(GameComponent_Bossgroup m, Faction f, bool withCounts)
	{
		if (m == null || f == null || !ModsConfig.BiotechActive || !DlcPf.Active)
		{
			return null;
		}
		DlcPerFactionData data = DlcPerFactionData.Get();
		if (data == null)
		{
			return null;
		}
		State s = new State { m = m };
		s.last = DlcPf.BeginInt("bossLast:" + f.loadID, ref m.lastBossgroupCalled, isTick: true);
		if (withCounts)
		{
			s.counts = DlcPfDictSwap<BossgroupDef>.Begin(data.bossCalls, CountsRef(m), f, 0, dropZeros: true);
			CountsRef(m) = s.counts.temp;
		}
		return s;
	}

	public static void End(State s)
	{
		if (s?.m == null)
		{
			return;
		}
		DlcPf.EndInt(s.last, ref s.m.lastBossgroupCalled);
		if (s.counts != null)
		{
			CountsRef(s.m) = s.counts.saved;
			DlcPerFactionData data = DlcPerFactionData.Get();
			if (data != null)
			{
				s.counts.End(data.bossCalls);
			}
		}
	}
}

[HarmonyPatch]
public static class DlcPfBossgroupManagerContext
{
	private static bool Prepare()
	{
		return true;
	}

	private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
	{
		Type t = typeof(GameComponent_Bossgroup);
		string[] names = new string[3] { "NumTimesCalledBossgroup", "Notify_BossgroupCalled", "ResetProgress" };
		foreach (string name in names)
		{
			System.Reflection.MethodInfo m = AccessTools.Method(t, name);
			if (m != null)
			{
				yield return m;
			}
		}
	}

	private static void Prefix(GameComponent_Bossgroup __instance, out DlcPfBossgroup.State __state)
	{
		__state = null;
		try
		{
			__state = DlcPfBossgroup.Begin(__instance, DlcPf.ContextFaction(), withCounts: true);
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("DlcPfBossgroupManagerContext", ex);
		}
	}

	private static void Finalizer(DlcPfBossgroup.State __state)
	{
		DlcPfBossgroup.End(__state);
	}
}

[HarmonyPatch(typeof(BossgroupWorker), "CanResolve")]
public static class DlcPfBossgroupCanResolve
{
	private static void Prefix(Pawn caller, out DlcPfBossgroup.State __state)
	{
		__state = null;
		try
		{
			__state = DlcPfBossgroup.Begin(Find.BossgroupManager, DlcPf.Prefer(caller?.Faction), withCounts: false);
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("DlcPfBossgroupCanResolve", ex);
		}
	}

	private static void Finalizer(DlcPfBossgroup.State __state)
	{
		DlcPfBossgroup.End(__state);
	}
}

[HarmonyPatch(typeof(Command_CallBossgroup), "IsDisabled")]
public static class DlcPfBossgroupGizmo
{
	private static void Prefix(out DlcPfBossgroup.State __state)
	{
		__state = null;
		try
		{
			__state = DlcPfBossgroup.Begin(Find.BossgroupManager, DlcPf.ContextFaction(), withCounts: false);
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("DlcPfBossgroupGizmo", ex);
		}
	}

	private static void Finalizer(DlcPfBossgroup.State __state)
	{
		DlcPfBossgroup.End(__state);
	}
}

[HarmonyPatch(typeof(CompUseEffect_CallBossgroup), "ShouldSendSpawnLetter", MethodType.Getter)]
public static class DlcPfBossgroupSpawnLetter
{
	private static void Prefix(CompUseEffect_CallBossgroup __instance, out DlcPfBossgroup.State __state)
	{
		__state = null;
		try
		{
			__state = DlcPfBossgroup.Begin(Find.BossgroupManager, DlcPf.ContextOr(__instance.parent?.MapHeld?.ParentFaction), withCounts: false);
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("DlcPfBossgroupSpawnLetter", ex);
		}
	}

	private static void Finalizer(DlcPfBossgroup.State __state)
	{
		DlcPfBossgroup.End(__state);
	}
}

// ───────────────────────── Военная помощь (Faction.lastMilitaryAidRequestTick) ─────────────────────────

/// <summary>
/// Faction.lastMilitaryAidRequestTick хранится на фракции-союзнике (Империя и
/// др.) — одно значение на всех игроков: запрос помощи одним игроком
/// включал суточный кулдаун у всех. Ключ — пара (союзник, игрок).
/// </summary>
internal static class DlcPfMilitaryAid
{
	public static DlcPf.IntSwap Begin(Faction ally, Faction requester)
	{
		if (ally == null || requester == null || !DlcPf.Active)
		{
			return default(DlcPf.IntSwap);
		}
		return DlcPf.BeginInt("aid:" + ally.loadID + ":" + requester.loadID, ref ally.lastMilitaryAidRequestTick, isTick: true);
	}

	public static void End(Faction ally, DlcPf.IntSwap s)
	{
		if (s.on && ally != null)
		{
			DlcPf.EndInt(s, ref ally.lastMilitaryAidRequestTick);
		}
	}
}

[HarmonyPatch(typeof(FactionDialogMaker), "RequestMilitaryAidOption")]
public static class DlcPfMilitaryAidDialog
{
	private static void Prefix(Faction faction, Pawn negotiator, out DlcPf.IntSwap __state)
	{
		__state = default(DlcPf.IntSwap);
		try
		{
			__state = DlcPfMilitaryAid.Begin(faction, DlcPf.Prefer(negotiator?.Faction));
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("DlcPfMilitaryAidDialog", ex);
		}
	}

	private static void Finalizer(Faction faction, DlcPf.IntSwap __state)
	{
		DlcPfMilitaryAid.End(faction, __state);
	}
}

[HarmonyPatch(typeof(FactionDialogMaker), "CallForAid")]
public static class DlcPfMilitaryAidCall
{
	private static void Prefix(Map map, Faction faction, out DlcPf.IntSwap __state)
	{
		__state = default(DlcPf.IntSwap);
		try
		{
			__state = DlcPfMilitaryAid.Begin(faction, DlcPf.ContextOr(map?.ParentFaction));
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("DlcPfMilitaryAidCall", ex);
		}
	}

	private static void Finalizer(Faction faction, DlcPf.IntSwap __state)
	{
		DlcPfMilitaryAid.End(faction, __state);
	}
}

[HarmonyPatch(typeof(RoyalTitlePermitWorker_CallAid), "CallAid")]
public static class DlcPfMilitaryAidPermit
{
	private static void Prefix(Pawn caller, Faction faction, out DlcPf.IntSwap __state)
	{
		__state = default(DlcPf.IntSwap);
		try
		{
			if (ModsConfig.RoyaltyActive)
			{
				__state = DlcPfMilitaryAid.Begin(faction, DlcPf.Prefer(caller?.Faction));
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("DlcPfMilitaryAidPermit", ex);
		}
	}

	private static void Finalizer(Faction faction, DlcPf.IntSwap __state)
	{
		DlcPfMilitaryAid.End(faction, __state);
	}
}

// ───────────────────────── Орбитальный сканер ─────────────────────────

/// <summary>
/// OrbitalScannerWorldComponent: один кулдаун (18 дней) на всех и случайный
/// сканер из всех фракций. В MP multifaction: кулдаун по фракциям (мировые
/// тики — ваниль писала мировые, а читала часы карты), отдельный бросок MTB
/// для каждой фракции, сигнал получает её сканер — в контексте и часах его
/// карты (ReceiveSignal пишет locateSignalTick в часах карты, которые потом
/// сравнивает CompTick).
/// </summary>
internal static class DlcPfOrbital
{
	public static readonly AccessTools.FieldRef<OrbitalScannerWorldComponent, List<CompOrbitalScanner>> WorkingRef = AccessTools.FieldRefAccess<OrbitalScannerWorldComponent, List<CompOrbitalScanner>>("workingScanners");

	public static readonly AccessTools.FieldRef<OrbitalScannerWorldComponent, int> LegacyLastRef = AccessTools.FieldRefAccess<OrbitalScannerWorldComponent, int>("lastFoundSignal");

	private const int CooldownTicks = 1080000;

	public static bool Applies()
	{
		return DlcPf.Active && MpSim.CanRestoreContext;
	}

	public static Faction ScannerFaction(CompOrbitalScanner scanner)
	{
		Thing parent = scanner?.parent;
		if (parent == null)
		{
			return null;
		}
		if (DlcPf.IsReal(parent.Faction))
		{
			return parent.Faction;
		}
		Faction mapFaction = parent.MapHeld?.ParentFaction;
		return DlcPf.IsReal(mapFaction) ? mapFaction : null;
	}

	public static bool OnCooldown(OrbitalScannerWorldComponent comp, Faction f, int worldNow)
	{
		DlcPerFactionData data = DlcPerFactionData.Get();
		int last = (data != null && data.ints.TryGetValue("orbital:" + f.loadID, out var v)) ? v : LegacyLastRef(comp);
		return last > 0 && worldNow < last + CooldownTicks;
	}

	public static void SetFound(Faction f, int worldNow)
	{
		DlcPerFactionData data = DlcPerFactionData.Get();
		if (data != null)
		{
			data.ints["orbital:" + f.loadID] = worldNow;
		}
	}
}

[HarmonyPatch(typeof(OrbitalScannerWorldComponent), "Notify_ScannerWorking")]
public static class DlcPfOrbitalNotify
{
	private static bool Prefix(OrbitalScannerWorldComponent __instance, CompOrbitalScanner scanner)
	{
		try
		{
			if (!DlcPfOrbital.Applies())
			{
				return true;
			}
			Faction f = DlcPfOrbital.ScannerFaction(scanner);
			if (f == null)
			{
				return false;
			}
			if (!DlcPfOrbital.OnCooldown(__instance, f, DlcPf.WorldNow()))
			{
				DlcPfOrbital.WorkingRef(__instance)?.Add(scanner);
			}
			return false;
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("DlcPfOrbitalNotify", ex);
			return true;
		}
	}
}

[HarmonyPatch(typeof(OrbitalScannerWorldComponent), "WorldComponentTick")]
public static class DlcPfOrbitalTick
{
	private static readonly List<CompOrbitalScanner> TmpScanners = new List<CompOrbitalScanner>();

	private static readonly List<CompOrbitalScanner> TmpGroup = new List<CompOrbitalScanner>();

	private static readonly List<int> TmpIds = new List<int>();

	private static bool Prefix(OrbitalScannerWorldComponent __instance)
	{
		try
		{
			if (!DlcPfOrbital.Applies())
			{
				return true;
			}
			List<CompOrbitalScanner> working = DlcPfOrbital.WorkingRef(__instance);
			if (working == null || working.Count == 0)
			{
				return false;
			}
			TmpScanners.Clear();
			TmpScanners.AddRange(working);
			working.Clear();
			int now = DlcPf.WorldNow();
			TmpIds.Clear();
			for (int i = 0; i < TmpScanners.Count; i++)
			{
				Faction f = DlcPfOrbital.ScannerFaction(TmpScanners[i]);
				if (f != null && !TmpIds.Contains(f.loadID))
				{
					TmpIds.Add(f.loadID);
				}
			}
			TmpIds.Sort();
			foreach (int fid in TmpIds)
			{
				Faction f2 = DlcPf.FactionById(fid);
				if (f2 == null || DlcPfOrbital.OnCooldown(__instance, f2, now))
				{
					continue;
				}
				if (!Rand.MTBEventOccurs(60000f, 1f, 1f))
				{
					continue;
				}
				TmpGroup.Clear();
				for (int j = 0; j < TmpScanners.Count; j++)
				{
					CompOrbitalScanner s = TmpScanners[j];
					if (s?.parent != null && s.parent.Spawned && DlcPfOrbital.ScannerFaction(s) == f2)
					{
						TmpGroup.Add(s);
					}
				}
				if (TmpGroup.Count == 0)
				{
					continue;
				}
				CompOrbitalScanner chosen = TmpGroup.RandomElement();
				ReceiveInMapContext(chosen, f2);
				DlcPfOrbital.SetFound(f2, now);
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("DlcPfOrbitalTick", ex);
		}
		finally
		{
			TmpScanners.Clear();
			TmpGroup.Clear();
		}
		return false;
	}

	private static void ReceiveInMapContext(CompOrbitalScanner scanner, Faction f)
	{
		Map map = scanner.parent.Map;
		bool pushed = false;
		object snapshot = null;
		try
		{
			MpSim.PushFaction(map, f);
			pushed = true;
			snapshot = MpSim.SetTimeFromMap(map);
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("DlcPfOrbitalTick.push", ex);
		}
		try
		{
			scanner.ReceiveSignal();
		}
		finally
		{
			try
			{
				MpSim.RestoreTime(snapshot);
				if (pushed)
				{
					MpSim.PopFaction(map);
				}
			}
			catch (Exception ex2)
			{
				CaravanLeaveHelper.LogErrorThrottled("DlcPfOrbitalTick.pop", ex2);
			}
		}
	}
}
