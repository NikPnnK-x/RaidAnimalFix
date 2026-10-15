using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace RaidAnimalFix;

// VEHICLE FRAMEWORK (SmashPhil.VehicleFramework) + Multiplayer.
// Multiplayer Compatibility синхронизирует у VF загрузку груза, полёты, турели и
// одиночную посадку, но не формирование каравана. По таблице совместимости MP
// (09.2026): меню каравана пустое, караван с техникой рассинхронит или не уходит,
// групповая посадка пешек в машину рассинхронит. Причины:
//  1. VF строит вкладки окна каравана и объект формирования только при ПЕРВОМ
//     открытии окна (thisWindowInstanceEverOpened == false), а окно сессии MP
//     (CaravanFormingProxy) всегда открывается с этим флагом = true → вкладок нет,
//     формирования нет (кнопка «Отправить» падает).
//  2. Отправка каравана с техникой идёт по собственному пути VF прямо из
//     интерфейса (высадка пешек, StartFormingCaravan / ExitMapAndCreateVehicleCaravan)
//     — мимо синхронной отправки сессии MP, только у нажавшего.
//  3. Рассадка по местам (CaravanHelper.assignedSeats) — static, заполняется в окне,
//     а читается и симуляцией (вес/вместимость/заметность каравана, работы сбора,
//     лорд формирования копирует её). → две копии: интерфейса и симуляции
//     (MpSimStateSwap); в симуляцию рассадка попадает командой отправки.
//  4. Групповая посадка: места выбираются и резервируются прямо из меню, синхронно
//     уходит только сам приказ. → вся групповая посадка — синхронной командой.
// Всё через рефлексию; ни на VF, ни на MP ссылок нет.

internal static class VfMp
{
	private static bool _resolved;

	private static bool _present;

	public static FieldInfo Formation;

	public static ConstructorInfo FormationCtor;

	public static PropertyInfo FormationDialog;

	public static MethodInfo CreateTabList;

	public static MethodInfo SetInitialTab;

	public static MethodInfo InitVehicleAssignments;

	public static FieldInfo TabsList;

	public static FieldInfo DialogMap;

	public static MethodInfo LocalTryForm;

	public static MethodInfo ReformInstantly;

	public static Type LeaveDisplayClass;

	public static MethodInfo LeaveLambda;

	public static FieldInfo LeaveFormCaravan;

	public static FieldInfo AssignedSeats;

	public static Type VehicleAssignmentType;

	public static MethodInfo SetAssignment;

	public static PropertyInfo AllAssignments;

	public static MethodInfo ClearAssignments;

	public static ConstructorInfo SeatCtor;

	public static FieldInfo SeatPawn;

	public static FieldInfo SeatHandler;

	public static FieldInfo Handlers;

	public static FieldInfo HandlerVehicle;

	public static Type VehiclePawnType;

	public static Type BoardDisplayClass;

	public static MethodInfo BoardOrderPawns;

	public static FieldInfo BoardVehicle;

	public static FieldInfo BoardPawns;

	public static Type ProxyType;

	public static Type SessionType;

	public static MethodInfo PrepareDummy;

	private static MethodInfo _mpComp;

	private static FieldInfo _sessionManager;

	private static PropertyInfo _allSessions;

	private static MethodInfo _removeSession;

