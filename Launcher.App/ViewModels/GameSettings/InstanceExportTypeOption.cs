namespace Launcher.App.ViewModels.GameSettings;

public sealed class InstanceExportTypeOption
{
    public InstanceExportTypeOption(string id, string title)
    {
        Id = id;
        Title = title;
    }

    public string Id { get; }

    public string Title { get; }

    public override string ToString()
    {
        return Title;
    }
}
