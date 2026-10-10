using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RaidAnimalFix;

[HarmonyPatch(typeof(Frame), "FailConstruction")]
public static class FailConstructionMessageSyncFix
{
	[HarmonyTranspiler]
	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
	{
		List<CodeInstruction> list = new List<CodeInstruction>(insts);
		MethodInfo operand = AccessTools.Method(typeof(FailConstructionMessageSyncFix), "ShouldNotifyForFaction");
		int num = 0;
		for (int i = 1; i < list.Count - 1; i++)
		{
			MethodInfo methodInfo = list[i].operand as MethodInfo;
			if (!(methodInfo == null) && !(methodInfo.Name != "get_OfPlayer") && !(methodInfo.DeclaringType != typeof(Faction)) && (!(list[i + 1].opcode != OpCodes.Bne_Un) || !(list[i + 1].opcode != OpCodes.Bne_Un_S)))
			{
				MethodInfo methodInfo2 = list[i - 1].operand as MethodInfo;
				if (!(methodInfo2 == null) && !(methodInfo2.Name != "get_Faction"))
				{
					list[i] = new CodeInstruction(OpCodes.Call, operand)
					{
						labels = list[i].labels,
						blocks = list[i].blocks
					};
					CodeInstruction codeInstruction = list[i + 1];
					list[i + 1] = new CodeInstruction(OpCodes.Brfalse, codeInstruction.operand)
					{
						labels = codeInstruction.labels,
						blocks = codeInstruction.blocks
					};
					num++;
				}
			}
		}
		if (num == 1)
		{
			Log.Message("[RaidAnimalFix] FailConstructionMessage: OfPlayer check -> player-faction frame");
		}
		else
		{
			Log.Warning("[RaidAnimalFix] FailConstructionMessage: expected 1 replacement, got " + num);
		}
		return list;
	}

	public static bool ShouldNotifyForFaction(Faction frameFaction)
	{
		if (!MpHelper.IsMultiplayer())
		{
			return frameFaction == Faction.OfPlayer;
		}
		return MpContext.IsRealPlayerFaction(frameFaction);
	}
}
