using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace RaidAnimalFix;

// GIDDY-UP 2 — CONTINUED (MemeGoddess.GiddyUp) + Multiplayer.
// Патч Multiplayer Compatibility для Giddy-Up 2 привязан к идентификатору
// «Owlchemist.GiddyUp» — к продолжению MemeGoddess он не применяется вовсе. К тому же
// в продолжении классы переехали (GiddyUpCore.RideAndRoll.Harmony.Pawn_GetGizmos),
// лямбд посадки стало 4, поля ExtendedPawnData / Designator_GU переименованы.
// Таблица совместимости MP: «сам садится — нормально, приказ сесть — рассинхрон».
// Повторяем покрытие Multiplayer Compatibility под новые имена:
//  * меню «сесть / слезть / сменить животное / слезть без привязи» → синхронно;
//  * кнопка «отпустить животных» и «закончить работу» → синхронно;
//  * выбор всадника в окне каравана → синхронно (+ отслеживание количества);
//  * галочки «можно ездить» в таблице животных → синхронно;
//  * дизайнатор зон Giddy-Up — выбранная зона передаётся в команду.
// Если Multiplayer Compatibility когда-нибудь добавит поддержку этого идентификатора —
// наша регистрация не выполняется (двойная регистрация MP запрещена).

internal static class GiddyUpMpSync
{
	private const string PackageId = "MemeGoddess.GiddyUp";

	private static Harmony _harmony;

	private static FieldInfo _trad;

	private static FieldInfo _parentLocals;

	private static FastInvokeHandler _adjustTo;

	public static void Register()
	{
		try
		{
			if (ModLister.GetActiveModWithIdentifier(PackageId, ignorePostfix: true) == null)
			{
				return;
			}
		}
		catch
		{
			return;
		}
		if (MpCompatHandles(PackageId))
		{
			Log.Message("[RaidAnimalFix] Giddy-Up 2: поддержка уже есть в Multiplayer Compatibility, наша регистрация пропущена");
			return;
		}
		int ok = 0;
		int fail = 0;
		void Try(string what, Action action)
		{
			try
			{
				action();
				ok++;
			}
			catch (Exception ex)
			{
				fail++;
				Log.Warning("[RaidAnimalFix] Giddy-Up 2 MP: " + what + ": " + (ex.InnerException ?? ex).Message);
			}
		}
		Type epd = AccessTools.TypeByName("GiddyUp.ExtendedPawnData");
		FieldInfo epdPawn = (epd == null) ? null : (AccessTools.Field(epd, "_pawn") ?? AccessTools.Field(epd, "pawn"));
		MethodInfo getData = AccessTools.Method("GiddyUp.StorageUtility:GetExtendedPawnData", new Type[1] { typeof(Pawn) });
		if (epd == null || epdPawn == null || getData == null)
		{
			Log.Warning("[RaidAnimalFix] Giddy-Up 2 MP: ExtendedPawnData не найден — синхронизация не зарегистрирована");
			return;
		}
		// Порядок регистраций фиксирован — одинаков у всех клиентов.
		Try("ExtendedPawnData worker", delegate
		{
			MP.RegisterSyncWorker<object>(delegate(SyncWorker sync, ref object data)
			{
				if (sync.isWriting)
				{
					sync.Write(epdPawn.GetValue(data) as Pawn);
				}
				else
				{
					Pawn pawn = sync.Read<Pawn>();
					data = (pawn == null) ? null : getData.Invoke(null, new object[1] { pawn });
				}
			}, epd);
		});
		Try("gizmo: release animals", delegate
		{
			RegisterDelegate("GiddyUp.Harmony.Patch_PawnGetGizmos", "<Postfix>b__0", new string[1] { "__instance" });
		});
		Try("gizmo: end current job", delegate
		{
			Type t = AccessTools.TypeByName("GiddyUpCore.RideAndRoll.Harmony.Pawn_GetGizmos") ?? AccessTools.TypeByName("GiddyUpRideAndRoll.Harmony.Pawn_GetGizmos");
			MP.RegisterSyncMethod(AccessTools.Method(t, "PawnEndCurrentJob"));
		});
		for (int i = 0; i < 4; i++)
		{
			int n = i;
			Try("float menu: mounting option " + n, delegate
			{
				RegisterDelegate("GiddyUp.Harmony.FloatMenuUtility", "<AddMountingOptions>b__" + n, null);
			});
		}
		Type widget = AccessTools.TypeByName("GiddyUpCaravan.Harmony.Patch_TransferableOneWayWidget");
		Try("caravan: select rider", delegate
		{
			MP.RegisterSyncMethod(AccessTools.Method(widget, "SelectMountRider"));
		});
		Try("caravan: clear rider", delegate
		{
			MP.RegisterSyncMethod(AccessTools.Method(widget, "ClearMountRider"));
		});
		Try("caravan: transferable count watch", PatchHandleAnimal);
		Try("animals tab: mountable (colonists)", delegate
		{
			MP.RegisterSyncMethod(AccessTools.Method("GiddyUpRideAndRoll.PawnColumnWorker_Mountable_Colonists:SetValue"));
		});
		Try("animals tab: mountable (slaves)", delegate
		{
			MP.RegisterSyncMethod(AccessTools.Method("GiddyUpRideAndRoll.PawnColumnWorker_Mountable_Slaves:SetValue"));
		});
		Try("area designator", delegate
		{
			Type des = AccessTools.TypeByName("GiddyUp.Designator_GU");
			FieldInfo area = AccessTools.Field(des, "<SelectedArea>k__BackingField") ?? AccessTools.Field(des, "selectedArea");
			FieldInfo label = AccessTools.Field(des, "_areaLabel") ?? AccessTools.Field(des, "areaLabel");
			if (area == null || label == null)
			{
				throw new MissingFieldException("Designator_GU area/label");
			}
			MP.RegisterSyncWorker<Designator>(delegate(SyncWorker sync, ref Designator designator)
			{
				if (sync.isWriting)
				{
					sync.Write(area.GetValue(designator) as Area);
					sync.Write(label.GetValue(designator) as string);
				}
				else
				{
					area.SetValue(designator, sync.Read<Area>());
					label.SetValue(designator, sync.Read<string>());
				}
			}, des, isImplicit: true, shouldConstruct: true);
		});
		Log.Message("[RaidAnimalFix] Giddy-Up 2 MP: зарегистрировано " + ok + ", ошибок " + fail);
	}

