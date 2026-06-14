using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Models;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels;

public sealed partial class GameManagementViewModel : ObservableObject
{
    private readonly ISettingsService settingsService;
    private readonly IGameVersionService gameVersionService;
    private readonly IGameInstanceService instanceService;
    private readonly IModService modService;
    private readonly IModrinthService modrinthService;
    private readonly IStatusService statusService;
    private readonly IReadOnlyDictionary<LoaderKind, ILoaderProvider> loaderProviders;
    private LauncherSettings settings = new();
    private readonly object refreshInstancesSync = new();
    private Task? refreshInstancesTask;
    private bool hasLoadedInstances;
    private bool suppressSelectedInstanceModRefresh;
    private int modRefreshVersion;

    [ObservableProperty]
    private GameInstance? selectedInstance;

    [ObservableProperty]
    private MinecraftVersionInfo? selectedMinecraftVersion;

    [ObservableProperty]
    private LoaderKind selectedLoader = LoaderKind.Vanilla;

    [ObservableProperty]
    private LoaderVersionInfo? selectedLoaderVersion;

    [ObservableProperty]
    private string newInstanceName = string.Empty;

    [ObservableProperty]
    private string modSearchQuery = string.Empty;

    [ObservableProperty]
    private ModrinthProject? selectedModrinthProject;

    [ObservableProperty]
    private double progressPercent;

    public GameManagementViewModel(
        ISettingsService settingsService,
        IGameVersionService gameVersionService,
        IGameInstanceService instanceService,
        IModService modService,
        IModrinthService modrinthService,
        IEnumerable<ILoaderProvider> loaderProviders,
        IStatusService statusService)
    {
        this.settingsService = settingsService;
        this.gameVersionService = gameVersionService;
        this.instanceService = instanceService;
        this.modService = modService;
        this.modrinthService = modrinthService;
        this.statusService = statusService;
        this.loaderProviders = loaderProviders.ToDictionary(provider => provider.Kind);

        foreach (var provider in this.loaderProviders.Values)
        {
            LoaderItems.Add(new NavigationItem
            {
                Page = provider.Kind.ToString(),
                Title = provider.DisplayName,
                Icon = provider.Kind is LoaderKind.Vanilla ? "\uE7C3" : "\uE8B7",
                Loader = provider.Kind
            });
        }
    }

    public ObservableCollection<GameInstance> Instances { get; } = [];
    public ObservableCollection<MinecraftVersionInfo> MinecraftVersions { get; } = [];
    public ObservableCollection<NavigationItem> LoaderItems { get; } = [];
    public ObservableCollection<LoaderVersionInfo> LoaderVersions { get; } = [];
    public ObservableCollection<LocalMod> Mods { get; } = [];
    public ObservableCollection<ModrinthProject> ModrinthProjects { get; } = [];

    public async Task InitializeAsync(LauncherSettings launcherSettings)
    {
        settings = launcherSettings;
        await EnsureInstancesLoadedAsync();
    }

    public async Task EnsureInstancesLoadedAsync()
    {
        if (hasLoadedInstances)
            return;

        await RefreshInstancesAsync();
    }

    public void SelectLoader(LoaderKind loader)
    {
        SelectedLoader = loader;
    }

    [RelayCommand]
    private async Task LoadMinecraftVersionsAsync()
    {
        ReportStatus("正在获取 Minecraft 版本列表...");
        MinecraftVersions.Clear();

        foreach (var version in await gameVersionService.GetVersionsAsync())
        {
            if (version.Type.Equals("Release", StringComparison.OrdinalIgnoreCase) || version.Name.StartsWith("1."))
                MinecraftVersions.Add(version);
        }

        SelectedMinecraftVersion ??= MinecraftVersions.FirstOrDefault();
        ReportStatus($"已加载 {MinecraftVersions.Count} 个版本");
    }

    [RelayCommand]
    public async Task RefreshInstancesAsync()
    {
        Task refreshTask;
        lock (refreshInstancesSync)
        {
            refreshTask = refreshInstancesTask ??= RefreshInstancesCoreAsync();
        }

        try
        {
            await refreshTask;
        }
        finally
        {
            lock (refreshInstancesSync)
            {
                if (ReferenceEquals(refreshInstancesTask, refreshTask))
                    refreshInstancesTask = null;
            }
        }
    }

    private async Task RefreshInstancesCoreAsync()
    {
        var loadedInstances = await instanceService.GetInstancesAsync();
        var previousSelectedId = SelectedInstance?.Id;

        Instances.Clear();
        foreach (var instance in loadedInstances)
            Instances.Add(instance);

        var selected = !string.IsNullOrWhiteSpace(settings.DefaultInstanceId)
            ? Instances.FirstOrDefault(instance => instance.Id == settings.DefaultInstanceId)
            : null;
        selected ??= !string.IsNullOrWhiteSpace(previousSelectedId)
            ? Instances.FirstOrDefault(instance => instance.Id == previousSelectedId)
            : null;
        selected ??= Instances.FirstOrDefault();

        suppressSelectedInstanceModRefresh = true;
        try
        {
            SelectedInstance = selected;
        }
        finally
        {
            suppressSelectedInstanceModRefresh = false;
        }

        await RefreshModsAsync();
        hasLoadedInstances = true;
    }

