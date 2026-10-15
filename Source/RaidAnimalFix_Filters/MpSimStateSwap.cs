using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RaidAnimalFix;

/// <summary>
/// Две копии статического состояния мода: одна для интерфейса, другая для симуляции.
///
/// Многие моды хранят в static-поле то, что игрок выбирает в окне (например,
/// рассадку пешек по машинам в Vehicle Framework), а читают это поле и интерфейс,
/// и симуляция (расчёт веса каравана, работы пешек). В MP окно открыто только у
/// одного игрока → у него симуляция считает иначе → рассинхрон.
///
/// Здесь на время тика или команды MP в поле подставляется «симуляционная» копия,
/// после — возвращается копия интерфейса. Симуляционная копия меняется только
/// внутри симуляции (то есть одинаково у всех), интерфейс работает как раньше.
/// </summary>
internal static class MpSimStateSwap
{
	private sealed class Slot
	{
		public FieldInfo field;

		public Func<object> factory;

		public object sim;

		public object ui;
	}

	private static readonly List<Slot> Slots = new List<Slot>();

	private static int _depth;

	public static bool InSwap => _depth > 0;

	public static void Register(FieldInfo field, Func<object> factory)
	{
		if (field == null || !field.IsStatic || factory == null)
		{
			return;
		}
		for (int i = 0; i < Slots.Count; i++)
		{
			if (Slots[i].field == field)
			{
				return;
			}
		}
		Slots.Add(new Slot
		{
			field = field,
			factory = factory
		});
	}

	internal static void Enter()
	{
		if (Slots.Count == 0 || _depth++ > 0)
		{
			return;
		}
		for (int i = 0; i < Slots.Count; i++)
		{
			Slot slot = Slots[i];
			try
			{
				slot.ui = slot.field.GetValue(null);
				if (slot.sim == null)
				{
					slot.sim = slot.factory();
				}
				slot.field.SetValue(null, slot.sim);
			}
			catch (Exception ex)
			{
				Log.ErrorOnce("[RaidAnimalFix] MpSimStateSwap enter " + slot.field.Name + ": " + ex.Message, 0x5A1D0 + i);
			}
		}
	}

	internal static void Exit()
	{
		if (Slots.Count == 0 || _depth == 0 || --_depth > 0)
		{
			return;
		}
		for (int i = 0; i < Slots.Count; i++)
		{
			Slot slot = Slots[i];
			try
			{
				// Симуляция могла заменить объект целиком — сохраняем то, что в поле сейчас.
				slot.sim = slot.field.GetValue(null);
				slot.field.SetValue(null, slot.ui);
				slot.ui = null;
			}
			catch (Exception ex)
			{
				Log.ErrorOnce("[RaidAnimalFix] MpSimStateSwap exit " + slot.field.Name + ": " + ex.Message, 0x5A1E0 + i);
			}
		}
	}

	/// <summary>Перед загрузкой игры (точка входа): симуляционные копии создаются заново у всех.</summary>
	internal static void ResetSim()
	{
		if (_depth > 0)
		{
			return;
		}
		for (int i = 0; i < Slots.Count; i++)
		{
			Slots[i].sim = null;
		}
	}
}

/// <summary>Входы в симуляцию MP: тик карты, тик мира, команда карты, команда мира.</summary>
[HarmonyPatch]
public static class MpSimStateSwapPatch
{
	private static bool Prepare()
	{
		return AccessTools.TypeByName("Multiplayer.Client.AsyncTimeComp") != null;
	}

	private static IEnumerable<MethodBase> TargetMethods()
	{
		Type map = AccessTools.TypeByName("Multiplayer.Client.AsyncTimeComp");
		Type world = AccessTools.TypeByName("Multiplayer.Client.AsyncTime.AsyncWorldTimeComp") ?? AccessTools.TypeByName("Multiplayer.Client.AsyncWorldTimeComp");
		foreach (Type t in new Type[2] { map, world })
		{
			if (t == null)
			{
				continue;
			}
			MethodInfo tick = AccessTools.DeclaredMethod(t, "Tick", Type.EmptyTypes);
			if (tick != null)
			{
				yield return tick;
			}
			foreach (MethodInfo m in AccessTools.GetDeclaredMethods(t))
			{
				if (m.Name == "ExecuteCmd" && m.GetParameters().Length == 1)
				{
					yield return m;
				}
			}
		}
	}

	private static void Prefix()
	{
		MpSimStateSwap.Enter();
	}

	private static void Finalizer()
	{
		MpSimStateSwap.Exit();
	}
}

/// <summary>Сброс симуляционных копий перед загрузкой игры в MP.</summary>
[HarmonyPatch]
public static class MpSimStateSwapReset
{
	private static IEnumerable<MethodBase> TargetMethods()
	{
		yield return AccessTools.DeclaredMethod(typeof(Game), "LoadGame");
		yield return AccessTools.DeclaredMethod(typeof(Game), "InitNewGame");
	}

	private static void Prefix()
	{
		if (MpHelper.IsMultiplayer())
		{
			MpSimStateSwap.ResetSim();
		}
	}
}
