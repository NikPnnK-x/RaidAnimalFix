using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RaidAnimalFix;

// Replace Stuff - Continued: в Multiplayer Compatibility патча нет.
// Закрываем места, где симуляция читает состояние конкретного клиента.

/// <summary>
/// Переключатель «чертежи поверх скал» — static bool у каждого свой, а читают
/// его внутри синхронной команды строительства. В MP всегда включён, кнопка скрыта.
/// </summary>
[HarmonyPatch]
public static class ReplaceStuffOverRockToggleFix
{
	private static FieldInfo _flag;

	private static MethodBase TargetMethod()
	{
		Type type = RafTypes.Find("Replace_Stuff.OverMineable.PlaySettings_BlueprintOverRockToggle");
		_flag = (type == null) ? null : AccessTools.Field(type, "blueprintOverRock");
		return (type == null) ? null : AccessTools.Method(type, "AddButton");
	}

	private static bool Prefix()
	{
		if (!MpHelper.IsMultiplayer())
		{
			return true;
		}
		ForceOn();
		return false;
	}

	public static void ForceOn()
	{
		try
		{
			if (_flag == null)
			{
				Type type = RafTypes.Find("Replace_Stuff.OverMineable.PlaySettings_BlueprintOverRockToggle");
				_flag = (type == null) ? null : AccessTools.Field(type, "blueprintOverRock");
			}
			_flag?.SetValue(null, true);
		}
		catch
		{
		}
	}
}

/// <summary>
/// FogUtil.IsUnderFog(Thing) смотрит туман Find.CurrentMap (карта под камерой
/// клиента) и падает в виде мира. Зовётся из UnFogFix во время тика.
/// Берём туман карты самой вещи.
/// </summary>
[HarmonyPatch]
public static class ReplaceStuffFogFix
{
	private static MethodBase TargetMethod()
	{
		Type type = RafTypes.Find("Replace_Stuff.OverMineable.FogUtil");
		return (type == null) ? null : AccessTools.Method(type, "IsUnderFog", new Type[1] { typeof(Thing) });
	}

	private static bool Prefix(Thing thing, ref bool __result)
	{
		Map map = thing?.MapHeld;
		if (map == null)
		{
			return true;
		}
		FogGrid fogGrid = map.fogGrid;
		__result = false;
		foreach (IntVec3 item in GenAdj.OccupiedRect(thing.Position, thing.Rotation, thing.def.Size))
		{
			if (item.InBounds(map) && fogGrid.IsFogged(item))
			{
				__result = true;
				break;
			}
		}
		return false;
	}
}

/// <summary>
/// CancelAboveBridges.CancelAbove: чертёж/рама над мостом не сносится, если
/// он ВЫДЕЛЕН у этого клиента (Find.Selector). Зовётся из DeSpawn/Destroy —
/// в симуляции. В MP проверку выделения выкидываем.
/// </summary>
[HarmonyPatch]
public static class ReplaceStuffCancelAboveSelectorFix
{
	private static readonly MethodInfo IsSelected = AccessTools.Method(typeof(Selector), "IsSelected", new Type[1] { typeof(object) });

	private static bool Prepare()
	{
		return RafTypes.Find("Replace_Stuff.PlaceBridges.CancelAboveBridges") != null && IsSelected != null;
	}

	private static IEnumerable<MethodBase> TargetMethods()
	{
		Type type = RafTypes.Find("Replace_Stuff.PlaceBridges.CancelAboveBridges");
		if (type == null || IsSelected == null)
		{
			yield break;
		}
		// Проверка сидит в лямбде list.Do(t => ...): ищем во вложенных типах.
		foreach (Type nested in type.GetNestedTypes(AccessTools.all).Concat(new Type[1] { type }))
		{
			foreach (MethodInfo method in nested.GetMethods(AccessTools.allDeclared))
			{
				if (method.IsAbstract || method.ContainsGenericParameters)
				{
					continue;
				}
				List<KeyValuePair<OpCode, object>> body;
				try
				{
					body = PatchProcessor.ReadMethodBody(method).ToList();
				}
				catch
				{
					continue;
				}
				if (body.Any((KeyValuePair<OpCode, object> i) => i.Value is MethodInfo m && m == IsSelected))
				{
					yield return method;
				}
			}
		}
	}

	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
	{
		MethodInfo replacement = AccessTools.Method(typeof(ReplaceStuffCancelAboveSelectorFix), "IsSelectedLocalOnly");
		foreach (CodeInstruction inst in insts)
		{
			if (inst.Calls(IsSelected))
			{
				inst.opcode = OpCodes.Call;
				inst.operand = replacement;
			}
			yield return inst;
		}
	}

