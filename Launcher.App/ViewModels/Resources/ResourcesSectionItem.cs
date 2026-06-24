using CommunityToolkit.Mvvm.ComponentModel;

namespace Launcher.App.ViewModels.Resources;

public sealed partial class ResourcesSectionItem : ObservableObject
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string IconKey { get; init; }

    [ObservableProperty]
    private bool isSelected;
}
