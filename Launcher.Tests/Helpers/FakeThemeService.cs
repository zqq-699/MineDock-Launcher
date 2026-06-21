using System.Windows.Media;
using Launcher.App.Services;

namespace Launcher.Tests.Helpers;

internal sealed class FakeThemeService : IThemeService
{
    public EffectiveTheme EffectiveTheme { get; private set; } = EffectiveTheme.Dark;

    public bool BackgroundBlurDisabled => LastDisableBackgroundBlur;

    public string? LastTheme { get; private set; }

    public bool LastFollowSystem { get; private set; }

    public int LastBackgroundOpacityPercent { get; private set; }

    public bool LastDisableBackgroundBlur { get; private set; }

    public int ApplyCount { get; private set; }

    public int ApplyBackgroundOpacityCount { get; private set; }

    public int ApplyBackgroundBlurDisabledCount { get; private set; }

    public event EventHandler<EffectiveThemeChangedEventArgs>? EffectiveThemeChanged;

    public event EventHandler<BackgroundBlurDisabledChangedEventArgs>? BackgroundBlurDisabledChanged;

    public void ApplyPreference(
        string? theme,
        bool followSystem,
        int backgroundOpacityPercent,
        bool disableBackgroundBlur)
    {
        LastTheme = theme;
        LastFollowSystem = followSystem;
        LastBackgroundOpacityPercent = backgroundOpacityPercent;
        var backgroundBlurDisabledChanged = LastDisableBackgroundBlur != disableBackgroundBlur;
        LastDisableBackgroundBlur = disableBackgroundBlur;
        ApplyCount++;
        var oldTheme = EffectiveTheme;
        EffectiveTheme = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase) && !followSystem
            ? EffectiveTheme.Light
            : EffectiveTheme.Dark;
        if (oldTheme != EffectiveTheme)
            EffectiveThemeChanged?.Invoke(this, new EffectiveThemeChangedEventArgs(oldTheme, EffectiveTheme));
        if (backgroundBlurDisabledChanged)
            BackgroundBlurDisabledChanged?.Invoke(this, new BackgroundBlurDisabledChangedEventArgs(disableBackgroundBlur));
    }

    public void ApplyBackgroundOpacity(int opacityPercent)
    {
        LastBackgroundOpacityPercent = opacityPercent;
        ApplyBackgroundOpacityCount++;
    }

    public void ApplyBackgroundBlurDisabled(bool disabled)
    {
        var changed = LastDisableBackgroundBlur != disabled;
        LastDisableBackgroundBlur = disabled;
        ApplyBackgroundBlurDisabledCount++;
        if (changed)
            BackgroundBlurDisabledChanged?.Invoke(this, new BackgroundBlurDisabledChangedEventArgs(disabled));
    }

    public object? GetResource(object key)
    {
        return null;
    }

    public Brush? GetBrush(object key)
    {
        return null;
    }

    public Color? GetColor(object key)
    {
        return null;
    }
}
