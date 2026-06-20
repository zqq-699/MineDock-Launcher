using System.Windows.Media;
using Launcher.App.Services;

namespace Launcher.Tests.Helpers;

internal sealed class FakeThemeService : IThemeService
{
    public EffectiveTheme EffectiveTheme { get; private set; } = EffectiveTheme.Dark;

    public string? LastTheme { get; private set; }

    public bool LastFollowSystem { get; private set; }

    public int ApplyCount { get; private set; }

    public event EventHandler<EffectiveThemeChangedEventArgs>? EffectiveThemeChanged;

    public void ApplyPreference(string? theme, bool followSystem)
    {
        LastTheme = theme;
        LastFollowSystem = followSystem;
        ApplyCount++;
        var oldTheme = EffectiveTheme;
        EffectiveTheme = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase) && !followSystem
            ? EffectiveTheme.Light
            : EffectiveTheme.Dark;
        if (oldTheme != EffectiveTheme)
            EffectiveThemeChanged?.Invoke(this, new EffectiveThemeChangedEventArgs(oldTheme, EffectiveTheme));
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
