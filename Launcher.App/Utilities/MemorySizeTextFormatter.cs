using Launcher.App.Resources;

namespace Launcher.App.Utilities;

internal static class MemorySizeTextFormatter
{
    public static string Format(int memoryMb)
    {
        if (memoryMb >= 1024)
            return FormatGb(memoryMb);

        return string.Format(Strings.Settings_MemorySizeMbFormat, memoryMb);
    }

    public static string FormatGb(double memoryMb)
    {
        return string.Format(Strings.Settings_MemorySizeGbFormat, memoryMb / 1024d);
    }
}
