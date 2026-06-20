namespace Launcher.App.Services;

public sealed class EffectiveThemeChangedEventArgs : EventArgs
{
    public EffectiveThemeChangedEventArgs(EffectiveTheme oldTheme, EffectiveTheme newTheme)
    {
        OldTheme = oldTheme;
        NewTheme = newTheme;
    }

    public EffectiveTheme OldTheme { get; }

    public EffectiveTheme NewTheme { get; }
}
