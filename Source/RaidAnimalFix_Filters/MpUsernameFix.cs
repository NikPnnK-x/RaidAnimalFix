using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Verse;
using Verse.Steam;

namespace RaidAnimalFix;

internal static class MpUsernameFix
{
	public static void ApplyNow()
	{
		try
		{
			Type type = RafTypes.Find("Multiplayer.Client.Multiplayer");
			if (type == null)
			{
				return;
			}
			FieldInfo field = type.GetField("username", BindingFlags.Static | BindingFlags.Public);
			FieldInfo field2 = type.GetField("settings", BindingFlags.Static | BindingFlags.Public);
			if (field == null)
			{
				return;
			}
			string text = field.GetValue(null) as string;
			if (!IsPlaceholder(text))
			{
				return;
			}
			string text2 = FromSteam();
			if (string.IsNullOrEmpty(text2) || text2 == text)
			{
				return;
			}
			field.SetValue(null, text2);
			object obj = ((field2 == null) ? null : field2.GetValue(null));
			if (obj != null)
			{
				FieldInfo field3 = obj.GetType().GetField("username", BindingFlags.Instance | BindingFlags.Public);
				if (field3 != null)
				{
					field3.SetValue(obj, text2);
				}
				MethodInfo method = obj.GetType().GetMethod("Write", Type.EmptyTypes);
				if (method != null)
				{
					method.Invoke(obj, null);
				}
			}
			Log.Message("[RaidAnimalFix] MP username: " + (text ?? "(null)") + " -> " + text2);
		}
		catch (Exception ex)
		{
			Log.Warning("[RaidAnimalFix] username fix failed: " + ex.Message);
		}
	}

	private static bool IsPlaceholder(string name)
	{
		if (string.IsNullOrEmpty(name) || name.Length < 3)
		{
			return true;
		}
		if (!name.StartsWith("Player") || name.Length < 7 || name.Length > 10)
		{
			return false;
		}
		for (int i = 6; i < name.Length; i++)
		{
			if (name[i] < '0' || name[i] > '9')
			{
				return false;
			}
		}
		return true;
	}

	private static string FromSteam()
	{
		string raw = null;
		try
		{
			if (SteamManager.Initialized)
			{
				raw = SteamUtility.SteamPersonaName;
			}
		}
		catch
		{
		}
		string text = Sanitize(Transliterate(raw));
		if (text != null && text.Length >= 3 && HasLetter(text))
		{
			if (text.Length <= 15)
			{
				return text;
			}
			return text.Substring(0, 15);
		}
		string text2 = SteamAccountSuffix();
		if (text2 != null)
		{
			return "s" + text2;
		}
		if (text != null && text.Length >= 3)
		{
			if (text.Length <= 15)
			{
				return text;
			}
			return text.Substring(0, 15);
		}
		return null;
	}

	private static string SteamAccountSuffix()
	{
		try
		{
			Type type = RafTypes.Find("Steamworks.SteamUser");
			MethodInfo methodInfo = ((type == null) ? null : type.GetMethod("GetSteamID", BindingFlags.Static | BindingFlags.Public));
			if (methodInfo == null)
			{
				return null;
			}
			object obj = methodInfo.Invoke(null, null);
			if (obj == null)
			{
				return null;
			}
			FieldInfo field = obj.GetType().GetField("m_SteamID", BindingFlags.Instance | BindingFlags.Public);
			if (field == null)
			{
				return null;
			}
			ulong num = Convert.ToUInt64(field.GetValue(obj));
			if (num == 0)
			{
				return null;
			}
			return (num % 10000000uL).ToString("D7");
		}
		catch
		{
			return null;
		}
	}

	private static bool HasLetter(string s)
	{
		for (int i = 0; i < s.Length; i++)
		{
			if ((s[i] >= 'A' && s[i] <= 'Z') || (s[i] >= 'a' && s[i] <= 'z'))
			{
				return true;
			}
		}
		return false;
	}

	private static string Sanitize(string s)
	{
		if (string.IsNullOrEmpty(s))
		{
			return null;
		}
		StringBuilder stringBuilder = new StringBuilder(s.Length);
		foreach (char c in s)
		{
			if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')
			{
				stringBuilder.Append(c);
			}
		}
		if (stringBuilder.Length != 0)
		{
			return stringBuilder.ToString();
		}
		return null;
	}

	private static string Transliterate(string raw)
	{
		if (string.IsNullOrEmpty(raw))
		{
			return null;
		}
		StringBuilder stringBuilder = new StringBuilder(raw.Length * 2);
		for (int i = 0; i < raw.Length; i++)
		{
			string text = MapChar(raw[i]);
			if (text != null)
			{
				stringBuilder.Append(text);
			}
		}
		if (stringBuilder.Length != 0)
		{
			return stringBuilder.ToString();
		}
		return null;
	}

	private static string MapChar(char c)
	{
		if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')
		{
			return c.ToString();
		}
		switch (c)
		{
		case 'А':
		case 'а':
			return "a";
		case 'Б':
		case 'б':
			return "b";
		case 'В':
		case 'в':
			return "v";
		case 'Г':
		case 'г':
			return "g";
		case 'Д':
		case 'д':
			return "d";
		case 'Ё':
		case 'Е':
		case 'е':
		case 'ё':
			return "e";
		case 'Ж':
		case 'ж':
			return "zh";
		case 'З':
		case 'з':
			return "z";
		case 'И':
		case 'и':
			return "i";
		case 'Й':
		case 'й':
			return "y";
		case 'К':
		case 'к':
			return "k";
		case 'Л':
		case 'л':
			return "l";
		case 'М':
		case 'м':
			return "m";
		case 'Н':
		case 'н':
			return "n";
		case 'О':
		case 'о':
			return "o";
		case 'П':
		case 'п':
			return "p";
		case 'Р':
		case 'р':
			return "r";
		case 'С':
		case 'с':
			return "s";
		case 'Т':
		case 'т':
			return "t";
		case 'У':
		case 'у':
			return "u";
		case 'Ф':
		case 'ф':
			return "f";
		case 'Х':
		case 'х':
			return "h";
		case 'Ц':
		case 'ц':
			return "ts";
		case 'Ч':
		case 'ч':
			return "ch";
		case 'Ш':
		case 'ш':
			return "sh";
		case 'Щ':
		case 'щ':
			return "sch";
		case 'Ы':
		case 'ы':
			return "y";
		case 'Э':
		case 'э':
			return "e";
		case 'Ю':
		case 'ю':
			return "yu";
		case 'Я':
		case 'я':
			return "ya";
		case 'Ъ':
		case 'Ь':
		case 'ъ':
		case 'ь':
			return "";
		case 'Ä':
		case 'ä':
			return "a";
		case 'Ö':
		case 'ö':
			return "o";
		case 'Ü':
		case 'ü':
			return "u";
		case 'ß':
			return "ss";
		case 'É':
		case 'è':
		case 'é':
		case 'ê':
		case 'ë':
			return "e";
		case 'Á':
		case 'à':
		case 'á':
		case 'â':
			return "a";
		case 'Í':
		case 'ì':
		case 'í':
		case 'î':
			return "i";
		case 'Ó':
		case 'ò':
		case 'ó':
		case 'ô':
			return "o";
		case 'Ú':
		case 'ù':
		case 'ú':
		case 'û':
			return "u";
		case 'Ñ':
		case 'ñ':
			return "n";
		case 'Ç':
		case 'ç':
			return "c";
		case ' ':
		case '-':
		case '.':
			return "_";
		default:
			return null;
		}
	}
}
