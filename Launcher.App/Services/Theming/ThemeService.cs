using System.Windows;
using System.Windows.Media;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace Launcher.App.Services;

public sealed class ThemeService : IThemeService, IDisposable
{
    private const string DarkThemeSource =
        "pack://application:,,,/MineDock_Launcher_x64;component/Resources/Themes/Dark.xaml";

    private const string LightThemeSource =
        "pack://application:,,,/MineDock_Launcher_x64;component/Resources/Themes/Light.xaml";

    private const string AccentThemeSourcePrefix =
        "pack://application:,,,/MineDock_Launcher_x64;component/Resources/Themes/Accents/";

    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger<ThemeService> logger;
    private string preferredTheme = LauncherDefaults.DefaultTheme;
    private string preferredAccentColor = LauncherDefaults.DefaultAccentColor;
    private bool followSystem = true;
    private int backgroundOpacityPercent = LauncherDefaults.DefaultLauncherBackgroundOpacityPercent;
    private bool disableBackgroundBlur;
    private bool hasAppliedTheme;
    private bool isDisposed;

    public ThemeService(IUiDispatcher uiDispatcher, ILogger<ThemeService>? logger = null)
    {
        this.uiDispatcher = uiDispatcher;
        this.logger = logger ?? NullLogger<ThemeService>.Instance;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
    }

    public EffectiveTheme EffectiveTheme { get; private set; } = EffectiveTheme.Dark;

    public bool BackgroundBlurDisabled => disableBackgroundBlur;

    public event EventHandler<EffectiveThemeChangedEventArgs>? EffectiveThemeChanged;

    public event EventHandler<BackgroundBlurDisabledChangedEventArgs>? BackgroundBlurDisabledChanged;

    public void ApplyPreference(
        string? theme,
        bool followSystem,
        int backgroundOpacityPercent,
        bool disableBackgroundBlur)
    {
        var backgroundBlurDisabledChanged = this.disableBackgroundBlur != disableBackgroundBlur;
        preferredTheme = NormalizeTheme(theme);
        this.followSystem = followSystem;
        this.backgroundOpacityPercent = NormalizeBackgroundOpacity(backgroundOpacityPercent);
        this.disableBackgroundBlur = disableBackgroundBlur;
        var nextTheme = ResolveEffectiveTheme(preferredTheme, followSystem);
        uiDispatcher.Invoke(() => ApplyEffectiveTheme(nextTheme));
        if (backgroundBlurDisabledChanged)
            BackgroundBlurDisabledChanged?.Invoke(this, new BackgroundBlurDisabledChangedEventArgs(this.disableBackgroundBlur));
    }

    public void ApplyAccent(string? accentColor)
    {
        var normalizedAccentColor = LauncherAccentColors.Normalize(accentColor);
        if (!string.IsNullOrWhiteSpace(accentColor)
            && !string.Equals(accentColor, normalizedAccentColor, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Invalid launcher accent color preference encountered. AccentColor={AccentColor} FallingBackTo={FallbackAccentColor}",
                accentColor,
                normalizedAccentColor);
        }

        preferredAccentColor = normalizedAccentColor;
        uiDispatcher.Invoke(() => ApplyAccentCore(preferredAccentColor));
    }

    public void ApplyBackgroundOpacity(int opacityPercent)
    {
        backgroundOpacityPercent = NormalizeBackgroundOpacity(opacityPercent);
        uiDispatcher.Invoke(() => ApplyBackgroundOpacityCore(backgroundOpacityPercent));
    }

    public void ApplyBackgroundBlurDisabled(bool disabled)
    {
        var changed = disableBackgroundBlur != disabled;
        disableBackgroundBlur = disabled;
        uiDispatcher.Invoke(() => ApplyBackgroundBlurDisabledCore(disableBackgroundBlur));
        if (changed)
            BackgroundBlurDisabledChanged?.Invoke(this, new BackgroundBlurDisabledChangedEventArgs(disableBackgroundBlur));
    }

    public object? GetResource(object key)
    {
        object? resource = null;
        uiDispatcher.Invoke(() =>
        {
            resource = global::System.Windows.Application.Current?.TryFindResource(key);
        });
        return resource;
    }

    public Brush? GetBrush(object key)
    {
        return GetResource(key) as Brush;
    }

