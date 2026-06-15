namespace Launcher.App.Services;

public sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public static ImmediateUiDispatcher Instance { get; } = new();

    private ImmediateUiDispatcher()
    {
    }

    public bool HasAccess => true;

    public void Post(Action action)
    {
        action();
    }

    public void Invoke(Action action)
    {
        action();
    }
}

