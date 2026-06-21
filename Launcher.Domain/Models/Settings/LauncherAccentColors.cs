namespace Launcher.Domain.Models;

public static class LauncherAccentColors
{
    public const string Blue = "Blue";
    public const string Cyan = "Cyan";
    public const string Green = "Green";
    public const string Emerald = "Emerald";
    public const string Purple = "Purple";
    public const string Pink = "Pink";
    public const string Orange = "Orange";
    public const string Amber = "Amber";

    public static IReadOnlyList<string> All { get; } =
    [
        Blue,
        Cyan,
        Green,
        Emerald,
        Purple,
        Pink,
        Orange,
        Amber
    ];

    public static string Normalize(string? accentColor)
    {
        foreach (var knownAccentColor in All)
        {
            if (string.Equals(knownAccentColor, accentColor, StringComparison.OrdinalIgnoreCase))
                return knownAccentColor;
        }

        return Blue;
    }
}