	public static Type T(string name)
	{
		return RafTypes.Find(name);
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
			VehiclePawnType = T("Vehicles.VehiclePawn");
			_present = VehiclePawnType != null;
			if (!_present)
			{
				return;
			}
			BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
			Type formation = T("Vehicles.World.CaravanFormation");
			Type formationInfo = T("Vehicles.World.FormationInfo");
			Type patch = T("Vehicles.Patch_FormCaravanDialog");
			Formation = (formation == null) ? null : AccessTools.Field(formation, "formation");
			FormationCtor = (formationInfo == null) ? null : AccessTools.Constructor(formationInfo, new Type[2]
			{
				typeof(Dialog_FormCaravan),
				typeof(Map)
			});
			FormationDialog = (formationInfo == null) ? null : AccessTools.Property(formationInfo, "Dialog");
			if (patch != null)
			{
				CreateTabList = AccessTools.Method(patch, "CreateTabListPostOpen");
				SetInitialTab = AccessTools.Method(patch, "SetInitialTab");
				InitVehicleAssignments = AccessTools.Method(patch, "InitVehicleAssignments");
			}
			TabsList = AccessTools.Field(typeof(Dialog_FormCaravan), "tabsList");
			DialogMap = AccessTools.Field(typeof(Dialog_FormCaravan), "map");
			if (formation != null)
			{
				foreach (MethodInfo m in formation.GetMethods(all))
				{
					if (m.Name.Contains("g__TryFormAndSendCaravan") && m.IsStatic && m.GetParameters().Length == 0 && m.ReturnType == typeof(bool))
					{
						LocalTryForm = m;
					}
				}
				ReformInstantly = AccessTools.Method(formation, "ReformInstantly", Type.EmptyTypes);
				foreach (Type nt in formation.GetNestedTypes(all))
				{
					FieldInfo fc = AccessTools.Field(nt, "formCaravan");
					if (fc == null || fc.FieldType != typeof(Dialog_FormCaravan))
					{
						continue;
					}
					foreach (MethodInfo m in nt.GetMethods(all))
					{
						if (m.Name.Contains("TryShowConfirmLeaveVehiclesDialog") && m.GetParameters().Length == 0 && m.ReturnType == typeof(void))
						{
							LeaveDisplayClass = nt;
							LeaveLambda = m;
							LeaveFormCaravan = fc;
						}
					}
				}
			}
			Type helper = T("Vehicles.CaravanHelper");
			AssignedSeats = (helper == null) ? null : AccessTools.Field(helper, "assignedSeats");
			VehicleAssignmentType = T("Vehicles.VehicleAssignment");
			if (VehicleAssignmentType != null)
			{
				SetAssignment = AccessTools.Method(VehicleAssignmentType, "SetAssignment");
				AllAssignments = AccessTools.Property(VehicleAssignmentType, "AllAssignments");
				ClearAssignments = AccessTools.Method(VehicleAssignmentType, "Clear", Type.EmptyTypes);
			}
			Type seat = T("Vehicles.AssignedSeat");
			Type handler = T("Vehicles.VehicleRoleHandler");
			if (seat != null && handler != null)
			{
				SeatCtor = AccessTools.Constructor(seat, new Type[2]
				{
					typeof(Pawn),
					handler
				});
				SeatPawn = AccessTools.Field(seat, "pawn");
				SeatHandler = AccessTools.Field(seat, "handler");
				HandlerVehicle = AccessTools.Field(handler, "vehicle");
			}
			Handlers = AccessTools.Field(VehiclePawnType, "handlers");
			foreach (Type nt in VehiclePawnType.GetNestedTypes(all))
			{
				foreach (MethodInfo m in nt.GetMethods(all))
				{
					if (m.Name.Contains("g__OrderPawns") && m.GetParameters().Length == 0 && !m.IsStatic)
					{
						FieldInfo v = AccessTools.Field(nt, "<>4__this");
						FieldInfo p = AccessTools.Field(nt, "pawns");
						if (v != null && p != null && typeof(List<Pawn>).IsAssignableFrom(p.FieldType))
						{
							BoardDisplayClass = nt;
							BoardOrderPawns = m;
							BoardVehicle = v;
							BoardPawns = p;
						}
					}
				}
			}
			ProxyType = T("Multiplayer.Client.CaravanFormingProxy");
			SessionType = T("Multiplayer.Client.CaravanFormingSession");
			PrepareDummy = (SessionType == null) ? null : AccessTools.Method(SessionType, "PrepareDummyDialog");
			Type ext = T("Multiplayer.Client.Extensions");
			_mpComp = (ext == null) ? null : AccessTools.Method(ext, "MpComp", new Type[1] { typeof(Map) });
			Type mmc = T("Multiplayer.Client.MultiplayerMapComp");
			_sessionManager = (mmc == null) ? null : AccessTools.Field(mmc, "sessionManager");
			Type sm = T("Multiplayer.Client.Persistent.SessionManager");
			if (sm != null)
			{
				_allSessions = AccessTools.Property(sm, "AllSessions");
				_removeSession = AccessTools.Method(sm, "RemoveSession");
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] VfMp resolve: " + ex.Message);
		}
	}

	public static bool Present
	{
		get
		{
			Resolve();
			return _present;
		}
	}

	public static bool IsProxy(object dialog)
	{
		Resolve();
		return dialog != null && ProxyType != null && ProxyType.IsInstanceOfType(dialog);
	}

	/// <summary>Окно формирования, которое VF сейчас считает своим (только интерфейс).</summary>
	public static Dialog_FormCaravan CurrentDialog()
	{
		Resolve();
		object f = Formation?.GetValue(null);
		return (f == null || FormationDialog == null) ? null : (FormationDialog.GetValue(f, null) as Dialog_FormCaravan);
	}

	public static object GetSession(Map map, out object manager)
	{
		Resolve();
		manager = null;
		if (map == null || _mpComp == null || _sessionManager == null || _allSessions == null || SessionType == null)
		{
			return null;
		}
		object comp = _mpComp.Invoke(null, new object[1] { map });
		manager = (comp == null) ? null : _sessionManager.GetValue(comp);
		if (manager == null || !(_allSessions.GetValue(manager, null) is IEnumerable sessions))
		{
			return null;
		}
		foreach (object s in sessions)
		{
			if (SessionType.IsInstanceOfType(s))
			{
				return s;
			}
		}
		return null;
	}

	public static void RemoveSession(object manager, object session)
	{
		Resolve();
		if (manager != null && session != null)
		{
			_removeSession?.Invoke(manager, new object[1] { session });
		}
	}

	/// <summary>Рассадка из копии интерфейса — тремя списками (пешка, машина, номер места).</summary>
	public static void CaptureSeats(out List<int> pawns, out List<int> vehicles, out List<int> handlers)
	{
		Resolve();
		pawns = new List<int>();
		vehicles = new List<int>();
		handlers = new List<int>();
		object assignment = AssignedSeats?.GetValue(null);
		if (!(((assignment == null) ? null : AllAssignments?.GetValue(assignment, null)) is IDictionary dict))
		{
			return;
		}
		foreach (DictionaryEntry e in dict)
		{
			Pawn pawn = e.Key as Pawn;
			object handler = (e.Value == null) ? null : SeatHandler?.GetValue(e.Value);
			Pawn vehicle = (handler == null) ? null : (HandlerVehicle?.GetValue(handler) as Pawn);
			IList list = (vehicle == null) ? null : (Handlers?.GetValue(vehicle) as IList);
			int index = list?.IndexOf(handler) ?? (-1);
			if (pawn != null && vehicle != null && index >= 0)
			{
				pawns.Add(pawn.thingIDNumber);
				vehicles.Add(vehicle.thingIDNumber);
				handlers.Add(index);
			}
		}
	}

	private static Pawn FindPawn(Map map, int id)
	{
		IReadOnlyList<Pawn> all = map.mapPawns.AllPawns;
		for (int i = 0; i < all.Count; i++)
		{
			if (all[i].thingIDNumber == id)
			{
				return all[i];
			}
		}
		return null;
	}

	/// <summary>В симуляции: записать рассадку в текущую (симуляционную) копию.</summary>
	public static void ApplySeats(Map map, List<int> pawns, List<int> vehicles, List<int> handlers)
	{
		Resolve();
		object assignment = AssignedSeats?.GetValue(null);
		if (assignment == null || SetAssignment == null || SeatCtor == null || pawns == null || vehicles == null || handlers == null)
		{
			return;
		}
		ClearAssignments?.Invoke(assignment, null);
		int n = Math.Min(pawns.Count, Math.Min(vehicles.Count, handlers.Count));
		for (int i = 0; i < n; i++)
		{
			Pawn pawn = FindPawn(map, pawns[i]);
			Pawn vehicle = FindPawn(map, vehicles[i]);
			IList list = (vehicle == null) ? null : (Handlers?.GetValue(vehicle) as IList);
			if (pawn == null || list == null || handlers[i] < 0 || handlers[i] >= list.Count)
			{
				continue;
			}
			SetAssignment.Invoke(assignment, new object[1] { SeatCtor.Invoke(new object[2]
			{
				pawn,
				list[handlers[i]]
			}) });
		}
	}

	public static void ClearCurrentSeats()
	{
		Resolve();
		object assignment = AssignedSeats?.GetValue(null);
		if (assignment != null)
		{
			ClearAssignments?.Invoke(assignment, null);
		}
	}

	public static bool AnySelectedVehicleGone(Dialog_FormCaravan dialog)
	{
		if (dialog?.transferables == null)
		{
			return false;
		}
		foreach (TransferableOneWay t in dialog.transferables)
		{
			if (t != null && t.CountToTransfer > 0 && t.AnyThing is Pawn p && VehiclePawnType.IsInstanceOfType(p) && !p.Spawned)
			{
				return true;
			}
		}
		return false;
	}
}

