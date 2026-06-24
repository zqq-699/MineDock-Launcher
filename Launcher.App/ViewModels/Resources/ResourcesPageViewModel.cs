using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels.Resources;

public sealed partial class ResourcesPageViewModel : ObservableObject
{
    private readonly ILogger<ResourcesPageViewModel>? logger;

    public ResourcesPageViewModel(
        IResourceCatalogService? resourceCatalogService = null,
        ILogger<ResourcesPageViewModel>? logger = null,
        IUiDispatcher? uiDispatcher = null)
    {
        this.logger = logger;

        Sections =
        [
            new ResourcesSectionItem { Id = "mods", Title = Strings.Resources_SectionMods, IconKey = "instance_setting_page/mod" },
            new ResourcesSectionItem { Id = "resource_packs", Title = Strings.Resources_SectionResourcePacks, IconKey = "main_menu_library" },
            new ResourcesSectionItem { Id = "shader_packs", Title = Strings.Resources_SectionShaderPacks, IconKey = "instance_setting_page/shader" },
            new ResourcesSectionItem { Id = "worlds", Title = Strings.Resources_SectionWorlds, IconKey = "instance_setting_page/saves" },
            new ResourcesSectionItem { Id = "modpacks", Title = Strings.Resources_SectionModpacks, IconKey = "general/general_extention" }
        ];

        ModPage = new ResourcesModPageViewModel(this, resourceCatalogService, logger, uiDispatcher);
        ResourcePacksPage = new ResourcesResourcePacksPageViewModel(this);
        ShaderPacksPage = new ResourcesShaderPacksPageViewModel(this);
        WorldsPage = new ResourcesWorldsPageViewModel(this);
        ModpacksPage = new ResourcesModpacksPageViewModel(this);

        SelectSection(Sections[0], logSelection: false);
    }

    public ObservableCollection<ResourcesSectionItem> Sections { get; }

    public ResourcesModPageViewModel ModPage { get; }

    public ResourcesResourcePacksPageViewModel ResourcePacksPage { get; }

    public ResourcesShaderPacksPageViewModel ShaderPacksPage { get; }

    public ResourcesWorldsPageViewModel WorldsPage { get; }

    public ResourcesModpacksPageViewModel ModpacksPage { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    [NotifyPropertyChangedFor(nameof(IsModsSection))]
    private ResourcesSectionItem? selectedSection;

    [ObservableProperty]
    private ResourcesSectionViewModelBase? currentSectionViewModel;

    public string PageTitle => SelectedSection?.Title ?? Strings.Page_Resources;

    public bool IsModsSection => SelectedSection?.Id == "mods";

    public void BeginEnsureCurrentSectionLoaded()
    {
        if (IsModsSection)
            ModPage.BeginEnsureProjectsLoaded();
    }

    [RelayCommand]
    private void SelectSection(ResourcesSectionItem? section)
    {
        SelectSection(section, logSelection: true);
    }

    private void SelectSection(ResourcesSectionItem? section, bool logSelection)
    {
        if (section is null || ReferenceEquals(SelectedSection, section))
            return;

        foreach (var item in Sections)
            item.IsSelected = ReferenceEquals(item, section);

        CurrentSectionViewModel = section.Id switch
        {
            "mods" => ModPage,
            "resource_packs" => ResourcePacksPage,
            "shader_packs" => ShaderPacksPage,
            "worlds" => WorldsPage,
            "modpacks" => ModpacksPage,
            _ => ModPage
        };
        SelectedSection = section;

        if (logSelection)
            logger?.LogInformation("Resources section selected. SectionId={SectionId}", section.Id);

        if (logSelection && section.Id == "mods")
            ModPage.BeginEnsureProjectsLoaded();
    }
}
