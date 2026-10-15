using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace RaidAnimalFix;

// ANCOT LIBRARY (Ancot.AncotLibrary) + Multiplayer — библиотека Milira и других модов Ancot.
//  1. Приказы из прицела (прыжки, «стрелять очередью», job-оружие, королевские разрешения
//     «высадка бойцов»/«удар с орбиты») — у MP синхронны только типы RimWorld. → синхронны;
//     поле faction разрешений передаётся вместе с ними.
//  2. Кнопки, меняющие игру (аэрокрафт: посадка/взлёт/точка назначения; турель-вертушка:
//     снаряд/огонь/стоп; щит, ПРО, автобой мехов, перезарядка, смена оружия, вылет дронов,
//     режим работы дронов, титулы и владельцы караванов, имя оружия) выполнялись только у
//     нажавшего. → синхронны (лямбды ищутся по IL, без номеров).
//  3. Галочки/ползунки, которые гизмо пишут прямо в поля (турели: огонь по готовности и
//     цель; дроны: авторемонт и порог зарядки; заряды и автозаполнение носителей). → поля
//     синхронны, MP следит за ними на время отрисовки гизмо.
//  4. Цель турели из прицела пишется в компоненты в лямбде гизмо. → уходит командой.
//  5. Указатель посадки аэрокрафта: MP не умеет передавать его (нет конструктора без
//     параметров) — посадка в MP падала. → свой сериализатор.
//  6. Отброс снарядом-отталкивателем при нулевом векторе — UnityEngine.Random (свой у
//     каждого). → Rand игры. Стоимость торговцев Ancot без базы — по карте камеры. → нет.
//  7. Настройки (политики одежды для колонистов) — сверка и блокировка окна в MP.

internal static class AncotMp
{
	public static Type Library => RafTypes.Find("AncotLibrary.GameComponent_AncotLibrary");

	public static bool Present => Library != null;

	/// <summary>Если стоит отдельный MP-патч Ancot Library, наш модуль не дублирует его.</summary>
	public static bool Enabled => Present && !ModCompatGuards.Active(ModCompatGuards.AncotForeign);

	/// <summary>Слежение за полями: тип гизмо → компоненты, которые он пишет.</summary>
	internal static readonly Dictionary<string, MpGizmoFieldWatch> GizmoWatches = new Dictionary<string, MpGizmoFieldWatch>();
}

/// <summary>Регистрация синхронизации Ancot Library (из MpApiRegistry, порядок фиксирован).</summary>
internal static class AncotMpSync
{
	private const string Tag = "Ancot Library";

	private static readonly string[] NoNames = new string[0];

