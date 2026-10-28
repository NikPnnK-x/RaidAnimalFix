using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RaidAnimalFix;

/// <summary>
/// Случайные вдохновения. CheckStartRandomInspiration зовётся из тика пешки у
/// всех клиентов, но ролл MTBEventOccurs + выбор вдохновения
/// RandomElementByWeightWithFallback потребляют общий поток Rand. Когда вход
/// метода расходится между клиентами (редкая комбинация состояния пешки в
/// multifaction — Desync-41/42: «Trace hashes don't match» / «Wrong random
/// state», у одного из клиентов ровно +2 вызова Rand: Human266179 на тике
/// 3539380, Human127729 на тике 4058368), ролл выполняется только у одного, и
/// общий поток Rand сдвигается у всех последующих потребителей.
///
/// Изолируем весь метод в собственный поток Rand с детерминированным сидом из
/// thingIDNumber пешки и тика игры (паттерн RngIsolation): общий поток Rand
/// решения больше не трогает вовсе — сдвинуть его рассинхронный вход не может;
/// а при одинаковом входе сид одинаков и результат одинаков у всех.
/// </summary>
[HarmonyPatch(typeof(InspirationHandler), "CheckStartRandomInspiration")]
public static class InspirationRandIsolation
{
	private static void Prefix(InspirationHandler __instance, ref bool __state)
	{
		__state = false;
		if (!MpHelper.IsMultiplayer())
		{
			return;
		}
		try
		{
			Pawn pawn = __instance.pawn;
			int seed = Gen.HashCombineInt(pawn?.thingIDNumber ?? 0, Find.TickManager?.TicksGame ?? 0);
			Rand.PushState(seed);
			__state = true;
		}
		catch (Exception ex)
		{
			CaravanLeaveHelper.LogErrorThrottled("InspirationRandIsolation", ex);
		}
	}

	private static void Finalizer(bool __state)
	{
		if (__state)
		{
			try
			{
				Rand.PopState();
			}
			catch (Exception ex)
			{
				CaravanLeaveHelper.LogErrorThrottled("InspirationRandIsolation.pop", ex);
			}
		}
	}
}