	/// <summary>Лямбда, найденная по имени метода в одном из вложенных классов-замыканий.</summary>
	private static void RegisterDelegate(string typeName, string methodName, string[] fields)
	{
		Type outer = AccessTools.TypeByName(typeName) ?? throw new TypeLoadException(typeName);
		foreach (Type nt in outer.GetNestedTypes(AccessTools.all))
		{
			if (AccessTools.DeclaredMethod(nt, methodName) != null)
			{
				if (fields == null)
				{
					MP.RegisterSyncDelegate(outer, nt.Name, methodName);
				}
				else
				{
					MP.RegisterSyncDelegate(outer, nt.Name, methodName, fields);
				}
				return;
			}
		}
		throw new MissingMethodException(typeName, methodName);
	}

	/// <summary>Как в Multiplayer Compatibility: перед выбором всадника отслеживаем количество в окне каравана.</summary>
	private static void PatchHandleAnimal()
	{
		Type widget = AccessTools.TypeByName("GiddyUpCaravan.Harmony.Patch_TransferableOneWayWidget");
		MethodInfo adjust = AccessTools.Method("Multiplayer.Client.SyncFields:TransferableAdjustTo");
		if (widget == null || adjust == null)
		{
			throw new MissingMethodException("HandleAnimal / TransferableAdjustTo");
		}
		_adjustTo = MethodInvoker.GetHandler(adjust);
		_harmony = _harmony ?? new Harmony("customfix.raidanimalfix.giddyup");
		MethodInfo noRider = null;
		MethodInfo withRider = null;
		foreach (Type nt in widget.GetNestedTypes(AccessTools.all))
		{
			MethodInfo b0 = AccessTools.DeclaredMethod(nt, "<HandleAnimal>b__0");
			if (b0 != null && AccessTools.Field(nt, "trad") != null)
			{
				noRider = b0;
				_trad = AccessTools.Field(nt, "trad");
			}
			MethodInfo b1 = AccessTools.DeclaredMethod(nt, "<HandleAnimal>b__1");
			if (b1 != null && AccessTools.Field(nt, "CS$<>8__locals1") != null)
			{
				withRider = b1;
				_parentLocals = AccessTools.Field(nt, "CS$<>8__locals1");
			}
		}
		if (noRider == null || withRider == null || _trad == null || _parentLocals == null)
		{
			throw new MissingMethodException("HandleAnimal lambdas");
		}
		_harmony.Patch(noRider, new HarmonyMethod(typeof(GiddyUpMpSync), "WatchNoRider"));
		_harmony.Patch(withRider, new HarmonyMethod(typeof(GiddyUpMpSync), "WatchWithRider"));
	}

	private static void WatchNoRider(object __instance)
	{
		if (MpHelper.IsMultiplayer() && _trad.GetValue(__instance) is TransferableOneWay trad)
		{
			_adjustTo(null, trad);
		}
	}

	private static void WatchWithRider(object __instance)
	{
		object parent = _parentLocals.GetValue(__instance);
		if (MpHelper.IsMultiplayer() && parent != null && _trad.GetValue(parent) is TransferableOneWay trad)
		{
			_adjustTo(null, trad);
		}
	}

	/// <summary>Есть ли в загруженном Multiplayer Compatibility модуль для этого идентификатора.</summary>
	private static bool MpCompatHandles(string packageId)
	{
		foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
		{
			string name = asm.GetName().Name;
			if (name == null || !name.StartsWith("Multiplayer_Compat", StringComparison.Ordinal))
			{
				continue;
			}
			Type[] types;
			try
			{
				types = asm.GetTypes();
			}
			catch (ReflectionTypeLoadException e)
			{
				types = e.Types.Where((Type t) => t != null).ToArray();
			}
			foreach (Type t in types)
			{
				try
				{
					foreach (CustomAttributeData a in t.GetCustomAttributesData())
					{
						if (a.AttributeType.Name == "MpCompatForAttribute" && a.ConstructorArguments.Any((CustomAttributeTypedArgument arg) => string.Equals(arg.Value as string, packageId, StringComparison.OrdinalIgnoreCase)))
						{
							return true;
						}
					}
				}
				catch
				{
				}
			}
		}
		return false;
	}
}
