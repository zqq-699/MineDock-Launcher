using Launcher.App.Resources;

namespace Launcher.App.ViewModels.Resources;

public sealed class ResourcesShaderPacksPageViewModel : ResourcesSectionViewModelBase
{
    public ResourcesShaderPacksPageViewModel(ResourcesPageViewModel parent)
        : base(parent, Strings.Resources_SectionShaderPacks)
    {
    }
}
