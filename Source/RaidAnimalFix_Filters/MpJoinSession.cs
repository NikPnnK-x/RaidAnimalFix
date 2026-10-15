using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RaidAnimalFix;

internal static class MpJoinSession
{
	private static bool _resolved;

	private static FieldInfo _playersField;

	private static FieldInfo _usernameField;

	private static FieldInfo _steamNameField;

	private static FieldInfo _simulatingField;

	private static FieldInfo _statusField;

	private static FieldInfo _typeField;

	private static FieldInfo _mpUsername;

	private static FieldInfo _mpSettings;

	private static FieldInfo _hostUsername;

	private static PropertyInfo _localServer;

	private static PropertyInfo _tickSimulating;

	private static MethodInfo _serverGetPlayer;

	private static MethodInfo _playerDisconnect;

	private static Type _disconnectReason;

	private static object _kickReason;

	private static float _cacheAt = -1f;

	private static bool _cacheHold;

	private static string _cacheUser = "";

	private static string _cacheName = "";

	// Сколько реального времени уже держим паузу ради конкретного игрока.
	// Застрявший статус Simulating не должен замораживать сессию навсегда.
	private const float MaxHoldSecondsPerJoiner = 90f;

	private static readonly Dictionary<string, float> _holdSince = new Dictionary<string, float>();

	private static void Resolve()
	{
		if (_resolved)
		{
			return;
		}
		_resolved = true;
		try
		{
			Type type = RafTypes.Find("Multiplayer.Client.Multiplayer");
			Type type2 = RafTypes.Find("Multiplayer.Client.MultiplayerSession");
			Type type3 = RafTypes.Find("Multiplayer.Client.PlayerInfo");
			Type type4 = RafTypes.Find("Multiplayer.Client.TickPatch");
			Type type5 = RafTypes.Find("Multiplayer.Common.MultiplayerServer");
			Type type6 = RafTypes.Find("Multiplayer.Common.ServerPlayer");
			_disconnectReason = RafTypes.Find("Multiplayer.Common.MpDisconnectReason");
			if (type2 != null)
			{
				_playersField = type2.GetField("players", BindingFlags.Instance | BindingFlags.Public);
			}
			if (type3 != null)
			{
				_usernameField = type3.GetField("username", BindingFlags.Instance | BindingFlags.Public);
				_steamNameField = type3.GetField("steamPersonaName", BindingFlags.Instance | BindingFlags.Public);
				_simulatingField = type3.GetField("simulating", BindingFlags.Instance | BindingFlags.Public);
				_statusField = type3.GetField("status", BindingFlags.Instance | BindingFlags.Public);
				_typeField = type3.GetField("type", BindingFlags.Instance | BindingFlags.Public);
			}
			if (type != null)
			{
				_mpUsername = type.GetField("username", BindingFlags.Static | BindingFlags.Public);
				_mpSettings = type.GetField("settings", BindingFlags.Static | BindingFlags.Public);
				_localServer = type.GetProperty("LocalServer", BindingFlags.Static | BindingFlags.Public);
			}
			if (type4 != null)
			{
				_tickSimulating = type4.GetProperty("Simulating", BindingFlags.Static | BindingFlags.Public);
			}
			if (type5 != null)
			{
				_hostUsername = type5.GetField("hostUsername", BindingFlags.Instance | BindingFlags.Public);
				_serverGetPlayer = type5.GetMethod("GetPlayer", new Type[1] { typeof(string) });
			}
			if (type6 != null && _disconnectReason != null)
			{
				_playerDisconnect = type6.GetMethod("Disconnect", new Type[2]
				{
					_disconnectReason,
					typeof(byte[])
				});
			}
			if (_disconnectReason != null)
			{
				_kickReason = Enum.Parse(_disconnectReason, "Kick");
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] MpJoinSession resolve failed: " + ex.Message);
		}
	}

	public static bool IsHost()
	{
		Resolve();
		try
		{
			return _localServer != null && _localServer.GetValue(null, null) != null;
		}
		catch
		{
			return false;
		}
	}

	public static bool IsLocalSimulating()
	{
		Resolve();
		try
		{
			return _tickSimulating != null && (bool)_tickSimulating.GetValue(null, null);
		}
		catch
		{
			return false;
		}
	}

	public static string LocalUsername()
	{
		Resolve();
		try
		{
			return (_mpUsername == null) ? "" : ((_mpUsername.GetValue(null) as string) ?? "");
		}
		catch
		{
			return "";
		}
	}