/// <summary>Регистрация копии рассадки для симуляции (MpSimStateSwap).</summary>
[StaticConstructorOnStartup]
public static class VfMpSeatSwapRegistration
{
	static VfMpSeatSwapRegistration()
	{
		try
		{
			if (VfMp.Present && VfMp.AssignedSeats != null && VfMp.VehicleAssignmentType != null)
			{
				Type t = VfMp.VehicleAssignmentType;
				MpSimStateSwap.Register(VfMp.AssignedSeats, () => Activator.CreateInstance(t));
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] VF seat swap registration: " + ex.Message);
		}
	}
}

/// <summary>1. Окно сессии MP: построить вкладки и объект формирования VF, которые VF строит только при первом открытии.</summary>
[HarmonyPatch(typeof(Dialog_FormCaravan), "PostOpen")]
public static class VfFormCaravanProxyTabs
{
	private static bool Prepare()
	{
		return VfMp.Present && VfMp.CreateTabList != null && VfMp.Formation != null && VfMp.TabsList != null && VfMp.DialogMap != null;
	}

	private static void Postfix(Dialog_FormCaravan __instance)
	{
		if (!VfMp.IsProxy(__instance) || !MpHelper.IsMultiplayer())
		{
			return;
		}
		try
		{
			if (VfMp.CurrentDialog() == __instance)
			{
				return;
			}
			List<TabRecord> tabs = VfMp.TabsList.GetValue(null) as List<TabRecord>;
			if (tabs == null)
			{
				return;
			}
			tabs.Clear();
			VfMp.Formation.SetValue(null, null);
			Map map = VfMp.DialogMap.GetValue(__instance) as Map;
			VfMp.CreateTabList.Invoke(null, new object[4] { __instance, map, tabs, false });
			VfMp.InitVehicleAssignments?.Invoke(null, new object[1] { __instance });
			VfMp.SetInitialTab?.Invoke(null, new object[1] { __instance });
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] VF caravan tabs (MP): " + (ex.InnerException ?? ex).Message);
		}
	}
}

