namespace Launcher.App.ViewModels.Resources;

public sealed class ResourcesListFooterStatusItem
{
    public ResourcesListFooterStatusItem(string message)
    {
        Message = message;
    }

    public string Message { get; }
}