    [RelayCommand]
    private async Task LoadLoaderVersionsAsync()
    {
        LoaderVersions.Clear();
        SelectedLoaderVersion = null;

        if (SelectedMinecraftVersion is null || !loaderProviders.TryGetValue(SelectedLoader, out var provider))
            return;

        if (!provider.IsImplemented)
        {
            ReportStatus($"{provider.DisplayName} 后续版本接入");
            return;
        }

        ReportStatus($"正在读取 {provider.DisplayName} 版本...");
        foreach (var version in await provider.GetLoaderVersionsAsync(SelectedMinecraftVersion.Name))
            LoaderVersions.Add(version);

        SelectedLoaderVersion = LoaderVersions.FirstOrDefault(v => v.IsStable) ?? LoaderVersions.FirstOrDefault();
        ReportStatus($"{provider.DisplayName} 可用版本已加载");
    }

    [RelayCommand]
    private async Task CreateInstanceAsync()
    {
        if (SelectedMinecraftVersion is null)
        {
            ReportStatus("请先选择 Minecraft 版本");
            return;
        }

        var progress = CreateProgress();
        var loaderVersion = SelectedLoader is LoaderKind.Vanilla ? null : SelectedLoaderVersion?.Version;
        var instance = await instanceService.CreateInstanceAsync(
            SelectedMinecraftVersion.Name,
            SelectedLoader,
            loaderVersion,
            NewInstanceName,
            progress);

        Instances.Add(instance);
        SelectedInstance = instance;
        ReportStatus($"实例 {instance.Name} 已创建");
    }

    [RelayCommand]
    private async Task RefreshModsAsync()
    {
        var refreshVersion = Interlocked.Increment(ref modRefreshVersion);
        var selectedInstance = SelectedInstance;

        if (selectedInstance is null)
        {
            Mods.Clear();
            return;
        }

        var loadedMods = await modService.GetModsAsync(selectedInstance);
        if (refreshVersion != modRefreshVersion
            || !string.Equals(selectedInstance.Id, SelectedInstance?.Id, StringComparison.Ordinal))
        {
            return;
        }

        Mods.Clear();
        foreach (var mod in loadedMods)
            Mods.Add(mod);
    }

    [RelayCommand]
    private async Task ToggleModAsync(LocalMod mod)
    {
        await modService.SetEnabledAsync(mod, !mod.IsEnabled);
        await RefreshModsAsync();
    }

    [RelayCommand]
    private async Task DeleteModAsync(LocalMod mod)
    {
        await modService.DeleteAsync(mod);
        await RefreshModsAsync();
    }

    [RelayCommand]
    private async Task ImportModFromPathAsync(string path)
    {
        if (SelectedInstance is null || string.IsNullOrWhiteSpace(path))
            return;

        await modService.ImportAsync(SelectedInstance, path);
        await RefreshModsAsync();
        ReportStatus("本地 Mod 已导入");
    }

    [RelayCommand]
    private async Task SearchModsAsync()
    {
        if (SelectedInstance is null)
        {
            ReportStatus("请先选择一个实例");
            return;
        }

        ReportStatus("正在搜索 Modrinth...");
        ModrinthProjects.Clear();
        foreach (var project in await modrinthService.SearchModsAsync(ModSearchQuery, SelectedInstance.MinecraftVersion, SelectedInstance.Loader))
            ModrinthProjects.Add(project);

        ReportStatus($"找到 {ModrinthProjects.Count} 个资源");
    }

    [RelayCommand]
    private async Task InstallSelectedModAsync()
    {
        if (SelectedInstance is null || SelectedModrinthProject is null)
            return;

        await modrinthService.InstallLatestCompatibleAsync(SelectedModrinthProject, SelectedInstance, CreateProgress());
        await RefreshModsAsync();
        ReportStatus($"{SelectedModrinthProject.Title} 已安装");
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        await settingsService.SaveAsync(settings);
        ReportStatus("启动器设置已保存");
    }

    [RelayCommand]
    private async Task SaveInstanceAsync()
    {
        if (SelectedInstance is null)
            return;

        await instanceService.SaveInstanceAsync(SelectedInstance);
        ReportStatus("实例设置已保存");
    }

    [RelayCommand]
    private async Task SetDefaultInstanceAsync()
    {
        if (SelectedInstance is null)
            return;

        settings.DefaultInstanceId = SelectedInstance.Id;
        await settingsService.SaveAsync(settings);
        ReportStatus($"{SelectedInstance.Name} 已设为默认实例");
    }

    partial void OnSelectedLoaderChanged(LoaderKind value)
    {
        _ = LoadLoaderVersionsAsync();
    }

    partial void OnSelectedMinecraftVersionChanged(MinecraftVersionInfo? value)
    {
        _ = LoadLoaderVersionsAsync();
    }

    partial void OnSelectedInstanceChanged(GameInstance? value)
    {
        if (!suppressSelectedInstanceModRefresh)
            _ = RefreshModsAsync();
    }

    private IProgress<LauncherProgress> CreateProgress()
    {
        return new Progress<LauncherProgress>(progress =>
        {
            ReportStatus(progress.Message);
            ProgressPercent = progress.Percent ?? 0;
        });
    }

    private void ReportStatus(string message)
    {
        statusService.Report(message);
    }
}
