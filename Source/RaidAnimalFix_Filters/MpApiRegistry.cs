using System;
using Multiplayer.API;
using Verse;

namespace RaidAnimalFix;

/// <summary>
/// Регистрация всех [SyncMethod]/[SyncField]/[SyncWorker] этой сборки через
/// MP API. Вызывается один раз при старте, на всех клиентах одинаково
/// (MP требует одинаковый порядок регистрации). Multiplayer — обязательная
/// зависимость мода, его 0MultiplayerAPI.dll всегда загружен.
/// </summary>
internal static class MpApiRegistry
{
	private static bool _done;

	public static bool Enabled
	{
		get
		{
			try
			{
				return MP.enabled;
			}
			catch
			{
				return false;
			}
		}
	}

	public static void RegisterAll()
	{
		if (_done)
		{
			return;
		}
		_done = true;
		try
		{
			if (!MP.enabled)
			{
				return;
			}
			MP.RegisterAll(typeof(MpApiRegistry).Assembly);
			// Порядок фиксирован — одинаков у всех клиентов.
			ReplaceStuffDesignatorSyncWorker.Register();
			GiddyUpMpSync.Register();
			Log.Message("[RaidAnimalFix] MP API: синхронные методы/поля сборки Filters зарегистрированы");
		}
		catch (Exception ex)
		{
			Log.Error("[RaidAnimalFix] MP API registration failed: " + ex);
		}
	}
}
