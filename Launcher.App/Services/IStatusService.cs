namespace Launcher.App.Services;

public interface IStatusService
{
    event Action<string>? MessageReported;

    void Report(string message);
}
