namespace Launcher.App.Services;

public interface IFloatingMessageService
{
    event Action<string>? MessageRequested;

    void Show(string message);
}
