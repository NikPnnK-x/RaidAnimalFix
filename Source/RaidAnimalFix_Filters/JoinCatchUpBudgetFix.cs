using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace RaidAnimalFix;

[HarmonyPatch]
public static class JoinCatchUpBudgetFix
{
	private const int BudgetMs = 150;

	private static MethodBase TargetMethod()
	{
		Type type = RafTypes.Find("Multiplayer.Client.TickPatch");
		if (!(type == null))
		{
			return AccessTools.Method(type, "DoUpdate");
		}
		return null;
	}

	[HarmonyTranspiler]
	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
	{
		List<CodeInstruction> list = new List<CodeInstruction>(insts);
		int num = 0;
		for (int i = 1; i < list.Count; i++)
		{
			CodeInstruction codeInstruction = list[i];
			bool flag = (codeInstruction.opcode == OpCodes.Ldc_I4_S && codeInstruction.operand is sbyte && (sbyte)codeInstruction.operand == 25) || (codeInstruction.opcode == OpCodes.Ldc_I4 && codeInstruction.operand is int && (int)codeInstruction.operand == 25);
			bool flag2 = codeInstruction.opcode == OpCodes.Ldc_I8 && codeInstruction.operand is long && (long)codeInstruction.operand == 25;
			if (flag || flag2)
			{
				MethodInfo methodInfo = list[i - 1].operand as MethodInfo;
				if (!(methodInfo == null) && !(methodInfo.Name != "get_ElapsedMilliseconds"))
				{
					list[i] = (flag2 ? new CodeInstruction(OpCodes.Ldc_I8, 150L)
					{
						labels = codeInstruction.labels,
						blocks = codeInstruction.blocks
					} : new CodeInstruction(OpCodes.Ldc_I4, 150)
					{
						labels = codeInstruction.labels,
						blocks = codeInstruction.blocks
					});
					num++;
				}
			}
		}
		if (num == 1)
		{
			Log.Message("[RaidAnimalFix] JoinCatchUpBudget: бюджет догона 25 мс -> " + 150 + " мс на кадр");
		}
		else
		{
			Log.Warning("[RaidAnimalFix] JoinCatchUpBudget: ожидалась 1 замена, получено " + num + " — догон при входе останется медленным");
		}
		return list;
	}
}
