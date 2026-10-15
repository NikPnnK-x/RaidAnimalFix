using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace RaidAnimalFix;

/// <summary>
/// Синхронизация лямбд из кнопок / меню модов без номеров лямбд.
///
/// Кнопка мода — это лямбда, которую компилятор кладёт либо в сам класс
/// (<c>&lt;GetGizmos&gt;b__12_0</c>, если захвачен только this), либо в класс-замыкание
/// (<c>&lt;&gt;c__DisplayClass12_0.&lt;GetGizmos&gt;b__1</c>), либо в <c>&lt;&gt;c</c>. Номера
/// меняются от версии к версии мода, поэтому лямбды ищутся по IL: синхронизируются
/// только те, что пишут указанные поля или вызывают указанные методы (меняют
/// состояние игры). Лямбды «открыть меню», «показать карточку» остаются локальными.
///
/// Порядок регистрации детерминирован (сортировка по именам) — MP требует одинаковый
/// порядок у всех клиентов.
/// </summary>
internal static class MpLambdaSync
{
	public sealed class Match
	{
		public Type parent;

		public Type declaring;

		public MethodInfo method;

		public bool onParent;

		public override string ToString()
		{
			return parent.Name + (onParent ? "" : ("+" + declaring.Name)) + "." + method.Name;
		}
	}

	private static readonly string[] UiBases = new string[3] { "Verse.Gizmo", "Verse.Window", "Verse.Command" };

	/// <summary>Уже зарегистрированные методы — повторная регистрация в MP бросает исключение.</summary>
	private static readonly HashSet<MethodBase> Registered = new HashSet<MethodBase>();

	/// <summary>Найти лямбды метода parentMethod класса typeName, меняющие состояние.</summary>
	public static List<Match> Find(string typeName, string parentMethod, string[] stateFields, string[] stateMethods)
	{
		List<Match> result = new List<Match>();
		Type parent = AccessTools.TypeByName(typeName);
		if (parent == null)
		{
			return result;
		}
		string prefix = "<" + parentMethod + ">b__";
		foreach (Type t in new Type[1] { parent }.Concat(Nested(parent).OrderBy((Type n) => n.FullName, StringComparer.Ordinal)))
		{
			List<MethodInfo> methods;
			try
			{
				methods = AccessTools.GetDeclaredMethods(t);
			}
			catch
			{
				continue;
			}
			foreach (MethodInfo m in methods.OrderBy((MethodInfo x) => x.Name, StringComparer.Ordinal))
			{
				if (m.Name.StartsWith(prefix, StringComparison.Ordinal) && TouchesState(m, stateFields, stateMethods))
				{
					result.Add(new Match
					{
						parent = parent,
						declaring = t,
						method = m,
						onParent = t == parent
					});
				}
			}
		}
		return result;
	}

	/// <summary>Зарегистрировать найденные лямбды в MP. Возвращает число зарегистрированных.</summary>
	public static int Register(string typeName, string parentMethod, string[] stateFields, string[] stateMethods, bool debugOnly = false, string tag = null, SyncContext context = SyncContext.None)
	{
		int n = 0;
		List<Match> matches = Find(typeName, parentMethod, stateFields ?? new string[0], stateMethods ?? new string[0]);
		foreach (Match m in matches)
		{
			if (!Registered.Add(m.method))
			{
				continue;
			}
			try
			{
				if (m.onParent)
				{
					if (IsUiType(m.parent) || m.method.IsStatic)
					{
						Log.Warning("[RaidAnimalFix] " + (tag ?? "MP") + ": лямбда " + m + " принадлежит объекту интерфейса — пропущена");
						continue;
					}
					ISyncMethod s = MP.RegisterSyncMethod(m.method);
					if (context != SyncContext.None)
					{
						s.SetContext(context);
					}
					if (debugOnly)
					{
						s.SetDebugOnly();
					}
				}
				else
				{
					ISyncDelegate d = MP.RegisterSyncDelegate(m.parent, m.declaring.Name, m.method.Name, null);
					if (context != SyncContext.None)
					{
						d.SetContext(context);
					}
					if (debugOnly)
					{
						d.SetDebugOnly();
					}
				}
				n++;
			}
			catch (Exception ex)
			{
				Log.Warning("[RaidAnimalFix] " + (tag ?? "MP") + ": не удалось синхронизировать " + m + ": " + ex.Message);
			}
		}
		if (matches.Count == 0)
		{
			Log.Warning("[RaidAnimalFix] " + (tag ?? "MP") + ": в " + typeName + "." + parentMethod + " не найдено лямбд, меняющих состояние (мод обновился?)");
		}
		return n;
	}

	private static bool TouchesState(MethodInfo method, string[] stateFields, string[] stateMethods)
	{
		List<CodeInstruction> code;
		try
		{
			code = PatchProcessor.GetOriginalInstructions(method);
		}
		catch
		{
			return false;
		}
		foreach (CodeInstruction ci in code)
		{
			if ((ci.opcode == OpCodes.Stfld || ci.opcode == OpCodes.Stsfld) && ci.operand is FieldInfo f && Array.IndexOf(stateFields, f.Name) >= 0)
			{
				return true;
			}
			if (ci.operand is MethodBase called && Array.IndexOf(stateMethods, called.Name) >= 0)
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsUiType(Type type)
	{
		foreach (string name in UiBases)
		{
			Type ui = AccessTools.TypeByName(name);
			if (ui != null && ui.IsAssignableFrom(type))
			{
				return true;
			}
		}
		return false;
	}

	private static IEnumerable<Type> Nested(Type type)
	{
		Type[] nested;
		try
		{
			nested = type.GetNestedTypes(AccessTools.all);
		}
		catch
		{
			yield break;
		}
		foreach (Type n in nested)
		{
			yield return n;
			foreach (Type deeper in Nested(n))
			{
				yield return deeper;
			}
		}
	}

	/// <summary>Синхронный метод по имени (если есть). Возвращает true при успехе.</summary>
	public static bool Method(string typeColonMethod, bool debugOnly = false, string tag = null, Type[] args = null)
	{
		try
		{
			MethodInfo m = AccessTools.Method(typeColonMethod, args);
			if (m == null)
			{
				Log.Warning("[RaidAnimalFix] " + (tag ?? "MP") + ": метод " + typeColonMethod + " не найден (мод обновился?)");
				return false;
			}
			return Method(m, debugOnly, tag) != null;
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] " + (tag ?? "MP") + ": не удалось синхронизировать " + typeColonMethod + ": " + ex.Message);
			return false;
		}
	}

	/// <summary>Синхронный метод (без повторной регистрации). Возвращает null, если уже был или не вышло.</summary>
	public static ISyncMethod Method(MethodInfo m, bool debugOnly = false, string tag = null)
	{
		if (m == null || !Registered.Add(m))
		{
			return null;
		}
		try
		{
			ISyncMethod s = MP.RegisterSyncMethod(m);
			if (debugOnly)
			{
				s.SetDebugOnly();
			}
			return s;
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] " + (tag ?? "MP") + ": не удалось синхронизировать " + m.DeclaringType?.FullName + ":" + m.Name + ": " + ex.Message);
			return null;
		}
	}
}
