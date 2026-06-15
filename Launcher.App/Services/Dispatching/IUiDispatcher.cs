namespace Launcher.App.Services;

public interface IUiDispatcher
{
    bool HasAccess { get; }

    void Post(Action action);

    void Invoke(Action action);
}

