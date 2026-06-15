using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface ILauncherStateMonitor : IDisposable
{
    event EventHandler? StateChanged;

    void Watch(LauncherSettings settings);

    void Stop();
}