	public static bool IsSelectedLocalOnly(Selector selector, object obj)
	{
		if (MpHelper.IsMultiplayer())
		{
			return false;
		}
		return selector.IsSelected(obj);
	}
}

/// <summary>
/// Порядок мостов (allBridgeTerrains) у каждого свой: его меняют в настройках
/// мода (Reorder), а FindBridgeFor берёт первый подходящий. В MP запрещаем
/// Reorder и при старте игры сортируем список по индексу дефа.
/// </summary>
[HarmonyPatch]
public static class ReplaceStuffBridgeReorderFix
{
	private static MethodBase TargetMethod()
	{
		Type type = RafTypes.Find("Replace_Stuff.PlaceBridges.BridgelikeTerrain");
		return (type == null) ? null : AccessTools.Method(type, "Reorder", new Type[2]
		{
			typeof(int),
			typeof(int)
		});
	}

	private static bool Prefix()
	{
		return !MpHelper.IsMultiplayer();
	}

	public static void NormalizeOrder()
	{
		try
		{
			Type type = RafTypes.Find("Replace_Stuff.PlaceBridges.BridgelikeTerrain");
			FieldInfo fieldInfo = (type == null) ? null : AccessTools.Field(type, "allBridgeTerrains");
			if (fieldInfo?.GetValue(null) is List<TerrainDef> list)
			{
				list.SortBy((TerrainDef t) => t.index);
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] ReplaceStuff bridge order: " + ex.Message);
		}
	}
}

/// <summary>
/// DestroyedBuildings.ExposeData при СОХРАНЕНИИ выкидывает записи. Сохраняет в
/// MP не каждый клиент одновременно (локальные/ручные сохранения хоста), так
/// что потом ReviveBuilding восстанавливал счета/настройки только у части.
/// В MP сохраняем без прореживания.
/// </summary>
[HarmonyPatch]
public static class ReplaceStuffDestroyedPruneFix
{
	private static FieldInfo _dict;

	private static MethodInfo _look;

	private static MethodBase TargetMethod()
	{
		Type type = RafTypes.Find("Replace_Stuff.DestroyedRestore.DestroyedBuildings");
		if (type == null)
		{
			return null;
		}
		_dict = AccessTools.Field(type, "destroyedBuildings");
		if (_dict == null || !_dict.FieldType.IsGenericType)
		{
			return null;
		}
		Type[] args = _dict.FieldType.GetGenericArguments();
		_look = typeof(Scribe_Collections).GetMethods(BindingFlags.Static | BindingFlags.Public).FirstOrDefault((MethodInfo m) => m.Name == "Look" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2 && m.GetParameters().Length == 4 && m.GetParameters()[0].ParameterType.IsByRef && m.GetParameters()[0].ParameterType.GetElementType().IsGenericType && m.GetParameters()[0].ParameterType.GetElementType().GetGenericTypeDefinition() == typeof(Dictionary<, >))?.MakeGenericMethod(args);
		return (_look == null) ? null : AccessTools.Method(type, "ExposeData");
	}

	private static bool Prefix(object __instance)
	{
		if (!MpHelper.IsMultiplayer() || Scribe.mode != LoadSaveMode.Saving)
		{
			return true;
		}
		try
		{
			object[] array = new object[4]
			{
				_dict.GetValue(__instance),
				"destroyedBuildings",
				LookMode.Value,
				LookMode.Deep
			};
			_look.Invoke(null, array);
			_dict.SetValue(__instance, array[0]);
			return false;
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("ReplaceStuffDestroyedPruneFix", ex);
			return true;
		}
	}
}

