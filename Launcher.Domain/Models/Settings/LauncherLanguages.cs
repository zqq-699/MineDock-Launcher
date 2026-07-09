namespace Launcher.Domain.Models;

public static class LauncherLanguages
{
    public const string SimplifiedChinese = "zh-Hans";
    public const string English = "en";

    public static string Normalize(string? language)
    {
        if (string.Equals(language, English, StringComparison.OrdinalIgnoreCase))
            return English;

        if (string.Equals(language, SimplifiedChinese, StringComparison.OrdinalIgnoreCase))
            return SimplifiedChinese;

        return LauncherDefaults.DefaultLauncherLanguage;
    }
}
