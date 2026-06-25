using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Download;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels.Resources;

public sealed partial class ResourcesPageViewModel : ObservableObject
{
    private readonly ILogger<ResourcesPageViewModel>? logger;

    public ResourcesPageViewModel(
        IResourceCatalogService? resourceCatalogService = null,
        ILogger<ResourcesPageViewModel>? logger = null,
        IUiDispatcher? uiDispatcher = null,
        IGameVersionService? gameVersionService = null,
        IGameInstanceService? gameInstanceService = null,
        IStatusService? statusService = null,
        IFilePickerService? filePickerService = null,
        IFloatingMessageService? floatingMessageService = null,
        DownloadTasksPageViewModel? downloadTasksPage = null)
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

        ModPage = new ResourcesModPageViewModel(
            this,
            resourceCatalogService,
            logger,
            uiDispatcher,
            gameVersionService,
            gameInstanceService,
            statusService,
            filePickerService,
            floatingMessageService,
            downloadTasksPage);
        ModPage.PropertyChanged += ModPage_PropertyChanged;
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
    [NotifyPropertyChangedFor(nameof(IsModSearchVisible))]
    [NotifyPropertyChangedFor(nameof(IsModProjectDetailsStep))]
    [NotifyPropertyChangedFor(nameof(PageTitleIconSource))]
    private ResourcesSectionItem? selectedSection;

    [ObservableProperty]
    private ResourcesSectionViewModelBase? currentSectionViewModel;

    public string PageTitle => IsModsSection
        ? ModPage.PageTitle
        : SelectedSection?.Title ?? Strings.Page_Resources;

    public bool IsModsSection => SelectedSection?.Id == "mods";

    public bool IsModSearchVisible => IsModsSection && (ModPage.IsProjectListStep || ModPage.IsProjectVersionsStep);

    public bool IsModProjectDetailsStep => IsModsSection && ModPage.IsProjectContentStep;

    public string? PageTitleIconSource => IsModsSection ? ModPage.PageTitleIconSource : null;

    public string ActiveModSearchQuery
    {
        get => ModPage.IsProjectVersionsStep
            ? ModPage.AvailableVersionSearchQuery
            : ModPage.SearchQuery;
        set
        {
            if (ModPage.IsProjectVersionsStep)
                ModPage.AvailableVersionSearchQuery = value;
            else
                ModPage.SearchQuery = value;

            OnPropertyChanged();
        }
    }

    public void BeginEnsureCurrentSectionLoaded()
    {
        if (IsModsSection)
            ModPage.BeginEnsureProjectsLoaded();
    }

    public async Task OpenModsForInstanceAsync(GameInstance instance)
    {
        var modsSection = Sections.FirstOrDefault(section => section.Id == "mods") ?? Sections[0];
        SelectSection(modsSection, logSelection: false);
        logger?.LogInformation(
            "Opening resources mod section from instance. InstanceId={InstanceId}, MinecraftVersion={MinecraftVersion}, Loader={Loader}",
            instance.Id,
            instance.MinecraftVersion,
            instance.Loader);
        await ModPage.ApplyInstanceFiltersAsync(instance);
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

        if (section.Id != "mods")
            ModPage.ResetToProjectList();

        if (logSelection)
            logger?.LogInformation("Resources section selected. SectionId={SectionId}", section.Id);

        if (logSelection && section.Id == "mods")
            ModPage.BeginEnsureProjectsLoaded();
    }

    private void ModPage_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ResourcesModPageViewModel.CurrentStep)
            or nameof(ResourcesModPageViewModel.SelectedProject)
            or nameof(ResourcesModPageViewModel.PageTitle)
            or nameof(ResourcesModPageViewModel.PageTitleIconSource))
        {
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(PageTitleIconSource));
            OnPropertyChanged(nameof(IsModSearchVisible));
            OnPropertyChanged(nameof(IsModProjectDetailsStep));
            OnPropertyChanged(nameof(ActiveModSearchQuery));
        }

        if (e.PropertyName is nameof(ResourcesModPageViewModel.SearchQuery)
            or nameof(ResourcesModPageViewModel.AvailableVersionSearchQuery))
        {
            OnPropertyChanged(nameof(ActiveModSearchQuery));
        }
    }
}