	public static string DisplayName(object player)
	{
		if (player == null)
		{
			return "?";
		}
		Resolve();
		string text = ReadString(player, _usernameField);
		string text2 = ReadString(player, _steamNameField);
		if (text2.Length > 0 && text2 != "[unknown]" && text2 != text)
		{
			return text2;
		}
		if (text.Length <= 0)
		{
			return "?";
		}
		return text;
	}

	public static string UsernameOf(object player)
	{
		if (player != null)
		{
			return ReadString(player, _usernameField);
		}
		return "";
	}

	public static string Relabel(object player, string original)
	{
		if (player == null || string.IsNullOrEmpty(original))
		{
			return original;
		}
		string text = UsernameOf(player);
		string text2 = DisplayName(player);
		if (text2 == text || text.Length == 0)
		{
			return original;
		}
		if (original.Length >= text.Length && original.StartsWith(text))
		{
			return text2 + original.Substring(text.Length);
		}
		return original;
	}

	public static bool ShouldHoldTicks()
	{
		if (!MpHelper.IsMultiplayer())
		{
			return false;
		}
		// Хост не ждёт: его отставание тормозит всех остальных.
		if (IsHost())
		{
			return false;
		}
		if (IsLocalSimulating())
		{
			return false;
		}
		Refresh();
		return _cacheHold;
	}

	public static bool TryGetJoiner(out string display, out string username)
	{
		Refresh();
		display = _cacheName;
		username = _cacheUser;
		if (_cacheHold)
		{
			return username.Length > 0;
		}
		return false;
	}

	public static void OnClickPlayer(object player)
	{
		if (player == null || !MpHelper.IsMultiplayer())
		{
			return;
		}
		string user = UsernameOf(player);
		string name = DisplayName(player);
		if (user.Length == 0)
		{
			return;
		}
		List<FloatMenuOption> list = new List<FloatMenuOption>();
		string label = ((name == user) ? name : (name + " (" + user + ")"));
		list.Add(new FloatMenuOption(label, null));
		if (IsHost() && !IsSelfOrHost(user))
		{
			list.Add(new FloatMenuOption("RAF_KickPlayer".Translate(name), delegate
			{
				AskKick(user, name);
			}));
		}
		if (list.Count > 1)
		{
			Find.WindowStack.Add(new FloatMenu(list));
		}
	}

