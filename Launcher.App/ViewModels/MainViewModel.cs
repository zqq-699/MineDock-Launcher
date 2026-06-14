using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Models;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ISettingsService settingsService;
    private readonly IWindowService windowService;
    private bool hasInitialized;
    private bool isSyncingCurrentState;

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

    public MainViewModel(
        ISettingsService settingsService,
        AccountPageViewModel accountPage,
        DownloadPageViewModel downloadPage,
        DownloadTasksPageViewModel downloadTasksPage,
        GameManagementViewModel gameManagement,
        ILaunchService launchService,
        IWindowService windowService,
        IStatusService statusService)
    {
        this.settingsService = settingsService;
        this.windowService = windowService;
        AccountPage = accountPage;
        DownloadPage = downloadPage;
        DownloadTasksPage = downloadTasksPage;
        GameManagement = gameManagement;
        HomePage = new HomePageViewModel(
            launchService,
            AccountPage,
            statusService,
            NavigateToPage,
            percent => ProgressPercent = percent);

        statusService.MessageReported += message => StatusMessage = message;
        DownloadPage.InstanceInstalled += DownloadPage_InstanceInstalled;
        AccountPage.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AccountPageViewModel.SelectedAccount))
                UpdateAccountNavigationAvatar();
        };
        GameManagement.PropertyChanged += GameManagement_PropertyChanged;

        UpdateNavigationSelection();
    }

    public AccountPageViewModel AccountPage { get; }

    public HomePageViewModel HomePage { get; }

    public DownloadPageViewModel DownloadPage { get; }

    public DownloadTasksPageViewModel DownloadTasksPage { get; }

    public GameManagementViewModel GameManagement { get; }

    public NavigationItem DownloadTasksNavigationItem { get; } =
        new() { Page = "Install", Title = "\u4e0b\u8f7d", Icon = "\uE896", IconKey = "main_menu_install" };

    public ObservableCollection<NavigationItem> NavigationItems { get; } =
    [
        new() { Page = "Account", Title = "账户", Icon = "\uE77B", IconKey = "main_menu_account" },
        new() { Page = "Home", Title = "主页", Icon = "\uE80F", IconKey = "main_menu_launch" },
        new() { Page = "Download", Title = "游戏下载", Icon = "\uE896", IconKey = "main_menu_instance_download" },
        new() { Page = "GameSettings", Title = "游戏设置", Icon = "\uE713", IconKey = "main_menu_instance_setting" },
        new() { Page = "Resources", Title = "资源中心", Icon = "\uE8F1", IconKey = "main_menu_library" },
        new() { Page = "Settings", Title = "设置", Icon = "\uE713", IconKey = "main_menu_setting" }
    ];

    public ObservableCollection<NavigationItem> SecondaryItems { get; } = [];

    [RelayCommand]
    public async Task InitializeAsync()
    {
        UpdateNavigationSelection();
        Settings = await settingsService.LoadAsync();
        IsMenuExpanded = Settings.IsMenuExpanded;
        await AccountPage.InitializeAsync(Settings);
        HomePage.SetSettings(Settings);
        await GameManagement.InitializeAsync(Settings);
        UpdateSecondaryItems();
        UpdateNavigationSelection();
        UpdateAccountNavigationAvatar();
        HomePage.Initialize(Settings, GameManagement.SelectedInstance);
        hasInitialized = true;
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
            GameManagement.SelectLoader(loader);
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
    private void MinimizeWindow()
    {
        windowService.Minimize();
    }

    [RelayCommand]
    private void CloseWindow()
    {
        windowService.Close();
    }

    partial void OnCurrentPageChanged(string value)
    {
        UpdateNavigationSelection();
        _ = SyncCurrentStateAsync();
    }

    public async Task SyncCurrentStateAsync()
    {
        if (!hasInitialized || isSyncingCurrentState)
            return;

        isSyncingCurrentState = true;
        try
        {
            await GameManagement.EnsureInstancesLoadedAsync();
            HomePage.SetSelectedInstance(GameManagement.SelectedInstance);

            if (string.Equals(CurrentPage, "Download", StringComparison.OrdinalIgnoreCase))
                await DownloadPage.EnsureVersionsLoadedAsync();
        }
        finally
        {
            isSyncingCurrentState = false;
        }
    }

    private void GameManagement_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameManagementViewModel.SelectedInstance))
            HomePage.SetSelectedInstance(GameManagement.SelectedInstance);

        if (e.PropertyName == nameof(GameManagementViewModel.ProgressPercent))
            ProgressPercent = GameManagement.ProgressPercent;
    }

    private void DownloadPage_InstanceInstalled(object? sender, GameInstance instance)
    {
        if (GameManagement.Instances.All(existing => existing.Id != instance.Id))
            GameManagement.Instances.Add(instance);

        GameManagement.SelectedInstance = instance;
        HomePage.SetSelectedInstance(instance);
    }

    private void UpdateSecondaryItems()
    {
        SecondaryItems.Clear();

        IEnumerable<NavigationItem> items = CurrentPage switch
        {
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

        DownloadTasksNavigationItem.IsSelected = string.Equals(
            DownloadTasksNavigationItem.Page,
            CurrentPage,
            StringComparison.OrdinalIgnoreCase);
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
