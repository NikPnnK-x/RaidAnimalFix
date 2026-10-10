using System;

namespace RaidAnimalFix;

internal static class UiSoundMute
{
	[ThreadStatic]
	public static int Depth;
}