	public static void AskKick(string username, string display)
	{
		if (!string.IsNullOrEmpty(username) && IsHost() && !IsSelfOrHost(username))
		{
			Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("RAF_KickConfirm".Translate(display ?? username), delegate
			{
				Kick(username);
			}, destructive: true));
		}
	}

	public static void Kick(string username)
	{
		Resolve();
		if (string.IsNullOrEmpty(username) || !IsHost())
		{
			return;
		}
		try
		{
			object obj = ((_localServer == null) ? null : _localServer.GetValue(null, null));
			if (obj != null && !(_serverGetPlayer == null) && !(_playerDisconnect == null) && _kickReason != null)
			{
				// Список игроков сервера живёт в серверном потоке: менять его
				// из главного потока Unity нельзя — только через Enqueue.
				object server = obj;
				Action kick = delegate
				{
					try
					{
						object result = _serverGetPlayer.Invoke(server, new object[1] { username });
						if (result == null)
						{
							TryFindPlayerBySteamName(server, username, ref result);
						}
						if (result != null)
						{
							_playerDisconnect.Invoke(result, new object[2] { _kickReason, null });
							Log.Message("[RaidAnimalFix] kicked " + username);
						}
					}
					catch (Exception ex2)
					{
						Log.Warning("[RaidAnimalFix] kick failed: " + ex2.Message);
					}
				};
				MethodInfo enqueue = AccessTools.Method(server.GetType(), "Enqueue", new Type[1] { typeof(Action) });
				if (enqueue != null)
				{
					enqueue.Invoke(server, new object[1] { kick });
				}
				else
				{
					kick();
				}
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] kick failed: " + ex.Message);
		}
	}

	public static void TryFindPlayerBySteamName(object server, string name, ref object result)
	{
		if (result != null || server == null || string.IsNullOrEmpty(name))
		{
			return;
		}
		Resolve();
		try
		{
			FieldInfo field = server.GetType().GetField("playerManager", BindingFlags.Instance | BindingFlags.Public);
			object obj = ((field == null) ? null : field.GetValue(server));
			if (obj == null)
			{
				return;
			}
			PropertyInfo property = obj.GetType().GetProperty("Players", BindingFlags.Instance | BindingFlags.Public);
			IList list = ((property == null) ? null : (property.GetValue(obj, null) as IList));
			if (list == null)
			{
				return;
			}
			for (int i = 0; i < list.Count; i++)
			{
				object obj2 = list[i];
				if (obj2 != null)
				{
					FieldInfo field2 = obj2.GetType().GetField("steamPersonaName", BindingFlags.Instance | BindingFlags.Public);
					string text = ((field2 == null) ? "" : ((field2.GetValue(obj2) as string) ?? ""));
					if (text.Length > 0 && string.Equals(text, name, StringComparison.OrdinalIgnoreCase))
					{
						result = obj2;
						break;
					}
				}
			}
		}
		catch
		{
		}
	}

	public static void DrawBanner()
	{
		try
		{
			if (MpHelper.IsMultiplayer() && !IsLocalSimulating() && TryGetJoiner(out var display, out var username))
			{
				GameFont font = Text.Font;
				TextAnchor anchor = Text.Anchor;
				Text.Font = GameFont.Small;
				string text = "RAF_PlayerJoining".Translate(display);
				Vector2 vector = Text.CalcSize(text);
				bool flag = IsHost() && !IsSelfOrHost(username);
				float num = (flag ? 90f : 0f);
				float num2 = vector.x + 24f;
				float x = ((float)UI.screenWidth - num2 - num) * 0.5f;
				Rect rect = new Rect(x, 6f, num2, 28f);
				Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.6f));
				Text.Anchor = TextAnchor.MiddleCenter;
				Widgets.Label(rect, text);
				Text.Anchor = anchor;
				Text.Font = font;
				if (flag && Widgets.ButtonText(new Rect(rect.xMax + 4f, 6f, 86f, 28f), "RAF_Kick".Translate(), drawBackground: true, doMouseoverSound: false))
				{
					AskKick(username, display);
				}
			}
		}
		catch
		{
		}
	}

	private static void Refresh()
	{
		float realtimeSinceStartup = Time.realtimeSinceStartup;
		if (realtimeSinceStartup - _cacheAt < 0.2f)
		{
			return;
		}
		_cacheAt = realtimeSinceStartup;
		_cacheHold = false;
		_cacheUser = "";
		_cacheName = "";
		Resolve();
		try
		{
			object session = MpHelper.GetSession();
			if (session == null || _playersField == null || !(_playersField.GetValue(session) is IList list))
			{
				return;
			}
			string text = LocalUsername();
			List<string> joining = new List<string>();
			for (int i = 0; i < list.Count; i++)
			{
				object obj = list[i];
				if (obj == null || IsArbiter(obj))
				{
					continue;
				}
				string text2 = ReadString(obj, _usernameField);
				if (text2.Length != 0 && !(text2 == text))
				{
					// Только реально загружающийся игрок (status == Simulating).
					// Флаг simulating ставит и «догонялка» у обычного игрока —
					// держать по нему значит устроить пинг-понг пауз между клиентами.
					string text3 = ((_statusField == null) ? "" : Convert.ToString(_statusField.GetValue(obj)));
					if (text3 == "Simulating")
					{
						joining.Add(text2);
						if (!_holdSince.TryGetValue(text2, out var since))
						{
							since = realtimeSinceStartup;
							_holdSince[text2] = since;
						}
						if (!_cacheHold && realtimeSinceStartup - since < MaxHoldSecondsPerJoiner)
						{
							_cacheHold = true;
							_cacheUser = text2;
							_cacheName = DisplayName(obj);
						}
					}
				}
			}
			if (_holdSince.Count > joining.Count)
			{
				List<string> done = new List<string>();
				foreach (string key in _holdSince.Keys)
				{
					if (!joining.Contains(key))
					{
						done.Add(key);
					}
				}
				for (int j = 0; j < done.Count; j++)
				{
					_holdSince.Remove(done[j]);
				}
			}
		}
		catch
		{
		}
	}

	private static bool IsArbiter(object player)
	{
		if (_typeField == null || player == null)
		{
			return false;
		}
		try
		{
			return Convert.ToString(_typeField.GetValue(player)) == "Arbiter";
		}
		catch
		{
			return false;
		}
	}

	private static bool IsSelfOrHost(string username)
	{
		if (string.IsNullOrEmpty(username))
		{
			return true;
		}
		if (username == LocalUsername())
		{
			return true;
		}
		Resolve();
		try
		{
			object obj = ((_localServer == null) ? null : _localServer.GetValue(null, null));
			if (obj == null || _hostUsername == null)
			{
				return false;
			}
			return _hostUsername.GetValue(obj) is string text && text == username;
		}
		catch
		{
			return false;
		}
	}

	private static string ReadString(object obj, FieldInfo field)
	{
		if (obj == null || field == null)
		{
			return "";
		}
		try
		{
			return (field.GetValue(obj) as string) ?? "";
		}
		catch
		{
			return "";
		}
	}
}
