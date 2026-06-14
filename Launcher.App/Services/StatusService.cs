namespace Launcher.App.Services;

public sealed class StatusService : IStatusService
{
    public event Action<string>? MessageReported;

    public void Report(string message)
    {
        MessageReported?.Invoke(message);
    }
}
