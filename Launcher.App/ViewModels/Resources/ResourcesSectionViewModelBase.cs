using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.App.Resources;

namespace Launcher.App.ViewModels.Resources;

public abstract class ResourcesSectionViewModelBase : ObservableObject
{
    protected ResourcesSectionViewModelBase(ResourcesPageViewModel parent, string title)
    {
        Parent = parent;
        Title = title;
    }

    public ResourcesPageViewModel Parent { get; }

    public string Title { get; }

    public string PlaceholderMessage => string.Format(Strings.Resources_SelectedPlaceholderMessageFormat, Title);
}
