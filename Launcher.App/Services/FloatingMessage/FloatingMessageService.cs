namespace Launcher.App.Services;

public sealed class FloatingMessageService : IFloatingMessageService
{
    public event Action<string>? MessageRequested;

    public void Show(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        MessageRequested?.Invoke(message);
    }
}
