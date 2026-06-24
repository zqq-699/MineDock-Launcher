using Launcher.App.Resources;

namespace Launcher.App.ViewModels.Resources;

public sealed class ResourcesWorldsPageViewModel : ResourcesSectionViewModelBase
{
    public ResourcesWorldsPageViewModel(ResourcesPageViewModel parent)
        : base(parent, Strings.Resources_SectionWorlds)
    {
    }
}
