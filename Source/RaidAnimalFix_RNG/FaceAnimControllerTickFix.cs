using System;
using System.Reflection;
using HarmonyLib;

namespace RaidAnimalFix;

/// <summary>
/// FacialAnimationControllerComp.CompTick в тике только помечает портрет
/// (UI), но через InitializeIfNeed → GatherPawnParam → GetAllMoodThoughts
/// может пересчитать мысли настроения в тике — и только у тех клиентов, где
/// рендер ещё не успел инициализировать компонент (зависит от кадров и
/// настроек отрисовки). В MP тик целиком пропускаем: инициализацию делает рендер.
/// </summary>
[HarmonyPatch]
public static class FaceAnimControllerTickFix
{
	private static MethodBase TargetMethod()
	{
		Type type = AccessTools.TypeByName("FacialAnimation.FacialAnimationControllerComp");
		if (type == null)
		{
			return null;
		}
		return AccessTools.Method(type, "CompTick");
	}

	private static bool Prefix()
	{
		return !MpHelper.IsMultiplayer();
	}
}
