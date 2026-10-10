using System;
using System.Reflection;
using HarmonyLib;

namespace RaidAnimalFix;

[HarmonyPatch]
public static class ChatPlayerLabelFix
{
	private static MethodBase TargetMethod()
	{
		Type type = AccessTools.TypeByName("Multiplayer.Client.ChatWindow");
		Type type2 = AccessTools.TypeByName("Multiplayer.Client.PlayerInfo");
		if (type == null || type2 == null)
		{
			return null;
		}
		MethodInfo methodInfo = AccessTools.Method(type, "DrawList");
		if (methodInfo == null || !methodInfo.IsGenericMethodDefinition)
		{
			return null;
		}
		return methodInfo.MakeGenericMethod(type2);
	}

	[HarmonyPrefix]
	public static void Prefix(object[] __args)
	{
		if (__args == null || __args.Length < 3 || !(__args[2] is Delegate @delegate))
		{
			return;
		}
		try
		{
			ParameterInfo[] parameters = @delegate.Method.GetParameters();
			if (parameters.Length == 1)
			{
				Type parameterType = parameters[0].ParameterType;
				MethodInfo method = typeof(ChatPlayerLabelFix).GetMethod("Wrap", BindingFlags.Static | BindingFlags.NonPublic);
				if (!(method == null))
				{
					Type type = typeof(Func<, >).MakeGenericType(parameterType, typeof(string));
					object obj = ((@delegate.GetType() == type) ? @delegate : Delegate.CreateDelegate(type, @delegate.Target, @delegate.Method));
					__args[2] = method.MakeGenericMethod(parameterType).Invoke(null, new object[1] { obj });
				}
			}
		}
		catch
		{
		}
	}

	private static Func<T, string> Wrap<T>(Func<T, string> old)
	{
		return delegate(T player)
		{
			string original = ((old == null) ? "" : old(player));
			return MpJoinSession.Relabel(player, original);
		};
	}
}