	public static void Register()
	{
		if (!AncotMp.Enabled)
		{
			return;
		}
		int n = MpTargetingSync.RegisterDeclared(AncotMp.Library.Assembly, Tag);
		RegisterPermitWorkers();
		RegisterLandDesignator();
		n += M("AncotLibrary.Building_SpinTurretGun:OrderAttack");
		n += M("AncotLibrary.CompTurretGun_Custom:ResetCurrentTarget");
		n += M("AncotLibrary.CompTurretGun_Building:ResetCurrentTarget");
		n += M("AncotLibrary.CompMechAutoFight:set_AutoFight");
		n += (MpLambdaSync.Method("AncotLibrary.WeaponTraitsUtility:SetUniqueName", debugOnly: false, Tag, new Type[2] { typeof(Thing), typeof(string) }) ? 1 : 0);
		n += Family("AncotLibrary.WorldObjectComp_AssignableToPawn_Caravan", "TryAssignPawn");
		n += Family("AncotLibrary.WorldObjectComp_AssignableToPawn_Caravan", "TryUnassignPawn");
		n += L("AncotLibrary.Building_Aerocraft", "GetGizmos", new string[4] { "FlightState", "landRotation", "targetPosition", "turningTick" }, new string[2] { "SetTargetDestination", "ConsumeFuel" });
		n += L("AncotLibrary.Building_SpinTurretGun", "GetGizmos", new string[1] { "holdFire" }, new string[2] { "ExtractShell", "ResetForcedTarget" });
		n += L("AncotLibrary.CompPhysicalShield", "GetGizmos", new string[1] { "holdShield" }, NoNames);
		n += L("AncotLibrary.CompPointDefense", "GetGizmos", new string[1] { "switchOn" }, NoNames);
		n += L("AncotLibrary.HediffComp_AISwitchCombat", "CompGetGizmos", new string[1] { "switchOn" }, NoNames);
		n += L("AncotLibrary.CompChargeWeaponImmediately", "GetGizmos", NoNames, new string[1] { "ChargeWeaponAtOnce" });
		n += L("AncotLibrary.CompMechAutoFight", "GetGizmos", NoNames, new string[3] { "set_AutoFight", "AddHediff", "RemoveHediff" });
		n += L("AncotLibrary.CompMechCarrier_Custom", "CompGetGizmosExtra", NoNames, new string[2] { "TrySpawnPawns", "TryAdd" });
		n += L("AncotLibrary.CompCommandPivot", "CompGetGizmosExtra", new string[2] { "sortie", "sortie_Terminal" }, NoNames);
		n += L("AncotLibrary.CompApparelReloadable_DeployPawn", "CompGetWornGizmosExtra", new string[2] { "sortie", "sortie_Terminal" }, NoNames);
		n += L("AncotLibrary.CompIntegrationWeaponSystem", "CompGetWornGizmosExtra", new string[1] { "activate" }, new string[1] { "SwitchGizmo" });
		n += L("AncotLibrary.CompRangeWeaponVerbSwitch", "GetWeaponGizmos", new string[2] { "switched", "verbProps" }, new string[2] { "VerbRefresh", "StopAll" });
		n += L("AncotLibrary.HediffComp_AlternateWeapon", "CompGetGizmos", NoNames, new string[3] { "TryDropEquipment", "AddToContainer", "EquipeFromStorage" });
		n += L("AncotLibrary.CompEmptyUniqueWeapon", "GetWeaponGizmos", NoNames, new string[2] { "Remove", "Add" });
		n += L("AncotLibrary.CompAdditionalGraphicSwitch", "GetGizmos", NoNames, new string[1] { "SwitchGraphicTo" });
		n += L("AncotLibrary.DroneGizmo", "GetWorkModeOptions", new string[1] { "workMode" }, new string[1] { "StopAll" });
		n += L("AncotLibrary.WorldObjectComp_RoyalTitleUpdate", "GetCaravanGizmos", NoNames, new string[1] { "TryUpdateTitle" });
		// Отладочные кнопки (видны только в режиме разработчика).
		n += L("AncotLibrary.CompDrone", "CompGetGizmosExtra", new string[1] { "powerTicksLeft" }, NoNames, debugOnly: true);
		n += L("AncotLibrary.CompThingCarrier_Custom", "CompGetGizmosExtra", new string[1] { "stackCount" }, new string[2] { "TryAdd", "ClearAndDestroyContents" }, debugOnly: true);
		n += L("AncotLibrary.CompMechCarrier_Custom", "CompGetGizmosExtra", new string[1] { "cooldownTicksRemaining" }, NoNames, debugOnly: true);
		n += L("AncotLibrary.CompSpawnerCustom", "CompGetGizmosExtra", NoNames, new string[2] { "ResetCountdown", "TryDoSpawn" }, debugOnly: true);
		n += L("AncotLibrary.CompSpawnerCustom_PlantGrow", "CompGetGizmosExtra", new string[1] { "growTicks" }, NoNames, debugOnly: true);
		n += L("AncotLibrary.CompApparelReloadable_Custom", "CompGetWornGizmosExtra", new string[1] { "remainingCharges" }, NoNames, debugOnly: true);
		n += L("AncotLibrary.CompGravitationalTrap", "CompGetGizmosExtra", new string[1] { "countdownTicksLeft" }, NoNames, debugOnly: true);
		n += L("AncotLibrary.CompRepulsiveTrap", "CompGetGizmosExtra", new string[1] { "countdownTicksLeft" }, NoNames, debugOnly: true);
		n += L("AncotLibrary.Building_DroneCharger", "GetGizmos", NoNames, new string[1] { "set_CurLevelPercentage" }, debugOnly: true);
		n += (MpLambdaSync.Method("AncotLibrary.CompDelayedPawnSpawnOnWakeup:Spawn", debugOnly: true, Tag) ? 1 : 0);
		// Поля, которые гизмо пишут напрямую.
		string[] turret = new string[3] { "fireAtWill", "forcedTarget", "isForcetargetDowned" };
		Watch("AncotLibrary.Gizmo_TurretGun", new MpGizmoFieldWatch("AncotLibrary.CompTurretGun_Custom", turret, null, Tag));
		Watch("AncotLibrary.Gizmo_TurretGunBuilding", new MpGizmoFieldWatch("AncotLibrary.CompTurretGun_Building", turret, null, Tag));
		Watch("AncotLibrary.DroneGizmo", new MpGizmoFieldWatch("AncotLibrary.CompDrone", new string[2] { "autoRepair", "PercentRecharge" }, new string[1] { "PercentRecharge" }, Tag));
		Watch("AncotLibrary.Gizmo_ApparelReloadable_Custom", new MpGizmoFieldWatch("AncotLibrary.CompApparelReloadable_Custom", new string[1] { "targetCharges" }, new string[1] { "targetCharges" }, Tag));
		MpGizmoFieldWatch carrier = new MpGizmoFieldWatch("AncotLibrary.CompThingCarrier_Custom", new string[1] { "maxToFill" }, new string[1] { "maxToFill" }, Tag);
		Watch("AncotLibrary.ThingCarrierGizmo", carrier);
		Watch("AncotLibrary.MechCarrierGizmo_Custom", carrier);
		Log.Message("[RaidAnimalFix] Ancot Library: " + n + " действий синхронизировано, полей гизмо: " + AncotMp.GizmoWatches.Count);
	}

