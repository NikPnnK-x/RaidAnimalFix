using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace RaidAnimalFix;

// Общие инструменты для модулей совместимости модов (Milira, Ancot Library, …).

/// <summary>Чистый интерфейс MP (клик игрока), а не тик и не исполнение команды.</summary>
internal static class MpUi
{
	public static bool Now()
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

/// <summary>
/// Карта для кода симуляции, который по ошибке берёт Find.CurrentMap (карту, на которую
/// смотрит клиент). В одиночной игре — как было. В MP: карта текущего контекста
/// (тикающая карта / карта команды), иначе — одна из баз игроков, выбранная одинаково у
/// всех (по номеру карты и игровому дню, без Rand).
/// </summary>
internal static class MpSimMap
{
	public static Map Current()
	{
		if (!MpHelper.IsMultiplayer())
		{
			return Find.CurrentMap;
		}
		Map context = MpSim.MapContext();
		if (context != null)
		{
			return context;
		}
		List<Map> maps = Find.Maps;
		if (maps == null || maps.Count == 0)
		{
			return null;
		}
		List<Map> homes = new List<Map>();
		foreach (Map m in maps)
		{
			if (!m.IsPocketMap && m.ParentFaction != null && m.ParentFaction.IsPlayer)
			{
				homes.Add(m);
			}
		}
		List<Map> pool = (homes.Count > 0) ? homes : new List<Map>(maps);
		pool.Sort((Map a, Map b) => a.uniqueID.CompareTo(b.uniqueID));
		int day = Math.Max(0, Find.TickManager.TicksGame / 60000);
		return pool[day % pool.Count];
	}

	/// <summary>Заменить вызовы Find.CurrentMap на MpSimMap.Current() в теле метода.</summary>
	public static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions, MethodBase original, string tag)
	{
		MethodInfo from = AccessTools.PropertyGetter(typeof(Find), "CurrentMap");
		MethodInfo to = AccessTools.Method(typeof(MpSimMap), "Current");
		int n = 0;
		foreach (CodeInstruction ci in instructions)
		{
			if (ci.Calls(from))
			{
				ci.opcode = OpCodes.Call;
				ci.operand = to;
				n++;
			}
			yield return ci;
		}
		if (n == 0)
		{
			Log.Warning("[RaidAnimalFix] " + tag + ": Find.CurrentMap not found in " + original?.DeclaringType?.Name + "." + original?.Name + " (mod updated?)");
		}
	}
}

/// <summary>
/// MP синхронизирует OrderForceTarget (приказ «атаковать/прыгнуть сюда» из прицела) только у
/// типов из сборки RimWorld. Здесь — то же для сборки мода: все типы ITargetingSource, у
/// которых OrderForceTarget объявлен в самом типе. Порядок — по имени типа.
/// </summary>
internal static class MpTargetingSync
{
	public static int RegisterDeclared(Assembly asm, string tag)
	{
		if (asm == null)
		{
			return 0;
		}
		Type[] types;
		try
		{
			types = asm.GetTypes();
		}
		catch (ReflectionTypeLoadException ex)
		{
			types = ex.Types.Where((Type t) => t != null).ToArray();
		}
		int n = 0;
		foreach (Type t in types.Where((Type t) => typeof(ITargetingSource).IsAssignableFrom(t)).OrderBy((Type t) => t.FullName, StringComparer.Ordinal))
		{
			MethodInfo m = t.GetMethod("OrderForceTarget", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, new Type[1] { typeof(LocalTargetInfo) }, null);
			if (m != null && !m.IsAbstract && MpLambdaSync.Method(m, debugOnly: false, tag) != null)
			{
				n++;
			}
		}
		return n;
	}
}

/// <summary>
/// Гизмо, которые пишут поля компонентов прямо в GizmoOnGUI (галочки, ползунки): поля
/// регистрируются как синхронные, а на время отрисовки гизмо MP следит за всеми
/// компонентами нужного типа, до которых гизмо может дотянуться (поля и списки гизмо).
/// </summary>
internal sealed class MpGizmoFieldWatch
{
	private readonly Type _compType;

	private readonly List<ISyncField> _fields = new List<ISyncField>();

	private static readonly Dictionary<Type, FieldInfo[]> FieldCache = new Dictionary<Type, FieldInfo[]>();

	public bool Ready => _compType != null && _fields.Count > 0;

	public MpGizmoFieldWatch(string compTypeName, string[] fieldNames, string[] bufferedFields, string tag)
	{
		_compType = AccessTools.TypeByName(compTypeName);
		if (_compType == null)
		{
			return;
		}
		foreach (string name in fieldNames)
		{
			if (AccessTools.Field(_compType, name) == null)
			{
				Log.Warning("[RaidAnimalFix] " + tag + ": field " + compTypeName + "." + name + " not found (mod updated?)");
				continue;
			}
			try
			{
				ISyncField f = MP.RegisterSyncField(_compType, name);
				if (bufferedFields != null && Array.IndexOf(bufferedFields, name) >= 0)
				{
					f.SetBufferChanges();
				}
				_fields.Add(f);
			}
			catch (Exception ex)
			{
				Log.Warning("[RaidAnimalFix] " + tag + ": cannot sync " + compTypeName + "." + name + ": " + ex.Message);
			}
		}
	}

