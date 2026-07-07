namespace Launcher.App.Services;

public interface IExternalLinkService
{
    bool TryOpen(string url);
}