	private static int M(string typeColonMethod)
	{
		return MpLambdaSync.Method(typeColonMethod, debugOnly: false, Tag) ? 1 : 0;
	}

	private static int L(string type, string parent, string[] fields, string[] methods, bool debugOnly = false)
	{
		return MpLambdaSync.Register(type, parent, fields, methods, debugOnly, Tag);
	}

	private static void Watch(string gizmoType, MpGizmoFieldWatch watch)
	{
		if (watch.Ready && RafTypes.Find(gizmoType) != null)
		{
			AncotMp.GizmoWatches[gizmoType] = watch;
		}
	}

	/// <summary>Виртуальный метод базового типа и его переопределения во всех модах.</summary>
	private static int Family(string baseTypeName, string method)
	{
		Type baseType = RafTypes.Find(baseTypeName);
		if (baseType == null)
		{
			return 0;
		}
		int n = 0;
		foreach (Type t in new Type[1] { baseType }.Concat(baseType.AllSubclasses().OrderBy((Type x) => x.FullName, StringComparer.Ordinal)))
		{
			MethodInfo m = AccessTools.DeclaredMethod(t, method);
			if (m != null && !m.IsAbstract && MpLambdaSync.Method(m, debugOnly: false, Tag) != null)
			{
				n++;
			}
		}
		return n;
	}

	/// <summary>Разрешения Ancot хранят фракцию-дарителя в своём поле — передаём его вместе с работником.</summary>
	private static void RegisterPermitWorkers()
	{
		foreach (string name in new string[2] { "AncotLibrary.RoyalTitlePermitWorker_DropPawn_join", "AncotLibrary.RoyalTitlePermitWorker_DropSkyFaller" })
		{
			Type t = RafTypes.Find(name);
			FieldInfo faction = (t == null) ? null : AccessTools.Field(t, "faction");
			if (faction == null || !typeof(RoyalTitlePermitWorker_Targeted).IsAssignableFrom(t))
			{
				continue;
			}
			try
			{
				MP.RegisterSyncWorker<RoyalTitlePermitWorker_Targeted>(delegate(SyncWorker sync, ref RoyalTitlePermitWorker_Targeted worker)
				{
					if (sync.isWriting)
					{
						sync.Write(faction.GetValue(worker) as Faction);
						return;
					}
					Faction value = sync.Read<Faction>();
					if (worker != null)
					{
						faction.SetValue(worker, value);
					}
				}, t, isImplicit: true);
			}
			catch (Exception ex)
			{
				Log.Warning("[RaidAnimalFix] " + Tag + ": permit worker " + name + ": " + ex.Message);
			}
		}
	}

