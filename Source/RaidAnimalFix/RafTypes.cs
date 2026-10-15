using System;
using System.Reflection;
using HarmonyLib;

namespace RaidAnimalFix;

/// <summary>
/// Поиск типа по полному имени для необязательных патчей (тип стороннего мода).
///
/// AccessTools.TypeByName при промахе (мода нет) дважды перебирает ВСЕ типы всех
/// загруженных сборок — сотни миллисекунд на каждый вызов, а таких вызовов при запуске
/// десятки. Здесь — те же первые два шага (Type.GetType и GetType по каждой сборке),
/// без полного перебора: он нужен только для коротких имён без пространства имён,
/// а мы всегда передаём полные (вложенные типы — через '+').
/// </summary>
public static class RafTypes
{
	public static Type Find(string fullName)
	{
		if (string.IsNullOrEmpty(fullName))
		{
			return null;
		}
		Type type = Type.GetType(fullName, throwOnError: false);
		if (type != null)
		{
			return type;
		}
		foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
		{
			try
			{
				type = assembly.GetType(fullName, throwOnError: false);
			}
			catch
			{
				type = null;
			}
			if (type != null)
			{
				return type;
			}
		}
		return null;
	}

	/// <summary>Как AccessTools.Method("Тип:Метод", …), но тип ищется через <see cref="Find"/>.</summary>
	public static MethodInfo Method(string typeColonName, Type[] parameters = null, Type[] generics = null)
	{
		if (string.IsNullOrEmpty(typeColonName))
		{
			return null;
		}
		int colon = typeColonName.LastIndexOf(':');
		if (colon <= 0 || colon == typeColonName.Length - 1)
		{
			return AccessTools.Method(typeColonName, parameters, generics);
		}
		Type type = Find(typeColonName.Substring(0, colon));
		return (type == null) ? null : AccessTools.Method(type, typeColonName.Substring(colon + 1), parameters, generics);
	}
}
