using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace RaidAnimalFix;

// ───────────────────────── Разрешения гостя на карте другого игрока ─────────────────────────

/// <summary>
/// MP хранит обозначения/зоны/«запрещено» по фракциям, поэтому гость своим
/// десигнатором на карте хозяина спокойно рубит, копает, охотится и разбирает.
/// Ограничиваем по разрешению хозяина (allowDesignate / allowForbidToggle).
/// Faction.OfPlayer здесь — кликающий игрок: в интерфейсе MP подставляет
/// локальную фракцию, в команде — фракцию отправителя. Карта — текущая
/// (MP при выполнении команды десигнатора выставляет карту команды).
/// Зоны, области, планы и отмена — локальные для фракции, их не ограничиваем.
/// </summary>
[HarmonyPatch]
public static class GuestDesignatorPermission
{
	/// <summary>Кэш «какое разрешение нужно типу» — чистая функция от типа.</summary>
	private static readonly Dictionary<Type, int> _required = new Dictionary<Type, int>();

	private static bool Prepare()
	{
		return true;
	}

	private static IEnumerable<MethodBase> TargetMethods()
	{
		HashSet<MethodBase> seen = new HashSet<MethodBase>();
		MethodInfo baseThing = AccessTools.DeclaredMethod(typeof(Designator), "CanDesignateThing", new Type[1] { typeof(Thing) });
		if (baseThing != null && seen.Add(baseThing))
		{
			yield return baseThing;
		}
		foreach (Type t in GenTypes.AllSubclasses(typeof(Designator)))
		{
			if (t.IsGenericTypeDefinition)
			{
				continue;
			}
			MethodInfo cell = null;
			MethodInfo thing = null;
			try
			{
				cell = AccessTools.DeclaredMethod(t, "CanDesignateCell", new Type[1] { typeof(IntVec3) });
				thing = AccessTools.DeclaredMethod(t, "CanDesignateThing", new Type[1] { typeof(Thing) });
			}
			catch
			{
			}
			if (cell != null && !cell.IsAbstract && cell.ReturnType == typeof(AcceptanceReport) && seen.Add(cell))
			{
				yield return cell;
			}
			if (thing != null && !thing.IsAbstract && thing.ReturnType == typeof(AcceptanceReport) && seen.Add(thing))
			{
				yield return thing;
			}
		}
	}

	private static void Postfix(Designator __instance, ref AcceptanceReport __result)
	{
		if (!__result.Accepted || __instance == null || !RelationsUtil.InMultiplayer)
		{
			return;
		}
		try
		{
			int need = Required(__instance.GetType());
			if (need < 0)
			{
				return;
			}
			Faction guest = Faction.OfPlayer;
			if (!RelationsApi.IsGuestOn(__instance.Map, guest, out var host))
			{
				return;
			}
			if (!RelationsApi.GuestAllowed(host, guest, (RafGuestPerm)need))
			{
				__result = new AcceptanceReport(((need == (int)RafGuestPerm.ForbidToggle) ? "RAF_Relations_GuestNoForbid" : "RAF_Relations_GuestNoDesignate").Translate(host.Name));
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("GuestDesignatorPermission", ex);
		}
	}

	private static int Required(Type t)
	{
		if (_required.TryGetValue(t, out var v))
		{
			return v;
		}
		string n = t.Name;
		if (typeof(Designator_Forbid).IsAssignableFrom(t) || typeof(Designator_Unforbid).IsAssignableFrom(t))
		{
			v = (int)RafGuestPerm.ForbidToggle;
		}
		else if (typeof(Designator_Cancel).IsAssignableFrom(t) || n.StartsWith("Designator_Area", StringComparison.Ordinal) || n.StartsWith("Designator_Zone", StringComparison.Ordinal) || n.StartsWith("Designator_Plan", StringComparison.Ordinal) || typeof(Designator_Zone).IsAssignableFrom(t))
		{
			v = -1;
		}
		else
		{
			v = (int)RafGuestPerm.Designate;
		}
		_required[t] = v;
		return v;
	}
}

/// <summary>
/// Переключатель «запрещено» (гизмо, Designator_Unforbid, ForbidUtility.SetForbidden):
/// MP пишет снятие запрета в набор фракции контекста, то есть гость «разрешает»
/// вещи хозяина для своих пешек. Без разрешения хозяина гость не может снимать
/// запрет на карте хозяина. Ставить запрет можно всегда (это не даёт доступа).
/// Блокируем только действия игрока: в интерфейсе (с сообщением) и при выполнении
/// sync-команды (детерминированно, OfPlayer = отправитель). Тики не трогаем.
/// </summary>
[HarmonyPatch(typeof(CompForbiddable), "Forbidden", MethodType.Setter)]
public static class GuestForbidTogglePermission
{
	[HarmonyPriority(Priority.First)]
	private static bool Prefix(CompForbiddable __instance, bool value)
	{
		if (value || !RelationsUtil.InMultiplayer)
		{
			return true;
		}
		try
		{
			Thing t = __instance.parent;
			if (t == null || !t.Spawned)
			{
				return true;
			}
			bool inUi = MP.InInterface;
			if (!inUi && !MP.IsExecutingSyncCommand)
			{
				return true;
			}
			Faction guest = Faction.OfPlayer;
			if (!RelationsApi.IsGuestOn(t.Map, guest, out var host))
			{
				return true;
			}
			// Труп своей пешки гость может разрешить всегда — чтобы забрать/похоронить.
			if (t is Corpse corpse && corpse.InnerPawn != null && corpse.InnerPawn.Faction == guest)
			{
				return true;
			}
			if (RelationsApi.GuestAllowed(host, guest, RafGuestPerm.ForbidToggle))
			{
				return true;
			}
			if (inUi)
			{
				Messages.Message("[RaidAnimalFix] " + "RAF_Relations_GuestNoForbid".Translate(host.Name), MessageTypeDefOf.RejectInput, historical: false);
			}
			return false;
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("GuestForbidTogglePermission", ex);
			return true;
		}
	}
}

/// <summary>
/// Еда и кровати хозяина для гостей. IsForbidden(Thing, Faction) в MP смотрит
/// набор «разрешено» фракции; вещи хозяина есть только в его наборе, поэтому для
/// гостевых пешек они запрещены. Здесь запоминаем, какая пешка спрашивает
/// (IsForbidden(Thing, Pawn)), и ниже ослабляем проверку только для её фракции —
/// работа гостя (переноска, отбор в караван) от этого не меняется.
/// Решение — функция синхронного состояния (вещь, пешка, карта, разрешения).
/// </summary>
[HarmonyPatch(typeof(ForbidUtility), "IsForbidden", new Type[]
{
	typeof(Thing),
	typeof(Pawn)
})]
public static class GuestForbidPawnContext
{
	/// <summary>Транзиентный «кто спрашивает» на время вызова (не состояние игры).</summary>
	[ThreadStatic]
	public static Pawn current;