/// <summary>2a. «Сформировать и отправить» караван с техникой → синхронная команда.</summary>
[HarmonyPatch]
public static class VfVehicleCaravanSendMp
{
	private static bool Prepare()
	{
		return VfMp.Present && VfMp.LocalTryForm != null && VfMp.PrepareDummy != null && VfMp.FormationCtor != null;
	}

	private static MethodBase TargetMethod()
	{
		return VfMp.LocalTryForm;
	}

	private static bool Prefix(ref bool __result)
	{
		if (!MpHelper.IsMultiplayer() || MpSim.InSim())
		{
			return true;
		}
		Dialog_FormCaravan dialog = VfMp.CurrentDialog();
		if (!VfMp.IsProxy(dialog))
		{
			return true;
		}
		VfMp.CaptureSeats(out List<int> pawns, out List<int> vehicles, out List<int> handlers);
		VfMpSync.VehicleCaravan(VfMp.DialogMap.GetValue(dialog) as Map, 0, pawns, vehicles, handlers);
		__result = true;
		return false;
	}
}

/// <summary>2b. Мгновенное переформирование каравана с техникой → синхронная команда.</summary>
[HarmonyPatch]
public static class VfVehicleCaravanReformMp
{
	private static bool Prepare()
	{
		return VfMp.Present && VfMp.ReformInstantly != null && VfMp.PrepareDummy != null && VfMp.FormationCtor != null;
	}

	private static MethodBase TargetMethod()
	{
		return VfMp.ReformInstantly;
	}

	private static bool Prefix()
	{
		if (!MpHelper.IsMultiplayer() || MpSim.InSim())
		{
			return true;
		}
		Dialog_FormCaravan dialog = VfMp.CurrentDialog();
		if (!VfMp.IsProxy(dialog))
		{
			return true;
		}
		VfMp.CaptureSeats(out List<int> pawns, out List<int> vehicles, out List<int> handlers);
		VfMpSync.VehicleCaravan(VfMp.DialogMap.GetValue(dialog) as Map, 1, pawns, vehicles, handlers);
		dialog.Close(doCloseSound: false);
		return false;
	}
}

/// <summary>2c. Подтверждение «оставить машины и переформировать» → синхронная команда.</summary>
[HarmonyPatch]
public static class VfVehicleCaravanLeaveVehiclesMp
{
	private static bool Prepare()
	{
		return VfMp.Present && VfMp.LeaveLambda != null && VfMp.PrepareDummy != null && VfMp.FormationCtor != null;
	}

	private static MethodBase TargetMethod()
	{
		return VfMp.LeaveLambda;
	}

