using System.Windows.Media;

namespace Launcher.App.Services;

public interface IThemeService
{
    EffectiveTheme EffectiveTheme { get; }

    bool BackgroundBlurDisabled { get; }

    event EventHandler<EffectiveThemeChangedEventArgs>? EffectiveThemeChanged;

    event EventHandler<BackgroundBlurDisabledChangedEventArgs>? BackgroundBlurDisabledChanged;

    void ApplyPreference(
        string? theme,
        bool followSystem,
        int backgroundOpacityPercent,
        bool disableBackgroundBlur);

    void ApplyBackgroundOpacity(int opacityPercent);

    void ApplyBackgroundBlurDisabled(bool disabled);

    object? GetResource(object key);

    Brush? GetBrush(object key);

    Color? GetColor(object key);
}
