using System.Windows.Threading;

namespace Launcher.App.Services;

public sealed class WpfUiDispatcher : IUiDispatcher
{
    public bool HasAccess => global::System.Windows.Application.Current?.Dispatcher?.CheckAccess() ?? true;

    public void Post(Action action)
    {
        var dispatcher = global::System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action, DispatcherPriority.Background);
    }

    public void Invoke(Action action)
    {
        var dispatcher = global::System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }
}