	private static bool Prefix(object __instance)
	{
		if (!MpHelper.IsMultiplayer() || MpSim.InSim())
		{
			return true;
		}
		Dialog_FormCaravan dialog = VfMp.LeaveFormCaravan.GetValue(__instance) as Dialog_FormCaravan;
		if (!VfMp.IsProxy(dialog))
		{
			return true;
		}
		VfMp.CaptureSeats(out List<int> pawns, out List<int> vehicles, out List<int> handlers);
		VfMpSync.VehicleCaravan(VfMp.DialogMap.GetValue(dialog) as Map, 2, pawns, vehicles, handlers);
		dialog.Close(doCloseSound: false);
		return false;
	}
}

/// <summary>4. Групповая посадка пешек в машину (меню по клику на машину) → синхронная команда.</summary>
[HarmonyPatch]
public static class VfBoardGroupMp
{
	private static bool Prepare()
	{
		return VfMp.Present && VfMp.BoardOrderPawns != null;
	}

	private static MethodBase TargetMethod()
	{
		return VfMp.BoardOrderPawns;
	}

	private static bool Prefix(object __instance)
	{
		if (!MpHelper.IsMultiplayer() || MpSim.InSim())
		{
			return true;
		}
		Pawn vehicle = VfMp.BoardVehicle.GetValue(__instance) as Pawn;
		List<Pawn> pawns = VfMp.BoardPawns.GetValue(__instance) as List<Pawn>;
		if (vehicle == null || pawns == null)
		{
			return true;
		}
		VfMpSync.BoardGroup(vehicle, new List<Pawn>(pawns));
		return false;
	}
}

/// <summary>Синхронные команды MP для Vehicle Framework.</summary>
public static class VfMpSync
{
	/// <summary>
	/// mode: 0 — сформировать и отправить, 1 — переформировать сразу, 2 — переформировать,
	/// оставив невыбранные машины. Выполняется у всех на «пустом» окне сессии MP, как
	/// это делает сам MP для ванильной отправки.
	/// </summary>
	[SyncMethod]
	public static void VehicleCaravan(Map map, int mode, List<int> seatPawns, List<int> seatVehicles, List<int> seatHandlers)
	{
		if (map == null || !VfMp.Present)
		{
			return;
		}
		object session = VfMp.GetSession(map, out object manager);
		if (session == null)
		{
			return;
		}
		object previous = VfMp.Formation.GetValue(null);
		try
		{
			Dialog_FormCaravan dummy = VfMp.PrepareDummy.Invoke(session, null) as Dialog_FormCaravan;
			if (dummy == null)
			{
				return;
			}
			VfMp.Formation.SetValue(null, VfMp.FormationCtor.Invoke(new object[2] { dummy, map }));
			VfMp.ApplySeats(map, seatPawns, seatVehicles, seatHandlers);
			bool ok = false;
			switch (mode)
			{
			case 0:
				ok = VfMp.LocalTryForm != null && (bool)VfMp.LocalTryForm.Invoke(null, null);
				break;
			case 1:
				VfMp.ReformInstantly?.Invoke(null, null);
				ok = VfMp.AnySelectedVehicleGone(dummy);
				break;
			case 2:
				if (VfMp.LeaveDisplayClass != null)
				{
					object closure = Activator.CreateInstance(VfMp.LeaveDisplayClass);
					VfMp.LeaveFormCaravan.SetValue(closure, dummy);
					VfMp.LeaveLambda.Invoke(closure, null);
					ok = VfMp.AnySelectedVehicleGone(dummy);
				}
				break;
			}
			if (ok)
			{
				VfMp.RemoveSession(manager, session);
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] VF vehicle caravan (MP, mode " + mode + "): " + (ex.InnerException ?? ex));
		}
		finally
		{
			VfMp.ClearCurrentSeats();
			VfMp.Formation.SetValue(null, previous);
		}
	}

	[SyncMethod]
	public static void BoardGroup(Pawn vehicle, List<Pawn> pawns)
	{
		if (vehicle == null || !vehicle.Spawned || pawns == null || VfMp.BoardDisplayClass == null || !VfMp.VehiclePawnType.IsInstanceOfType(vehicle))
		{
			return;
		}
		List<Pawn> valid = pawns.Where((Pawn p) => p != null && p.Spawned && p.Map == vehicle.Map && p.Faction == vehicle.Faction).ToList();
		if (valid.Count == 0)
		{
			return;
		}
		try
		{
			object closure = Activator.CreateInstance(VfMp.BoardDisplayClass);
			VfMp.BoardVehicle.SetValue(closure, vehicle);
			VfMp.BoardPawns.SetValue(closure, valid);
			VfMp.BoardOrderPawns.Invoke(closure, null);
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] VF group boarding (MP): " + (ex.InnerException ?? ex).Message);
		}
	}
}
