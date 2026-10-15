using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace RaidAnimalFix;

/// <summary>
/// AsyncTimeComp ставит карту на паузу, если хоть одна сессия на ней
/// IsCurrentlyPausing(map): формирование каравана, загрузка транспортёров,
/// ритуал, торговля, момент взросления. Гость, открывший такое окно на карте
/// хозяина, останавливает чужую колонию на всё время. Отвечаем false, когда
/// карта принадлежит реальному игроку, а сессия — другому реальному игроку.
/// Фракция сессии берётся из её синхронных полей, решение одинаково у всех.
/// </summary>
[HarmonyPatch]
public static class GuestSessionNoPause
{
	private static readonly string[] SessionTypes = new string[6] { "Multiplayer.Client.CaravanFormingSession", "Multiplayer.Client.TransporterLoading", "Multiplayer.Client.MpTradeSession", "Multiplayer.Client.Persistent.RitualSession", "Multiplayer.Client.Persistent.PsychicRitualSession", "Multiplayer.Client.Persistent.GrowthMomentSession" };

	private static readonly Dictionary<Type, Func<object, Faction>> _getters = new Dictionary<Type, Func<object, Faction>>();

	private static bool Prepare()
	{
		foreach (MethodBase item in TargetMethods())
		{
			_ = item;
			return true;
		}
		return false;
	}

	private static IEnumerable<MethodBase> TargetMethods()
	{
		for (int i = 0; i < SessionTypes.Length; i++)
		{
			Type t = RafTypes.Find(SessionTypes[i]);
			if (!(t == null))
			{
				MethodInfo m = AccessTools.DeclaredMethod(t, "IsCurrentlyPausing", new Type[1] { typeof(Map) });
				if (m != null)
				{
					yield return m;
				}
			}
		}
	}

	private static void Postfix(object __instance, Map map, ref bool __result)
	{
		if (!__result || map == null || __instance == null)
		{
			return;
		}
		try
		{
			Faction owner = map.ParentFaction;
			if (owner == null || !owner.IsPlayer || !MpContext.IsRealPlayerFaction(owner))
			{
				return;
			}
			Faction f = SessionFaction(__instance);
			if (f != null && f != owner && MpContext.IsRealPlayerFaction(f))
			{
				__result = false;
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("GuestSessionNoPause", ex);
		}
	}

	/// <summary>Фракция, открывшая сессию: поле faction / NegotiatorFaction / организатор ритуала / пешка.</summary>
	public static Faction SessionFaction(object session)
	{
		Type t = session.GetType();
		if (!_getters.TryGetValue(t, out var getter))
		{
			getter = BuildGetter(t);
			_getters[t] = getter;
		}
		return getter?.Invoke(session);
	}

	private static Func<object, Faction> BuildGetter(Type t)
	{
		FieldInfo faction = AccessTools.Field(t, "faction");
		if (faction != null && typeof(Faction).IsAssignableFrom(faction.FieldType))
		{
			return (object s) => faction.GetValue(s) as Faction;
		}
		PropertyInfo neg = AccessTools.Property(t, "NegotiatorFaction");
		if (neg != null)
		{
			return (object s) => neg.GetValue(s, null) as Faction;
		}
		FieldInfo data = AccessTools.Field(t, "data");
		if (data != null)
		{
			FieldInfo organizer = AccessTools.Field(data.FieldType, "organizer");
			if (organizer != null)
			{
				return delegate(object s)
				{
					object d = data.GetValue(s);
					return (d == null) ? null : (organizer.GetValue(d) as Pawn)?.Faction;
				};
			}
		}
		PropertyInfo pawn = AccessTools.Property(t, "Pawn");
		if (pawn != null)
		{
			return delegate(object s)
			{
				try
				{
					return (pawn.GetValue(s, null) as Pawn)?.Faction;
				}
				catch
				{
					return null;
				}
			};
		}
		return null;
	}
}

/// <summary>
/// MultiplayerMapComp.CreateCaravanFormingSession / CreateTransporterLoadingSession
/// возвращают первую сессию этого типа на карте, даже чужой фракции, и игрок
/// молча получает окно другого игрока (кнопки MP отключает). Вернуть null нельзя:
/// вызывающий код MP (StartFormingCaravan / DialogLoadTransportersCtorPatch) сразу
/// зовёт OpenWindow() и упал бы. Поэтому только объясняем: сообщение отправителю
/// команды (IsExecutingSyncCommandIssuedBySelf — локальное решение показа).
/// </summary>
[HarmonyPatch]
public static class GuestForeignSessionNotice
{
	private static bool Prepare()
	{
		return RafTypes.Find("Multiplayer.Client.MultiplayerMapComp") != null;
	}

	private static IEnumerable<MethodBase> TargetMethods()
	{
		Type t = RafTypes.Find("Multiplayer.Client.MultiplayerMapComp");
		if (!(t == null))
		{
			MethodInfo a = AccessTools.DeclaredMethod(t, "CreateCaravanFormingSession");
			if (a != null)
			{
				yield return a;
			}
			MethodInfo b = AccessTools.DeclaredMethod(t, "CreateTransporterLoadingSession");
			if (b != null)
			{
				yield return b;
			}
		}
	}

	private static void Postfix(MethodBase __originalMethod, Faction faction, object __result)
	{
		if (__result == null || faction == null)
		{
			return;
		}
		try
		{
			Faction other = GuestSessionNoPause.SessionFaction(__result);
			if (other == null || other == faction)
			{
				return;
			}
			bool show;
			try
			{
				show = MP.IsExecutingSyncCommandIssuedBySelf || MP.InInterface;
			}
			catch
			{
				show = false;
			}
			if (show)
			{
				string key = (__originalMethod.Name == "CreateCaravanFormingSession") ? "RAF_Relations_ForeignCaravanSessionBusy" : "RAF_Relations_ForeignTransporterSessionBusy";
				Messages.Message("[RaidAnimalFix] " + key.Translate(other.Name), MessageTypeDefOf.RejectInput, historical: false);
			}
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("GuestForeignSessionNotice", ex);
		}
	}
}
