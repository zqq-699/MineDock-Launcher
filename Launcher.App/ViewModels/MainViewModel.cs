using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Models;
using Launcher.App.Services;
using Launcher.Core.Models;
using Launcher.Core.Services;

namespace Launcher.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ISettingsService settingsService;
    private readonly IGameVersionService gameVersionService;
    private readonly IGameInstanceService instanceService;
    private readonly IModService modService;
    private readonly IModrinthService modrinthService;
    private readonly IReadOnlyDictionary<LoaderKind, ILoaderProvider> loaderProviders;

    [ObservableProperty]
    private LauncherSettings settings = new();

    [ObservableProperty]
    private string currentPage = "Home";

    [ObservableProperty]
    private bool isMenuExpanded;

    [ObservableProperty]
    private string statusMessage = "\u51c6\u5907\u5c31\u7eea";

    [ObservableProperty]
    private double progressPercent;

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

    public MainViewModel(
        ISettingsService settingsService,
        IGameVersionService gameVersionService,
        IGameInstanceService instanceService,
        ILaunchService launchService,
        IModService modService,
        IModrinthService modrinthService,
        IEnumerable<ILoaderProvider> loaderProviders,
        AccountPageViewModel accountPage,
        DownloadPageViewModel downloadPage,
        IStatusService statusService)
    {
        this.settingsService = settingsService;
        this.gameVersionService = gameVersionService;
        this.instanceService = instanceService;
        this.modService = modService;
        this.modrinthService = modrinthService;
        this.loaderProviders = loaderProviders.ToDictionary(provider => provider.Kind);
        AccountPage = accountPage;
        DownloadPage = downloadPage;
        HomePage = new HomePageViewModel(
            launchService,
            AccountPage,
            statusService,
            NavigateToPage,
            percent => ProgressPercent = percent);

        statusService.MessageReported += message => StatusMessage = message;
        AccountPage.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AccountPageViewModel.SelectedAccount))
                UpdateAccountNavigationAvatar();
        };

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

        UpdateNavigationSelection();
    }

    public AccountPageViewModel AccountPage { get; }

    public HomePageViewModel HomePage { get; }

    public DownloadPageViewModel DownloadPage { get; }

    public ObservableCollection<NavigationItem> NavigationItems { get; } =
    [
        new() { Page = "Account", Title = "\u8d26\u6237", Icon = "\uE77B", IconKey = "main_menu_account" },
        new() { Page = "Home", Title = "\u4e3b\u9875", Icon = "\uE80F", IconKey = "main_menu_launch" },
        new() { Page = "Download", Title = "\u6e38\u620f\u4e0b\u8f7d", Icon = "\uE896", IconKey = "main_menu_instance_download" },
        new() { Page = "GameSettings", Title = "\u6e38\u620f\u8bbe\u7f6e", Icon = "\uE713", IconKey = "main_menu_instance_setting" },
        new() { Page = "Resources", Title = "\u8d44\u6e90\u4e2d\u5fc3", Icon = "\uE8F1", IconKey = "main_menu_library" },
        new() { Page = "Settings", Title = "\u8bbe\u7f6e", Icon = "\uE713", IconKey = "main_menu_setting" }
    ];

    public ObservableCollection<NavigationItem> SecondaryItems { get; } = [];
    public ObservableCollection<GameInstance> Instances { get; } = [];
    public ObservableCollection<MinecraftVersionInfo> MinecraftVersions { get; } = [];
    public ObservableCollection<NavigationItem> LoaderItems { get; } = [];
    public ObservableCollection<LoaderVersionInfo> LoaderVersions { get; } = [];
    public ObservableCollection<LocalMod> Mods { get; } = [];
    public ObservableCollection<ModrinthProject> ModrinthProjects { get; } = [];

    [RelayCommand]
    public async Task InitializeAsync()
    {
        UpdateNavigationSelection();
        Settings = await settingsService.LoadAsync();
        IsMenuExpanded = Settings.IsMenuExpanded;
        await AccountPage.InitializeAsync(Settings);
        HomePage.SetSettings(Settings);
        await RefreshInstancesAsync();
        UpdateSecondaryItems();
        UpdateNavigationSelection();
        UpdateAccountNavigationAvatar();
        HomePage.Initialize(Settings, SelectedInstance);
    }

    [RelayCommand]
    private void Navigate(NavigationItem item)
    {
        SelectNavigationItem(item);
    }

    public void SelectNavigationItem(NavigationItem item)
    {
        if (item.Loader is LoaderKind loader)
        {
            SelectedLoader = loader;
            CurrentPage = "Download";
        }
        else
        {
            CurrentPage = item.Page;
        }

        UpdateSecondaryItems();
        UpdateNavigationSelection();
    }

    [RelayCommand]
    private async Task ToggleMenuAsync()
    {
        IsMenuExpanded = !IsMenuExpanded;
        Settings.IsMenuExpanded = IsMenuExpanded;
        await settingsService.SaveAsync(Settings);
    }

    [RelayCommand]
    private async Task LoadMinecraftVersionsAsync()
    {
        StatusMessage = "\u6b63\u5728\u83b7\u53d6 Minecraft \u7248\u672c\u5217\u8868...";
        MinecraftVersions.Clear();

        foreach (var version in await gameVersionService.GetVersionsAsync())
        {
            if (version.Type.Equals("Release", StringComparison.OrdinalIgnoreCase) || version.Name.StartsWith("1."))
                MinecraftVersions.Add(version);
        }

        SelectedMinecraftVersion ??= MinecraftVersions.FirstOrDefault();
        StatusMessage = $"\u5df2\u52a0\u8f7d {MinecraftVersions.Count} \u4e2a\u7248\u672c";
    }

    [RelayCommand]
    private async Task RefreshInstancesAsync()
    {
        Instances.Clear();
        foreach (var instance in await instanceService.GetInstancesAsync())
            Instances.Add(instance);

        SelectedInstance = await instanceService.GetDefaultInstanceAsync() ?? Instances.FirstOrDefault();
        await RefreshModsAsync();
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
            StatusMessage = $"{provider.DisplayName} \u540e\u7eed\u7248\u672c\u63a5\u5165";
            return;
        }

        StatusMessage = $"\u6b63\u5728\u8bfb\u53d6 {provider.DisplayName} \u7248\u672c...";
        foreach (var version in await provider.GetLoaderVersionsAsync(SelectedMinecraftVersion.Name))
            LoaderVersions.Add(version);

        SelectedLoaderVersion = LoaderVersions.FirstOrDefault(v => v.IsStable) ?? LoaderVersions.FirstOrDefault();
        StatusMessage = $"{provider.DisplayName} \u53ef\u7528\u7248\u672c\u5df2\u52a0\u8f7d";
    }

    [RelayCommand]
    private async Task CreateInstanceAsync()
    {
        if (SelectedMinecraftVersion is null)
        {
            StatusMessage = "\u8bf7\u5148\u9009\u62e9 Minecraft \u7248\u672c";
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
        StatusMessage = $"\u5b9e\u4f8b {instance.Name} \u5df2\u521b\u5efa";
    }

    [RelayCommand]
    private async Task RefreshModsAsync()
    {
        Mods.Clear();
        if (SelectedInstance is null)
            return;

        foreach (var mod in await modService.GetModsAsync(SelectedInstance))
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
        StatusMessage = "\u672c\u5730 Mod \u5df2\u5bfc\u5165";
    }

    [RelayCommand]
    private async Task SearchModsAsync()
    {
        if (SelectedInstance is null)
        {
            StatusMessage = "\u8bf7\u5148\u9009\u62e9\u4e00\u4e2a\u5b9e\u4f8b";
            return;
        }

        StatusMessage = "\u6b63\u5728\u641c\u7d22 Modrinth...";
        ModrinthProjects.Clear();
        foreach (var project in await modrinthService.SearchModsAsync(ModSearchQuery, SelectedInstance.MinecraftVersion, SelectedInstance.Loader))
            ModrinthProjects.Add(project);

        StatusMessage = $"\u627e\u5230 {ModrinthProjects.Count} \u4e2a\u8d44\u6e90";
    }

    [RelayCommand]
    private async Task InstallSelectedModAsync()
    {
        if (SelectedInstance is null || SelectedModrinthProject is null)
            return;

        await modrinthService.InstallLatestCompatibleAsync(SelectedModrinthProject, SelectedInstance, CreateProgress());
        await RefreshModsAsync();
        StatusMessage = $"{SelectedModrinthProject.Title} \u5df2\u5b89\u88c5";
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        Settings.IsMenuExpanded = IsMenuExpanded;
        await settingsService.SaveAsync(Settings);
        StatusMessage = "\u542f\u52a8\u5668\u8bbe\u7f6e\u5df2\u4fdd\u5b58";
    }

    [RelayCommand]
    private async Task SaveInstanceAsync()
    {
        if (SelectedInstance is null)
            return;

        await instanceService.SaveInstanceAsync(SelectedInstance);
        StatusMessage = "\u5b9e\u4f8b\u8bbe\u7f6e\u5df2\u4fdd\u5b58";
    }

    [RelayCommand]
    private async Task SetDefaultInstanceAsync()
    {
        if (SelectedInstance is null)
            return;

        Settings.DefaultInstanceId = SelectedInstance.Id;
        await settingsService.SaveAsync(Settings);
        StatusMessage = $"{SelectedInstance.Name} \u5df2\u8bbe\u4e3a\u9ed8\u8ba4\u5b9e\u4f8b";
    }

    partial void OnSelectedLoaderChanged(LoaderKind value)
    {
        _ = LoadLoaderVersionsAsync();
    }

    partial void OnCurrentPageChanged(string value)
    {
        UpdateNavigationSelection();
        if (string.Equals(value, "Download", StringComparison.OrdinalIgnoreCase))
            _ = DownloadPage.EnsureVersionsLoadedAsync();
    }

    partial void OnSelectedMinecraftVersionChanged(MinecraftVersionInfo? value)
    {
        _ = LoadLoaderVersionsAsync();
    }

    partial void OnSelectedInstanceChanged(GameInstance? value)
    {
        HomePage.SetSelectedInstance(value);
        _ = RefreshModsAsync();
    }

    private IProgress<LauncherProgress> CreateProgress()
    {
        return new Progress<LauncherProgress>(progress =>
        {
            StatusMessage = progress.Message;
            ProgressPercent = progress.Percent ?? 0;
        });
    }

    private void UpdateSecondaryItems()
    {
        SecondaryItems.Clear();

        IEnumerable<NavigationItem> items = CurrentPage switch
        {
            "GameSettings" =>
            [
                new NavigationItem { Page = "GameSettings", Title = "\u5b9e\u4f8b\u5217\u8868", Icon = "\uE8A5" },
                new NavigationItem { Page = "GameSettings", Title = "Java/\u5185\u5b58", Icon = "\uE950" },
                new NavigationItem { Page = "GameSettings", Title = "\u76ee\u5f55\u7ba1\u7406", Icon = "\uE8B7" }
            ],
            "Resources" =>
            [
                new NavigationItem { Page = "Resources", Title = "Mod", Icon = "\uE8F1" },
                new NavigationItem { Page = "Resources", Title = "\u5149\u5f71", Icon = "\uE790" },
                new NavigationItem { Page = "Resources", Title = "\u5730\u56fe", Icon = "\uE707" }
            ],
            "Settings" =>
            [
                new NavigationItem { Page = "Settings", Title = "\u5916\u89c2\u4e3b\u9898", Icon = "\uE771" },
                new NavigationItem { Page = "Settings", Title = "\u9ed8\u8ba4\u8bbe\u7f6e", Icon = "\uE713" },
                new NavigationItem { Page = "Settings", Title = "\u5173\u4e8e", Icon = "\uE946" }
            ],
            _ => []
        };

        foreach (var item in items)
            SecondaryItems.Add(item);
    }

    private void UpdateNavigationSelection()
    {
        foreach (var item in NavigationItems)
            item.IsSelected = string.Equals(item.Page, CurrentPage, StringComparison.OrdinalIgnoreCase);
    }

    private void NavigateToPage(string page)
    {
        CurrentPage = page;
        UpdateSecondaryItems();
        UpdateNavigationSelection();
    }

    private void UpdateAccountNavigationAvatar()
    {
        var accountItem = NavigationItems.FirstOrDefault(item => item.Page == "Account");
        if (accountItem is not null)
            accountItem.AvatarUrl = AccountPage.SelectedAccount?.AvatarUrl;
    }
}