	public void WatchReachable(object gizmo)
	{
		if (!Ready || gizmo == null)
		{
			return;
		}
		HashSet<object> seen = new HashSet<object>(RefEq.Instance);
		foreach (FieldInfo fi in FieldsOf(gizmo.GetType()))
		{
			object value;
			try
			{
				value = fi.GetValue(gizmo);
			}
			catch
			{
				continue;
			}
			if (value == null)
			{
				continue;
			}
			if (_compType.IsInstanceOfType(value))
			{
				Watch(value, seen);
			}
			else if (value is IEnumerable list && !(value is string))
			{
				foreach (object item in list)
				{
					if (item != null && _compType.IsInstanceOfType(item))
					{
						Watch(item, seen);
					}
				}
			}
		}
	}

	private void Watch(object comp, HashSet<object> seen)
	{
		if (!seen.Add(comp))
		{
			return;
		}
		foreach (ISyncField f in _fields)
		{
			f.Watch(comp);
		}
	}

	private static FieldInfo[] FieldsOf(Type t)
	{
		if (!FieldCache.TryGetValue(t, out FieldInfo[] fields))
		{
			List<FieldInfo> list = new List<FieldInfo>();
			for (Type cur = t; cur != null && cur != typeof(object); cur = cur.BaseType)
			{
				list.AddRange(cur.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
			}
			fields = list.ToArray();
			FieldCache[t] = fields;
		}
		return fields;
	}

	private sealed class RefEq : IEqualityComparer<object>
	{
		public static readonly RefEq Instance = new RefEq();

		public new bool Equals(object a, object b)
		{
			return a == b;
		}

		public int GetHashCode(object o)
		{
			return RuntimeHelpers.GetHashCode(o);
		}
	}
}

/// <summary>
/// Визуальные методы модов, которые тратят Rand уже после проверки «видит ли игрок эту
/// клетку» (ShouldSpawnMotesAt — своя карта у каждого клиента). Ванильные FleckMaker MP
/// изолирует сам; здесь — то же для FleckMaker'ов модов: весь Rand внутри откатывается.
/// </summary>
[HarmonyPatch]
public static class ModVisualRandIsolationMp
{
	private static readonly string[] Targets = new string[3] { "Milira.MiliraFleckMaker:ThrowPlasmaAirPuffUp", "Milira.MiliraFleckMaker:ThrowLineEMP", "AncotLibrary.AncotFleckMaker:ThrowTrailFleckUp" };

	private static IEnumerable<MethodBase> TargetMethods()
	{
		foreach (string name in Targets)
		{
			MethodInfo m = AccessTools.Method(name);
			if (m != null)
			{
				yield return m;
			}
		}
	}

	private static void Prefix(out bool __state)
	{
		__state = MpHelper.IsMultiplayer();
		if (__state)
		{
			Rand.PushState();
		}
	}

	private static Exception Finalizer(Exception __exception, bool __state)
	{
		if (__state)
		{
			Rand.PopState();
		}
		return __exception;
	}
}

/// <summary>
/// Галочки в таблицах пешек модов (PawnColumnWorker_Checkbox.SetValue пишет поле
/// компонента). Вместо синхронизации самого SetValue (MP пришлось бы пересоздавать
/// таблицу) клик уходит командой (колонка, пешка, значение), а SetValue выполняется у всех.
/// </summary>
[HarmonyPatch]
public static class ModCheckboxColumnsMp
{
	private static readonly string[] Columns = new string[3] { "Milira.PawnColumnWorker_AutoSuitMilian", "Milira.PawnColumnWorker_BillWorkerDisplayMilian", "AncotLibrary.PawnColumnWorker_DroneAutoRepair" };

	private static readonly Dictionary<Type, MethodInfo> SetValueOf = new Dictionary<Type, MethodInfo>();

	private static IEnumerable<MethodBase> TargetMethods()
	{
		foreach (string name in Columns)
		{
			Type t = AccessTools.TypeByName(name);
			if (t == null || ModCompatGuards.ForeignPatchFor(t))
			{
				continue;
			}
			MethodInfo m = AccessTools.DeclaredMethod(t, "SetValue", new Type[3] { typeof(Pawn), typeof(bool), typeof(PawnTable) });
			if (m != null)
			{
				SetValueOf[t] = m;
				yield return m;
			}
		}
	}

	private static bool Prefix(PawnColumnWorker __instance, Pawn pawn, bool value)
	{
		if (!MpUi.Now() || __instance?.def == null || pawn == null)
		{
			return true;
		}
		SetCheckboxColumn(__instance.def, pawn, value);
		return false;
	}

	[SyncMethod]
	public static void SetCheckboxColumn(PawnColumnDef def, Pawn pawn, bool value)
	{
		PawnColumnWorker worker = def?.Worker;
		if (worker == null || pawn == null || !SetValueOf.TryGetValue(worker.GetType(), out MethodInfo setValue))
		{
			return;
		}
		setValue.Invoke(worker, new object[3] { pawn, value, null });
	}
}

/// <summary>Патчи-дубли: если стоит отдельный MP-патч для мода, наш модуль для него молчит.</summary>
internal static class ModCompatGuards
{
	public const string MiliraForeign = "keullaeseu.mp.ancot.milirarace";

	public const string AncotForeign = "keullaeseu.mp.ancot.ancotlibrary";

	public static bool Active(string packageId)
	{
		try
		{
			return ModsConfig.IsActive(packageId);
		}
		catch
		{
			return false;
		}
	}

	public static bool ForeignPatchFor(Type t)
	{
		string ns = t?.Namespace;
		if (ns == "Milira")
		{
			return Active(MiliraForeign);
		}
		if (ns == "AncotLibrary")
		{
			return Active(AncotForeign);
		}
		return false;
	}
}
