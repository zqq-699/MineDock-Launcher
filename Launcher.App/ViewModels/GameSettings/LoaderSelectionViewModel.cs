using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.App.Models;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.Utilities;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class LoaderSelectionViewModel : ObservableObject
{
    private readonly IGameVersionService gameVersionService;
    private readonly IStatusService statusService;
    private readonly IReadOnlyDictionary<LoaderKind, ILoaderProvider> loaderProviders;

    [ObservableProperty]
    private MinecraftVersionInfo? selectedMinecraftVersion;

    [ObservableProperty]
    private LoaderKind selectedLoader = LoaderKind.Vanilla;

    [ObservableProperty]
    private LoaderVersionInfo? selectedLoaderVersion;

    public LoaderSelectionViewModel(
        IGameVersionService gameVersionService,
        IEnumerable<ILoaderProvider> loaderProviders,
        IStatusService statusService)
    {
        this.gameVersionService = gameVersionService;
        this.statusService = statusService;
        this.loaderProviders = loaderProviders.ToDictionary(provider => provider.Kind);

        foreach (var provider in this.loaderProviders.Values)
            LoaderItems.Add(NavigationCatalog.CreateLoaderItem(provider));
    }

    public ObservableCollection<MinecraftVersionInfo> MinecraftVersions { get; } = [];
    public ObservableCollection<NavigationItem> LoaderItems { get; } = [];
    public ObservableCollection<LoaderVersionInfo> LoaderVersions { get; } = [];

    public void SelectLoader(LoaderKind loader)
    {
        SelectedLoader = loader;
    }

    public async Task LoadMinecraftVersionsAsync()
    {
        ReportStatus(Strings.Status_LoadingVersions);
        var versions = await gameVersionService.GetVersionsAsync();
        MinecraftVersions.ReplaceWith(versions.Where(IsSelectableMinecraftVersion));

        SelectedMinecraftVersion ??= MinecraftVersions.FirstOrDefault();
        ReportStatus(string.Format(Strings.Status_VersionsLoadedFormat, MinecraftVersions.Count));
    }

    public async Task LoadLoaderVersionsAsync()
    {
        LoaderVersions.Clear();
        SelectedLoaderVersion = null;

        if (SelectedMinecraftVersion is null || !loaderProviders.TryGetValue(SelectedLoader, out var provider))
            return;

        if (!provider.IsImplemented)
        {
            ReportStatus(string.Format(Strings.Status_LoaderVersionsPendingFormat, LoaderDisplayNameProvider.GetDisplayName(provider.Kind)));
            return;
        }

        ReportStatus(string.Format(Strings.Status_LoadingLoaderVersionsFormat, LoaderDisplayNameProvider.GetDisplayName(provider.Kind)));
        LoaderVersions.ReplaceWith(await provider.GetLoaderVersionsAsync(SelectedMinecraftVersion.Name));

        SelectedLoaderVersion = LoaderVersions.FirstOrDefault(v => v.IsStable) ?? LoaderVersions.FirstOrDefault();
        ReportStatus(string.Format(Strings.Status_LoaderVersionsLoadedFormat, LoaderDisplayNameProvider.GetDisplayName(provider.Kind)));
    }

    partial void OnSelectedLoaderChanged(LoaderKind value)
    {
        _ = LoadLoaderVersionsAsync();
    }

    partial void OnSelectedMinecraftVersionChanged(MinecraftVersionInfo? value)
    {
        _ = LoadLoaderVersionsAsync();
    }

    private void ReportStatus(string message)
    {
        statusService.Report(message);
    }

    private static bool IsSelectableMinecraftVersion(MinecraftVersionInfo version)
    {
        return version.Type.Equals("Release", StringComparison.OrdinalIgnoreCase) || version.Name.StartsWith("1.");
    }
}

