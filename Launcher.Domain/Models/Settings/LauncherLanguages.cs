namespace Launcher.Domain.Models;

public static class LauncherLanguages
{
    public const string SimplifiedChinese = "zh-Hans";
    public const string TraditionalChinese = "zh-Hant";
    public const string Japanese = "ja-JP";
    public const string English = "en";

    public static string Normalize(string? language)
    {
        if (string.Equals(language, English, StringComparison.OrdinalIgnoreCase))
            return English;

        if (string.Equals(language, TraditionalChinese, StringComparison.OrdinalIgnoreCase))
            return TraditionalChinese;

        if (string.Equals(language, Japanese, StringComparison.OrdinalIgnoreCase))
            return Japanese;

        if (string.Equals(language, SimplifiedChinese, StringComparison.OrdinalIgnoreCase))
            return SimplifiedChinese;

        return LauncherDefaults.DefaultLauncherLanguage;
    }
}