	private static void Prefix(Pawn pawn, out Pawn __state)
	{
		__state = current;
		current = null;
		if (pawn == null)
		{
			return;
		}
		Faction f = pawn.Faction;
		if (f == null || !f.IsPlayer)
		{
			return;
		}
		Map map = pawn.MapHeld;
		Faction owner = map?.ParentFaction;
		if (owner != null && owner != f && owner.IsPlayer)
		{
			current = pawn;
		}
	}

	private static void Finalizer(Pawn __state)
	{
		current = __state;
	}
}

[HarmonyPatch(typeof(ForbidUtility), "IsForbidden", new Type[]
{
	typeof(Thing),
	typeof(Faction)
})]
public static class GuestFoodBedsPermission
{
	private static void Postfix(Thing t, Faction faction, ref bool __result)
	{
		if (!__result)
		{
			return;
		}
		Pawn p = GuestForbidPawnContext.current;
		if (p == null || t == null || faction == null || faction != p.Faction || !t.Spawned)
		{
			return;
		}
		try
		{
			if (!RelationsApi.IsGuestOn(t.Map, faction, out var host))
			{
				return;
			}
			RafGuestPerm perm;
			if (t is Building_Bed)
			{
				perm = RafGuestPerm.Beds;
			}
			else
			{
				if (!IsGuestFood(t))
				{
					return;
				}
				perm = RafGuestPerm.Food;
			}
			if (!RelationsApi.GuestAllowed(host, faction, perm))
			{
				return;
			}
			// Только то, что хозяин сам считает доступным (не запрещено у него).
			bool hostForbids;
			GuestForbidPawnContext.current = null;
			try
			{
				hostForbids = t.IsForbidden(host);
			}
			finally
			{
				GuestForbidPawnContext.current = p;
			}
			if (!hostForbids)
			{
				__result = false;
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("GuestFoodBedsPermission", ex);
		}
	}

	public static bool IsGuestFood(Thing t)
	{
		if (t is Corpse || t is Pawn)
		{
			return false;
		}
		ThingDef def = t.def;
		return def != null && def.IsNutritionGivingIngestible && !def.IsDrug;
	}
}

/// <summary>
/// RestUtility.IsValidBedFor отвергает кровать, если её фракция не совпадает с
/// фракцией пешки (или её HostFaction). Для разрешённого гостя повторяем
/// остальные проверки метода, пропуская только сравнение фракций.
/// </summary>
[HarmonyPatch(typeof(RestUtility), "IsValidBedFor")]
public static class GuestBedPermission
{
	private static void Postfix(Thing bedThing, Pawn sleeper, Pawn traveler, bool checkSocialProperness, bool allowMedBedEvenIfSetToNoCare, bool ignoreOtherReservations, GuestStatus? guestStatus, ref bool __result)
	{
		if (__result || !(bedThing is Building_Bed bed) || sleeper == null || traveler == null || !RelationsUtil.InMultiplayer)
		{
			return;
		}
		try
		{
			Faction guest = traveler.Faction;
			if (guest == null || sleeper.Faction != guest || guestStatus.HasValue || sleeper.IsPrisoner || sleeper.IsSlave)
			{
				return;
			}
			if (!bed.Spawned || !RelationsApi.IsGuestOn(bed.Map, guest, out var host) || bed.Faction != host)
			{
				return;
			}
			if (!RelationsApi.GuestAllowed(host, guest, RafGuestPerm.Beds))
			{
				return;
			}
			if (!RestUtility.CanUseBedNow(bed, sleeper, checkSocialProperness, allowMedBedEvenIfSetToNoCare, guestStatus))
			{
				return;
			}
			if (!traveler.CanReach(bed, PathEndMode.OnCell, Danger.Some))
			{
				return;
			}
			if (!sleeper.HasReserved(bed) && !traveler.CanReserve(bed, bed.SleepingSlotsCount, 0, null, ignoreOtherReservations))
			{
				return;
			}
			if (traveler.HasReserved<JobDriver_TakeToBed>(bed, sleeper))
			{
				return;
			}
			if (bed.IsForbidden(traveler))
			{
				return;
			}
			if (ModsConfig.AnomalyActive && sleeper.IsMutant && sleeper.needs.rest == null && sleeper.mutant.Def.entitledToMedicalCare && !bed.Medical)
			{
				return;
			}
			__result = true;
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("GuestBedPermission", ex);
		}
	}
}

/// <summary>
/// Труп пешки игрока на чужой карте: MP при Kill пушит фракцию жертвы, и труп
/// «разрешён» только ей — хозяин карты не может его похоронить/убрать.
/// После смерти в симуляции дополнительно снимаем запрет в контексте хозяина
/// (MpSim.PushFaction(map, host) → SetForbidPatch пишет в его набор).
/// </summary>
[HarmonyPatch(typeof(Pawn), "Kill")]
public static class GuestCorpseUnforbidForOwner
{
	private static void Postfix(Pawn __instance)
	{
		if (__instance == null || !RelationsUtil.InMultiplayer)
		{
			return;
		}
		try
		{
			Corpse corpse = __instance.Corpse;
			if (corpse == null || !corpse.Spawned || !MpSim.InSim() || !MpSim.CanRestoreContext)
			{
				return;
			}
			Faction victim = __instance.Faction;
			Map map = corpse.Map;
			if (!RelationsApi.IsGuestOn(map, victim, out var host))
			{
				return;
			}
			if (corpse.IsForbidden(victim) || !corpse.IsForbidden(host))
			{
				return;
			}
			MpSim.PushFaction(map, host);
			try
			{
				corpse.SetForbidden(value: false, warnOnFail: false);
			}
			finally
			{
				MpSim.PopFaction(map);
			}
			Log.Message("[RaidAnimalFix] Relations: труп " + __instance.LabelShort + " (" + victim.Name + ") разрешён и для хозяина карты " + host.Name);
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("GuestCorpseUnforbidForOwner", ex);
		}
	}
}

/// <summary>
/// Караван другого игрока вошёл на карту: хозяину — локальное сообщение
/// (только его клиентам; RealPlayerFaction читаем лишь для решения «показать»).
/// </summary>
[HarmonyPatch(typeof(CaravanEnterMapUtility), "Enter", new Type[]
{
	typeof(Caravan),
	typeof(Map),
	typeof(Func<Pawn, IntVec3>),
	typeof(CaravanDropInventoryMode),
	typeof(bool)
})]
public static class GuestArrivalMessage
{
	private static void Prefix(Caravan caravan, out Faction __state)
	{
		__state = caravan?.Faction;
	}

	private static void Postfix(Map map, Faction __state)
	{
		if (__state == null || map == null || !RelationsUtil.InMultiplayer)
		{
			return;
		}
		try
		{
			if (!RelationsApi.IsGuestOn(map, __state, out var host))
			{
				return;
			}
			string status = RelationsUi.KindLabel(RelationsApi.Kind(host, __state));
			RelationsUtil.NotifyLocal(host, "RAF_Relations_MsgGuestArrived".Translate(__state.Name, map.Parent?.LabelCap ?? map.ToString(), status), RelationsApi.AtWar(host, __state) ? MessageTypeDefOf.ThreatBig : MessageTypeDefOf.NeutralEvent);
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("GuestArrivalMessage", ex);
		}
	}
}
