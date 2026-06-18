namespace Launcher.Application.Services;

public sealed class GameLaunchSession
{
    private int exitHandled;

    public GameLaunchSession(string instanceId, string instanceName, Task<LaunchExitResult> exitTask)
    {
        InstanceId = instanceId;
        InstanceName = instanceName;
        ExitTask = exitTask;
    }

    public string InstanceId { get; }

    public string InstanceName { get; }

    public Task<LaunchExitResult> ExitTask { get; }

    public bool TryMarkExitHandled()
    {
        return Interlocked.Exchange(ref exitHandled, 1) == 0;
    }
}
