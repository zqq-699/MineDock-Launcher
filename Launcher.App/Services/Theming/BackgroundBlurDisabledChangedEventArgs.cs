namespace Launcher.App.Services;

public sealed class BackgroundBlurDisabledChangedEventArgs : EventArgs
{
    public BackgroundBlurDisabledChangedEventArgs(bool disabled)
    {
        Disabled = disabled;
    }

    public bool Disabled { get; }
}