/// <summary>
/// Настройки Replace Stuff (порядок мостов и др.) читаются в симуляции.
/// Во время MP-игры окно настроек заблокировано — менять их только до игры
/// (с включённым Sync configs у хоста).
/// </summary>
[HarmonyPatch]
public static class ReplaceStuffSettingsLockFix
{
	private static MethodBase TargetMethod()
	{
		Type type = RafTypes.Find("Replace_Stuff.Mod");
		return (type == null) ? null : AccessTools.Method(type, "DoSettingsWindowContents", new Type[1] { typeof(UnityEngine.Rect) });
	}

	private static bool Prefix(UnityEngine.Rect inRect)
	{
		if (!MpHelper.IsMultiplayer() || Current.ProgramState != ProgramState.Playing)
		{
			return true;
		}
		Widgets.Label(inRect, "[Desync Fix] " + "RAF_ReplaceStuffSettingsLocked".Translate().Resolve());
		return false;
	}
}

/// <summary>
/// DesignatorContext.designating — static-флаг, который UI ставит на время
/// CanDesignateCell и снимает после. Исключение оставляет его true только у
/// одного клиента, а симуляция читает его в проверках размещения. Снимаем в
/// финализаторе.
/// </summary>
[HarmonyPatch]
public static class ReplaceStuffDesignatingFlagFix
{
	private static FieldInfo _flag;

	private static bool Prepare()
	{
		Type type = RafTypes.Find("Replace_Stuff.DesignatorContext");
		_flag = (type == null) ? null : AccessTools.Field(type, "designating");
		return _flag != null;
	}

	private static IEnumerable<MethodBase> TargetMethods()
	{
		Type type = RafTypes.Find("Replace_Stuff.Designator_ReplaceStuff");
		MethodInfo methodInfo = (type == null) ? null : AccessTools.Method(type, "CanDesignateCell", new Type[1] { typeof(IntVec3) });
		if (methodInfo != null)
		{
			yield return methodInfo;
		}
		MethodInfo methodInfo3 = AccessTools.Method(typeof(Designator_Build), "CanDesignateCell", new Type[1] { typeof(IntVec3) });
		if (methodInfo3 != null)
		{
			yield return methodInfo3;
		}
		MethodInfo methodInfo2 = AccessTools.Method(typeof(Designator_Install), "CanDesignateCell", new Type[1] { typeof(IntVec3) });
		if (methodInfo2 != null)
		{
			yield return methodInfo2;
		}
	}

	private static void Finalizer()
	{
		try
		{
			_flag?.SetValue(null, false);
		}
		catch
		{
		}
	}
}

/// <summary>
/// MP сериализует дизайнаторы неявным воркером, который создаёт НОВЫЙ
/// экземпляр; конструктор Designator_ReplaceStuff сбрасывает материал на
/// дефолтный (дерево), и выбранный игроком stuffDef терялся у всех.
/// Свой воркер передаёт stuffDef.
/// </summary>
internal static class ReplaceStuffDesignatorSyncWorker
{
	public static void Register()
	{
		Type type = RafTypes.Find("Replace_Stuff.Designator_ReplaceStuff");
		FieldInfo stuff = (type == null) ? null : AccessTools.Field(type, "stuffDef");
		if (stuff == null)
		{
			return;
		}
		Multiplayer.API.MP.RegisterSyncWorker<Designator>(delegate(Multiplayer.API.SyncWorker sync, ref Designator designator)
		{
			if (sync.isWriting)
			{
				sync.Write(stuff.GetValue(designator) as ThingDef);
			}
			else
			{
				ThingDef value = sync.Read<ThingDef>();
				if (value != null)
				{
					stuff.SetValue(designator, value);
				}
			}
		}, type, isImplicit: true, shouldConstruct: true);
		Log.Message("[RaidAnimalFix] ReplaceStuff: материал «Заменить» синхронизируется");
	}
}
