using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.App.Resources;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels.Resources;

public sealed partial class ResourcesModPageViewModel : ResourcesSectionViewModelBase
{
    private readonly ILogger? logger;

    public ResourcesModPageViewModel(ResourcesPageViewModel parent, ILogger? logger = null)
        : base(parent, Strings.Resources_SectionMods)
    {
        this.logger = logger;

        VersionOptions = [new ResourcesFilterOptionItem { Id = "all", Title = Strings.Resources_ModFilterAllVersions }];
        LoaderOptions = [new ResourcesFilterOptionItem { Id = "all", Title = Strings.Resources_ModFilterAllLoaders }];
        SourceOptions = [new ResourcesFilterOptionItem { Id = "all", Title = Strings.Resources_ModFilterAllSources }];
        TypeOptions = [new ResourcesFilterOptionItem { Id = "all", Title = Strings.Resources_ModFilterAllTypes }];

        selectedVersionOption = VersionOptions[0];
        selectedLoaderOption = LoaderOptions[0];
        selectedSourceOption = SourceOptions[0];
        selectedTypeOption = TypeOptions[0];
    }

    public ObservableCollection<ResourcesFilterOptionItem> VersionOptions { get; }

    public ObservableCollection<ResourcesFilterOptionItem> LoaderOptions { get; }

    public ObservableCollection<ResourcesFilterOptionItem> SourceOptions { get; }

    public ObservableCollection<ResourcesFilterOptionItem> TypeOptions { get; }

    [ObservableProperty]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    private ResourcesFilterOptionItem? selectedVersionOption;

    [ObservableProperty]
    private ResourcesFilterOptionItem? selectedLoaderOption;

    [ObservableProperty]
    private ResourcesFilterOptionItem? selectedSourceOption;

    [ObservableProperty]
    private ResourcesFilterOptionItem? selectedTypeOption;

    partial void OnSelectedVersionOptionChanged(ResourcesFilterOptionItem? value)
    {
        LogFilterSelection("version", value);
    }

    partial void OnSelectedLoaderOptionChanged(ResourcesFilterOptionItem? value)
    {
        LogFilterSelection("loader", value);
    }

    partial void OnSelectedSourceOptionChanged(ResourcesFilterOptionItem? value)
    {
        LogFilterSelection("source", value);
    }

    partial void OnSelectedTypeOptionChanged(ResourcesFilterOptionItem? value)
    {
        LogFilterSelection("type", value);
    }

    private void LogFilterSelection(string filterId, ResourcesFilterOptionItem? option)
    {
        if (option is null)
            return;

        logger?.LogInformation(
            "Resources mod filter selected. FilterId={FilterId}, OptionId={OptionId}",
            filterId,
            option.Id);
    }
}
