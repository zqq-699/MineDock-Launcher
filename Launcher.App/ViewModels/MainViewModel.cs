using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Models;
using Launcher.Core.Models;
using Launcher.Core.Services;

namespace Launcher.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ISettingsService settingsService;
    private readonly IGameVersionService gameVersionService;
    private readonly IGameInstanceService instanceService;
    private readonly ILaunchService launchService;
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
    private string statusMessage = "准备就绪";

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

    [ObservableProperty]
    private LauncherAccount? selectedAccount;

    [ObservableProperty]
    private bool isAddAccountDialogOpen;

    [ObservableProperty]
    private AccountTypeOption? selectedAccountTypeOption;

    [ObservableProperty]
    private string addAccountDialogStep = "Type";

    [ObservableProperty]
    private string newOfflineAccountName = string.Empty;

    [ObservableProperty]
    private bool isNewOfflineAccountNameInvalid;

    [ObservableProperty]
    private bool isDeleteAccountDialogOpen;

    [ObservableProperty]
    private LauncherAccount? accountPendingDelete;

    public bool IsAccountTypeStep => AddAccountDialogStep == "Type";
    public bool IsOfflineNameStep => AddAccountDialogStep == "OfflineName";
    public string AddAccountDialogTitle => IsOfflineNameStep ? "\u79bb\u7ebf\u8d26\u6237" : "\u6dfb\u52a0\u8d26\u6237";
    public string AddAccountDialogSubtitle => IsOfflineNameStep
        ? "\u8f93\u5165\u8981\u6dfb\u52a0\u7684\u79bb\u7ebf\u8d26\u6237\u540d\u3002"
        : "\u9009\u62e9\u8981\u6dfb\u52a0\u7684\u8d26\u6237\u7c7b\u578b\u3002";

    public ObservableCollection<NavigationItem> NavigationItems { get; } =
    [
        new() { Page = "Account", Title = "\u8d26\u6237", Icon = "\uE77B" },
        new() { Page = "Home", Title = "主页", Icon = "\uE80F" },
        new() { Page = "Download", Title = "游戏下载", Icon = "\uE896" },
        new() { Page = "GameSettings", Title = "游戏设置", Icon = "\uE713" },
        new() { Page = "Resources", Title = "资源中心", Icon = "\uE8F1" },
        new() { Page = "Settings", Title = "设置", Icon = "\uE713" }
    ];

    public ObservableCollection<NavigationItem> SecondaryItems { get; } = [];
    public ObservableCollection<LauncherAccount> Accounts { get; } = [];
    public ObservableCollection<AccountTypeOption> AccountTypeOptions { get; } =
    [
        new()
        {
            Kind = "Offline",
            Title = "\u79bb\u7ebf\u8d26\u6237",
            Description = "\u4f7f\u7528\u672c\u5730\u7528\u6237\u540d\u8fdb\u5165\u6e38\u620f\u3002",
            Icon = "\uE77B"
        },
        new()
        {
            Kind = "Microsoft",
            Title = "\u6b63\u7248\u8d26\u6237",
            Description = "\u901a\u8fc7 Microsoft \u8d26\u6237\u767b\u5f55\u5e76\u83b7\u53d6\u76ae\u80a4\u5934\u50cf\u3002",
            Icon = "\uE72E"
        }
    ];
    public ObservableCollection<GameInstance> Instances { get; } = [];
    public ObservableCollection<MinecraftVersionInfo> MinecraftVersions { get; } = [];
    public ObservableCollection<NavigationItem> LoaderItems { get; } = [];
    public ObservableCollection<LoaderVersionInfo> LoaderVersions { get; } = [];
    public ObservableCollection<LocalMod> Mods { get; } = [];
    public ObservableCollection<ModrinthProject> ModrinthProjects { get; } = [];

    public MainViewModel(
        ISettingsService settingsService,
        IGameVersionService gameVersionService,
        IGameInstanceService instanceService,
        ILaunchService launchService,
        IModService modService,
        IModrinthService modrinthService,
        IEnumerable<ILoaderProvider> loaderProviders)
    {
        this.settingsService = settingsService;
        this.gameVersionService = gameVersionService;
        this.instanceService = instanceService;
        this.launchService = launchService;
        this.modService = modService;
        this.modrinthService = modrinthService;
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

        UpdateNavigationSelection();
    }

    [RelayCommand]
    public async Task InitializeAsync()
    {
        UpdateNavigationSelection();
        Settings = await settingsService.LoadAsync();
        IsMenuExpanded = Settings.IsMenuExpanded;
        LoadAccountsFromSettings();
        await RefreshInstancesAsync();
        await LoadMinecraftVersionsAsync();
        UpdateSecondaryItems();
        UpdateNavigationSelection();
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

    public void SelectAccount(LauncherAccount account)
    {
        SelectedAccount = account;
        foreach (var item in Accounts)
            item.IsSelected = ReferenceEquals(item, account);

        UpdateAccountNavigationAvatar();
    }

    public void OpenAddAccountDialog()
    {
        AddAccountDialogStep = "Type";
        NewOfflineAccountName = string.Empty;
        IsNewOfflineAccountNameInvalid = false;
        SelectedAccountTypeOption = AccountTypeOptions.FirstOrDefault();
        IsAddAccountDialogOpen = true;
    }

    public void CancelAddAccountDialog()
    {
        IsAddAccountDialogOpen = false;
        AddAccountDialogStep = "Type";
        NewOfflineAccountName = string.Empty;
        IsNewOfflineAccountNameInvalid = false;
    }

    public void ConfirmAddAccountDialog()
    {
        if (SelectedAccountTypeOption is null)
            return;

        if (IsAccountTypeStep)
        {
            if (SelectedAccountTypeOption.Kind is "Offline")
            {
                AddAccountDialogStep = "OfflineName";
                return;
            }

            IsAddAccountDialogOpen = false;
            StatusMessage = "\u6b63\u7248\u8d26\u6237\u767b\u5f55\u540e\u7eed\u63a5\u5165";
            return;
        }

        var accountName = NewOfflineAccountName.Trim();
        if (string.IsNullOrWhiteSpace(accountName))
        {
            IsNewOfflineAccountNameInvalid = true;
            StatusMessage = "\u8bf7\u8f93\u5165\u79bb\u7ebf\u8d26\u6237\u540d";
            return;
        }

        var account = new LauncherAccount
        {
            Id = $"offline-{Guid.NewGuid():N}",
            DisplayName = accountName,
            IsOffline = true
        };

        Accounts.Add(account);
        SelectAccount(account);

        NewOfflineAccountName = string.Empty;
        IsNewOfflineAccountNameInvalid = false;
        AddAccountDialogStep = "Type";
        IsAddAccountDialogOpen = false;
        StatusMessage = $"\u5df2\u6dfb\u52a0\u79bb\u7ebf\u8d26\u6237 {accountName}";
    }

    public void OpenDeleteAccountDialog(LauncherAccount account)
    {
        AccountPendingDelete = account;
        IsDeleteAccountDialogOpen = true;
    }

    public void CancelDeleteAccountDialog()
    {
        IsDeleteAccountDialogOpen = false;
        AccountPendingDelete = null;
    }

    public void ConfirmDeleteAccountDialog()
    {
        if (AccountPendingDelete is null)
            return;

        var deletedName = AccountPendingDelete.DisplayName;
        if (ReferenceEquals(SelectedAccount, AccountPendingDelete))
        {
            SelectedAccount = null;
            UpdateAccountNavigationAvatar();
        }

        Accounts.Remove(AccountPendingDelete);
        IsDeleteAccountDialogOpen = false;
        AccountPendingDelete = null;
        StatusMessage = $"\u5df2\u5220\u9664\u8d26\u6237 {deletedName}";
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
        StatusMessage = "正在获取 Minecraft 版本列表...";
        MinecraftVersions.Clear();

        foreach (var version in await gameVersionService.GetVersionsAsync())
        {
            if (version.Type.Equals("Release", StringComparison.OrdinalIgnoreCase) || version.Name.StartsWith("1."))
                MinecraftVersions.Add(version);
        }

        SelectedMinecraftVersion ??= MinecraftVersions.FirstOrDefault();
        StatusMessage = $"已加载 {MinecraftVersions.Count} 个版本";
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
            StatusMessage = $"{provider.DisplayName} 后续版本接入";
            return;
        }

        StatusMessage = $"正在读取 {provider.DisplayName} 版本...";
        foreach (var version in await provider.GetLoaderVersionsAsync(SelectedMinecraftVersion.Name))
            LoaderVersions.Add(version);

        SelectedLoaderVersion = LoaderVersions.FirstOrDefault(v => v.IsStable) ?? LoaderVersions.FirstOrDefault();
        StatusMessage = $"{provider.DisplayName} 可用版本已加载";
    }

    [RelayCommand]
    private async Task CreateInstanceAsync()
    {
        if (SelectedMinecraftVersion is null)
        {
            StatusMessage = "请先选择 Minecraft 版本";
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
        StatusMessage = $"实例 {instance.Name} 已创建";
    }

    [RelayCommand]
    private async Task LaunchAsync()
    {
        if (SelectedInstance is null)
        {
            StatusMessage = "还没有可启动的实例";
            return;
        }

        await launchService.LaunchAsync(SelectedInstance, Settings, CreateProgress());
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
        StatusMessage = "本地 Mod 已导入";
    }

    [RelayCommand]
    private async Task SearchModsAsync()
    {
        if (SelectedInstance is null)
        {
            StatusMessage = "请先选择一个实例";
            return;
        }

        StatusMessage = "正在搜索 Modrinth...";
        ModrinthProjects.Clear();
        foreach (var project in await modrinthService.SearchModsAsync(ModSearchQuery, SelectedInstance.MinecraftVersion, SelectedInstance.Loader))
            ModrinthProjects.Add(project);

        StatusMessage = $"找到 {ModrinthProjects.Count} 个资源";
    }

    [RelayCommand]
    private async Task InstallSelectedModAsync()
    {
        if (SelectedInstance is null || SelectedModrinthProject is null)
            return;

        await modrinthService.InstallLatestCompatibleAsync(SelectedModrinthProject, SelectedInstance, CreateProgress());
        await RefreshModsAsync();
        StatusMessage = $"{SelectedModrinthProject.Title} 已安装";
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        Settings.IsMenuExpanded = IsMenuExpanded;
        await settingsService.SaveAsync(Settings);
        StatusMessage = "启动器设置已保存";
    }

    [RelayCommand]
    private async Task SaveInstanceAsync()
    {
        if (SelectedInstance is null)
            return;

        await instanceService.SaveInstanceAsync(SelectedInstance);
        StatusMessage = "实例设置已保存";
    }

    [RelayCommand]
    private async Task SetDefaultInstanceAsync()
    {
        if (SelectedInstance is null)
            return;

        Settings.DefaultInstanceId = SelectedInstance.Id;
        await settingsService.SaveAsync(Settings);
        StatusMessage = $"{SelectedInstance.Name} 已设为默认实例";
    }

    partial void OnSelectedLoaderChanged(LoaderKind value)
    {
        _ = LoadLoaderVersionsAsync();
    }

    partial void OnCurrentPageChanged(string value)
    {
        UpdateNavigationSelection();
    }

    partial void OnSelectedMinecraftVersionChanged(MinecraftVersionInfo? value)
    {
        _ = LoadLoaderVersionsAsync();
    }

    partial void OnSelectedInstanceChanged(GameInstance? value)
    {
        _ = RefreshModsAsync();
    }

    partial void OnAddAccountDialogStepChanged(string value)
    {
        OnPropertyChanged(nameof(IsAccountTypeStep));
        OnPropertyChanged(nameof(IsOfflineNameStep));
        OnPropertyChanged(nameof(AddAccountDialogTitle));
        OnPropertyChanged(nameof(AddAccountDialogSubtitle));
    }

    partial void OnNewOfflineAccountNameChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            IsNewOfflineAccountNameInvalid = false;
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

        var items = CurrentPage switch
        {
            "Download" => LoaderItems,
            "GameSettings" =>
            [
                new NavigationItem { Page = "GameSettings", Title = "实例列表", Icon = "\uE8A5" },
                new NavigationItem { Page = "GameSettings", Title = "Java/内存", Icon = "\uE950" },
                new NavigationItem { Page = "GameSettings", Title = "目录管理", Icon = "\uE8B7" }
            ],
            "Resources" =>
            [
                new NavigationItem { Page = "Resources", Title = "Mod", Icon = "\uE8F1" },
                new NavigationItem { Page = "Resources", Title = "光影", Icon = "\uE790" },
                new NavigationItem { Page = "Resources", Title = "地图", Icon = "\uE707" }
            ],
            "Settings" =>
            [
                new NavigationItem { Page = "Settings", Title = "外观主题", Icon = "\uE771" },
                new NavigationItem { Page = "Settings", Title = "默认设置", Icon = "\uE713" },
                new NavigationItem { Page = "Settings", Title = "关于", Icon = "\uE946" }
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

    private void LoadAccountsFromSettings()
    {
        Accounts.Clear();
        Accounts.Add(new LauncherAccount
        {
            Id = "offline",
            DisplayName = Settings.OfflineUsername,
            IsOffline = true
        });

        SelectedAccount = null;
        UpdateAccountNavigationAvatar();
    }

    private void UpdateAccountNavigationAvatar()
    {
        var accountItem = NavigationItems.FirstOrDefault(item => item.Page == "Account");
        if (accountItem is not null)
            accountItem.AvatarUrl = SelectedAccount?.AvatarUrl;
    }
}
