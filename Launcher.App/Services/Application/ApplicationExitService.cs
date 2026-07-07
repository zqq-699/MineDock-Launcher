namespace Launcher.App.Services;

public sealed class ApplicationExitService : IApplicationExitService
{
    public void Shutdown()
    {
        System.Windows.Application.Current?.Shutdown();
    }
}
