using Launcher.App.Resources;

namespace Launcher.App.ViewModels.Resources;

public sealed class ResourcesModpacksPageViewModel : ResourcesSectionViewModelBase
{
    public ResourcesModpacksPageViewModel(ResourcesPageViewModel parent)
        : base(parent, Strings.Resources_SectionModpacks)
    {
    }
}
