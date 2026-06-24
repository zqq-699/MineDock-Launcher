using Launcher.App.Resources;

namespace Launcher.App.ViewModels.Resources;

public sealed class ResourcesResourcePacksPageViewModel : ResourcesSectionViewModelBase
{
    public ResourcesResourcePacksPageViewModel(ResourcesPageViewModel parent)
        : base(parent, Strings.Resources_SectionResourcePacks)
    {
    }
}
