using System.Windows.Media;

namespace Launcher.App.Services;

public interface IThemeService
{
    EffectiveTheme EffectiveTheme { get; }

    event EventHandler<EffectiveThemeChangedEventArgs>? EffectiveThemeChanged;

    void ApplyPreference(string? theme, bool followSystem);

    object? GetResource(object key);

    Brush? GetBrush(object key);

    Color? GetColor(object key);
}