    public Color? GetColor(object key)
    {
        return GetResource(key) as Color?;
    }

    public void Dispose()
    {
        if (isDisposed)
            return;

        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        isDisposed = true;
    }

    private void ApplyEffectiveTheme(EffectiveTheme nextTheme)
    {
        var application = global::System.Windows.Application.Current;
        if (application is null)
            return;

        var dictionaries = application.Resources.MergedDictionaries;
        for (var index = dictionaries.Count - 1; index >= 0; index--)
        {
            if (IsThemeDictionary(dictionaries[index]))
                dictionaries.RemoveAt(index);
        }

        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(GetThemeSource(nextTheme), UriKind.Absolute)
        });
        ApplyBackgroundOpacityCore(backgroundOpacityPercent);
        ApplyBackgroundBlurDisabledCore(disableBackgroundBlur);
        ApplyAccentCore(preferredAccentColor);

        var oldTheme = EffectiveTheme;
        EffectiveTheme = nextTheme;
        if (!hasAppliedTheme)
        {
            hasAppliedTheme = true;
            return;
        }

        if (oldTheme != nextTheme)
            EffectiveThemeChanged?.Invoke(this, new EffectiveThemeChangedEventArgs(oldTheme, nextTheme));
    }

    private EffectiveTheme ResolveEffectiveTheme(string theme, bool useSystemTheme)
    {
        if (useSystemTheme)
            return ResolveSystemTheme();

        return string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)
            ? EffectiveTheme.Light
            : EffectiveTheme.Dark;
    }

    private static EffectiveTheme ResolveSystemTheme()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                0);
            return value is int appsUseLightTheme && appsUseLightTheme > 0
                ? EffectiveTheme.Light
                : EffectiveTheme.Dark;
        }
        catch
        {
            return EffectiveTheme.Dark;
        }
    }

    private static string NormalizeTheme(string? theme)
    {
        return string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)
            ? "Light"
            : LauncherDefaults.DefaultTheme;
    }

    private static int NormalizeBackgroundOpacity(int opacityPercent)
    {
        return Math.Clamp(opacityPercent, 0, 100);
    }

    private static void ApplyBackgroundOpacityCore(int opacityPercent)
    {
        var application = global::System.Windows.Application.Current;
        if (application is null)
            return;

        application.Resources["Opacity.Page.Background"] = NormalizeBackgroundOpacity(opacityPercent) / 100d;
    }

    private static void ApplyBackgroundBlurDisabledCore(bool disabled)
    {
        _ = disabled;
    }

    private void ApplyAccentCore(string accentColor)
    {
        var application = global::System.Windows.Application.Current;
        if (application is null)
            return;

        var dictionaries = application.Resources.MergedDictionaries;
        for (var index = dictionaries.Count - 1; index >= 0; index--)
        {
            if (IsAccentDictionary(dictionaries[index]))
                dictionaries.RemoveAt(index);
        }

        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(GetAccentThemeSource(accentColor), UriKind.Absolute)
        });
        logger.LogInformation("Launcher accent applied. AccentColor={AccentColor}", accentColor);
    }

    private static string GetThemeSource(EffectiveTheme theme)
    {
        return theme is EffectiveTheme.Light ? LightThemeSource : DarkThemeSource;
    }

    private static string GetAccentThemeSource(string accentColor)
    {
        var normalizedAccentColor = LauncherAccentColors.Normalize(accentColor);
        return $"{AccentThemeSourcePrefix}{normalizedAccentColor}.xaml";
    }

    private static bool IsThemeDictionary(ResourceDictionary dictionary)
    {
        var source = dictionary.Source?.ToString();
        return source?.EndsWith("/Resources/Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase) == true
            || source?.EndsWith("/Resources/Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsAccentDictionary(ResourceDictionary dictionary)
    {
        var source = dictionary.Source?.ToString();
        return source?.Contains("/Resources/Themes/Accents/", StringComparison.OrdinalIgnoreCase) == true;
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (!followSystem)
            return;

        if (e.Category is not UserPreferenceCategory.General
            and not UserPreferenceCategory.VisualStyle
            and not UserPreferenceCategory.Color)
        {
            return;
        }

        uiDispatcher.Post(() => ApplyPreference(
            preferredTheme,
            followSystem,
            backgroundOpacityPercent,
            disableBackgroundBlur));
    }
}
