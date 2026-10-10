using Verse;

namespace RaidAnimalFix;

internal static class RngIsolation
{
	public static void Begin(ref bool state)
	{
		state = false;
		if (MpHelper.IsMultiplayer())
		{
			Rand.PushState();
			state = true;
		}
	}

	public static void End(bool state)
	{
		if (state)
		{
			Rand.PopState();
		}
	}

	public static void BeginSeeded(int seed, ref bool state)
	{
		state = false;
		if (MpHelper.IsMultiplayer())
		{
			Rand.PushState(seed);
			state = true;
		}
	}

	public static int SeedOf(ThingComp comp)
	{
		try
		{
			if (comp != null && comp.parent != null)
			{
				return comp.parent.thingIDNumber;
			}
		}
		catch
		{
		}
		return 0;
	}
}