	/// <summary>5. Указатель посадки: вещь, её def, поворот и метод-действие (лямбда аэрокрафта).</summary>
	private static void RegisterLandDesignator()
	{
		Type t = RafTypes.Find("AncotLibrary.Designator_Land");
		if (t == null)
		{
			return;
		}
		FieldInfo thingField = AccessTools.Field(t, "thing");
		FieldInfo defField = AccessTools.Field(t, "thingDef");
		FieldInfo rotField = AccessTools.Field(t, "placingRot");
		FieldInfo actionField = AccessTools.Field(t, "action");
		ConstructorInfo ctor = AccessTools.Constructor(t, new Type[4]
		{
			typeof(Texture),
			typeof(Thing),
			typeof(ThingDef),
			typeof(Action<IntVec3, Rot4>)
		});
		if (thingField == null || defField == null || rotField == null || actionField == null || ctor == null)
		{
			Log.Warning("[RaidAnimalFix] " + Tag + ": Designator_Land changed, landing is not synced (mod updated?)");
			return;
		}
		try
		{
			MP.RegisterSyncWorker<Designator>(delegate(SyncWorker sync, ref Designator designator)
			{
				if (sync.isWriting)
				{
					sync.Write(thingField.GetValue(designator) as Thing);
					sync.Write(defField.GetValue(designator) as ThingDef);
					sync.Write((Rot4)rotField.GetValue(designator));
					Delegate action = actionField.GetValue(designator) as Delegate;
					Thing owner = action?.Target as Thing;
					sync.Write(owner);
					sync.Write((owner != null) ? action.Method.Name : string.Empty);
					return;
				}
				Thing thing = sync.Read<Thing>();
				ThingDef def = sync.Read<ThingDef>();
				Rot4 rot = sync.Read<Rot4>();
				Thing target = sync.Read<Thing>();
				string method = sync.Read<string>();
				Action<IntVec3, Rot4> act = null;
				if (target != null && !method.NullOrEmpty())
				{
					MethodInfo m = AccessTools.Method(target.GetType(), method, new Type[2]
					{
						typeof(IntVec3),
						typeof(Rot4)
					});
					if (m != null && !m.IsStatic)
					{
						act = Delegate.CreateDelegate(typeof(Action<IntVec3, Rot4>), target, m, throwOnBindFailure: false) as Action<IntVec3, Rot4>;
					}
				}
				if (def == null)
				{
					designator = null;
					return;
				}
				designator = (Designator)ctor.Invoke(new object[4] { def.uiIcon, thing, def, act });
				rotField.SetValue(designator, rot);
			}, t, isImplicit: true);
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] " + Tag + ": Designator_Land worker: " + ex.Message);
		}
	}
}

/// <summary>3. На время отрисовки гизмо MP следит за полями компонентов, которые оно пишет.</summary>
[HarmonyPatch]
public static class AncotGizmoFieldWatchMp
{
	private static readonly string[] Gizmos = new string[6] { "AncotLibrary.Gizmo_TurretGun", "AncotLibrary.Gizmo_TurretGunBuilding", "AncotLibrary.DroneGizmo", "AncotLibrary.Gizmo_ApparelReloadable_Custom", "AncotLibrary.ThingCarrierGizmo", "AncotLibrary.MechCarrierGizmo_Custom" };

	private static bool Prepare()
	{
		return AncotMp.Enabled;
	}

	private static IEnumerable<MethodBase> TargetMethods()
	{
		foreach (string name in Gizmos)
		{
			Type t = RafTypes.Find(name);
			MethodInfo m = (t == null) ? null : AccessTools.DeclaredMethod(t, "GizmoOnGUI");
			if (m != null)
			{
				yield return m;
			}
		}
	}

	private static void Prefix(object __instance, MethodBase __originalMethod, out bool __state)
	{
		__state = false;
		if (!MpHelper.IsMultiplayer() || !AncotMp.GizmoWatches.TryGetValue(__originalMethod.DeclaringType.FullName, out MpGizmoFieldWatch watch))
		{
			return;
		}
		MP.WatchBegin();
		__state = true;
		watch.WatchReachable(__instance);
	}

	private static Exception Finalizer(Exception __exception, bool __state)
	{
		if (__state)
		{
			MP.WatchEnd();
		}
		return __exception;
	}
}

/// <summary>4. Цель турели из прицела: вместо записи в компоненты у одного клиента — команда.</summary>
[HarmonyPatch]
public static class AncotTurretTargetMp
{
	private static readonly Dictionary<Type, FieldInfo[]> TargetFields = new Dictionary<Type, FieldInfo[]>();

	private static bool Prepare()
	{
		return AncotMp.Enabled;
	}

	private static IEnumerable<MethodBase> TargetMethods()
	{
		foreach (string gizmo in new string[2] { "AncotLibrary.Gizmo_TurretGun", "AncotLibrary.Gizmo_TurretGunBuilding" })
		{
			foreach (MpLambdaSync.Match m in MpLambdaSync.Find(gizmo, "ProcessTargetingInput", new string[1] { "forcedTarget" }, new string[0]))
			{
				yield return m.method;
			}
		}
	}

	private static bool Prefix(object __instance, object[] __args)
	{
		if (!MpUi.Now() || __args == null || __args.Length < 1 || !(__args[0] is LocalTargetInfo target))
		{
			return true;
		}
		object gizmo = (__instance is Gizmo) ? __instance : AccessTools.Field(__instance.GetType(), "<>4__this")?.GetValue(__instance);
		List<ThingComp> comps = new List<ThingComp>();
		if (gizmo != null)
		{
			foreach (FieldInfo fi in AccessTools.GetDeclaredFields(gizmo.GetType()))
			{
				if (!fi.IsStatic && fi.GetValue(gizmo) is IEnumerable list && !(list is string))
				{
					foreach (object item in list)
					{
						if (item is ThingComp comp && !comps.Contains(comp))
						{
							comps.Add(comp);
						}
					}
				}
			}
		}
		if (comps.Count == 0)
		{
			return true;
		}
		SetTurretTarget(comps, target);
		return false;
	}

	[SyncMethod]
	public static void SetTurretTarget(List<ThingComp> comps, LocalTargetInfo target)
	{
		if (comps == null)
		{
			return;
		}
		foreach (ThingComp comp in comps)
		{
			if (comp == null)
			{
				continue;
			}
			FieldInfo[] f = FieldsFor(comp.GetType());
			if (f == null)
			{
				continue;
			}
			f[0].SetValue(comp, target);
			f[1].SetValue(comp, target);
			if (target.Pawn != null && target.Pawn.Downed)
			{
				f[2].SetValue(comp, true);
			}
		}
	}

	private static FieldInfo[] FieldsFor(Type t)
	{
		if (!TargetFields.TryGetValue(t, out FieldInfo[] f))
		{
			FieldInfo cur = AccessTools.Field(t, "currentTarget");
			FieldInfo forced = AccessTools.Field(t, "forcedTarget");
			FieldInfo downed = AccessTools.Field(t, "isForcetargetDowned");
			f = (cur != null && forced != null && downed != null && cur.FieldType == typeof(LocalTargetInfo) && forced.FieldType == typeof(LocalTargetInfo) && downed.FieldType == typeof(bool)) ? new FieldInfo[3] { cur, forced, downed } : null;
			TargetFields[t] = f;
		}
		return f;
	}
}

/// <summary>6. Отброс при нулевом векторе — направление из Rand игры, а не UnityEngine.Random.</summary>
[HarmonyPatch]
public static class AncotRepulsiveDirectionMp
{
	private static bool Prepare()
	{
		return AncotMp.Enabled;
	}

	private static MethodBase TargetMethod()
	{
		return RafTypes.Method("AncotLibrary.Projectile_Repulsive:TryToKnockBack");
	}

	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo unity = AccessTools.PropertyGetter(typeof(UnityEngine.Random), "onUnitSphere");
		int n = 0;
		foreach (CodeInstruction ci in instructions)
		{
			if (ci.Calls(unity))
			{
				ci.operand = AccessTools.Method(typeof(AncotRepulsiveDirectionMp), "Direction");
				n++;
			}
			yield return ci;
		}
		if (n == 0)
		{
			Log.Warning("[RaidAnimalFix] Ancot Library TryToKnockBack: UnityEngine.Random not found (mod updated?)");
		}
	}

	public static Vector3 Direction()
	{
		if (!MpHelper.IsMultiplayer())
		{
			return UnityEngine.Random.onUnitSphere;
		}
		float angle = Rand.Range(0f, 360f) * ((float)Math.PI / 180f);
		return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
	}
}

/// <summary>6. Ассортимент торговцев Ancot без базы игрока — не по карте камеры клиента.</summary>
[HarmonyPatch]
public static class AncotTraderStockMapMp
{
	private static bool Prepare()
	{
		return AncotMp.Enabled;
	}

	private static MethodBase TargetMethod()
	{
		Type t = RafTypes.Find("AncotLibrary.ThingSetMaker_TraderStockCustom");
		return (t == null) ? null : AccessTools.DeclaredMethod(t, "Generate");
	}

	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
	{
		return MpSimMap.Rewrite(instructions, __originalMethod, "Ancot Library");
	}
}

/// <summary>7. Окно настроек Ancot Library во время MP-игры заблокировано.</summary>
[HarmonyPatch]
public static class AncotSettingsLockMp
{
	private static bool Prepare()
	{
		return AncotMp.Present;
	}

	private static MethodBase TargetMethod()
	{
		Type t = RafTypes.Find("AncotLibrary.Mod_AncotLibrary");
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
		Widgets.Label(inRect, "[Desync Fix] " + "RAF_SimSettingsLocked".Translate("Ancot Library").Resolve());
		Text.Font = font;
		return false;
	}
}
