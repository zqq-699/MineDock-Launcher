using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Models;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Settings;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Shell;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ISettingsService settingsService;
    private readonly IWindowService windowService;
    private readonly IStatusService statusService;
    private bool hasPrimedSettings;
    private bool hasInitialized;
    private bool isSyncingCurrentState;

    [ObservableProperty]
    private LauncherSettings settings = new();

    [ObservableProperty]
    private string currentPage = NavigationCatalog.HomePage;

    [ObservableProperty]
    private bool isMenuExpanded;

    [ObservableProperty]
    private string statusMessage = Strings.Status_Ready;

    [ObservableProperty]
    private double progressPercent;

    public MainViewModel(
        ISettingsService settingsService,
        AccountPageViewModel accountPage,
        DownloadPageViewModel downloadPage,
        DownloadTasksPageViewModel downloadTasksPage,
        GameSettingsPageViewModel gameSettingsPage,
        SettingsPageViewModel settingsPage,
        GameManagementViewModel gameManagement,
        IWindowService windowService,
        IStatusService statusService,
        IHomePageViewModelFactory homePageFactory)
    {
        this.settingsService = settingsService;
        this.windowService = windowService;
        this.statusService = statusService;
        AccountPage = accountPage;
        DownloadPage = downloadPage;
        DownloadTasksPage = downloadTasksPage;
        GameSettingsPage = gameSettingsPage;
        SettingsPage = settingsPage;
        GameManagement = gameManagement;
        HomePage = homePageFactory.Create(
            AccountPage,
            percent => ProgressPercent = percent,
            instance => GameManagement.SelectLaunchInstanceAsync(instance),
            OpenGameSettingsForInstanceAsync);

        statusService.MessageReported += message => StatusMessage = message;
        DownloadPage.InstanceInstalled += DownloadPage_InstanceInstalled;
        AccountPage.PropertyChanged += AccountPage_PropertyChanged;
        GameManagement.PropertyChanged += GameManagement_PropertyChanged;
        GameSettingsPage.LaunchInstanceRequested += GameSettingsPage_LaunchInstanceRequested;
        GameSettingsPage.InstancesChanged += GameSettingsPage_InstancesChanged;

        UpdateNavigationSelection();
    }

    public AccountPageViewModel AccountPage { get; }

    public HomePageViewModel HomePage { get; }

    public DownloadPageViewModel DownloadPage { get; }

    public DownloadTasksPageViewModel DownloadTasksPage { get; }

    public GameSettingsPageViewModel GameSettingsPage { get; }

    public SettingsPageViewModel SettingsPage { get; }

    public GameManagementViewModel GameManagement { get; }

    public NavigationItem DownloadTasksNavigationItem { get; } = NavigationCatalog.CreateDownloadTasksItem();

    public ObservableCollection<NavigationItem> NavigationItems { get; } = new(NavigationCatalog.CreatePrimaryItems());

    public ObservableCollection<NavigationItem> SecondaryItems { get; } = [];

    public async Task PrimeAsync()
    {
        if (hasPrimedSettings)
            return;

        Settings = await settingsService.LoadAsync();
        IsMenuExpanded = Settings.IsMenuExpanded;
        AccountPage.PrimeFromSettings(Settings);
        HomePage.SetSettings(Settings);
        GameSettingsPage.PrimeFromSettings(Settings);
        SettingsPage.PrimeFromSettings(Settings);
        UpdateNavigationSelection();
        UpdateAccountNavigationAvatar();
        hasPrimedSettings = true;
    }

    [RelayCommand]
    public async Task InitializeAsync()
    {
        await PrimeAsync();
        await AccountPage.InitializeAsync(Settings);
        HomePage.SetSettings(Settings);
        await GameManagement.InitializeAsync(Settings);
        await HomePage.EnsureVersionTypesLoadedAsync();
        UpdateSecondaryItems();
        UpdateNavigationSelection();
        UpdateAccountNavigationAvatar();
        HomePage.SetLaunchInstances(GameManagement.Instances);
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
        var targetPage = item.Loader is LoaderKind
            ? NavigationCatalog.DownloadPage
            : item.Page;
        var isRepeatingGameSettingsClick = NavigationCatalog.IsPage(CurrentPage, NavigationCatalog.GameSettingsPage)
            && NavigationCatalog.IsPage(targetPage, NavigationCatalog.GameSettingsPage);
        var isRepeatingHomeClick = NavigationCatalog.IsPage(CurrentPage, NavigationCatalog.HomePage)
            && NavigationCatalog.IsPage(targetPage, NavigationCatalog.HomePage);

        if (item.Loader is LoaderKind loader)
        {
            GameManagement.SelectLoader(loader);
            CurrentPage = targetPage;
        }
        else
        {
            CurrentPage = targetPage;
        }

        UpdateSecondaryItems();
        UpdateNavigationSelection();

        if (isRepeatingGameSettingsClick && hasInitialized)
            _ = GameSettingsPage.RefreshInstancesSilentlyAsync();

        if (isRepeatingHomeClick && hasInitialized)
            _ = RefreshHomeInstancesAsync();
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
            if (NavigationCatalog.IsPage(CurrentPage, NavigationCatalog.HomePage))
            {
                await RefreshHomeInstancesAsync();
            }
            else
            {
                await GameManagement.EnsureInstancesLoadedAsync();
                await HomePage.EnsureVersionTypesLoadedAsync();
                SyncHomeLaunchInstances();
            }

            if (NavigationCatalog.IsPage(CurrentPage, NavigationCatalog.DownloadPage))
                await DownloadPage.EnsureVersionsLoadedAsync();

            if (NavigationCatalog.IsPage(CurrentPage, NavigationCatalog.GameSettingsPage))
                await GameSettingsPage.RefreshInstancesForPageActivationAsync();
        }
        finally
        {
            isSyncingCurrentState = false;
        }
    }

    private async Task RefreshHomeInstancesAsync()
    {
        await GameManagement.RefreshInstancesAsync();
        await HomePage.EnsureVersionTypesLoadedAsync();
        SyncHomeLaunchInstances();
    }

    private void SyncHomeLaunchInstances()
    {
        HomePage.SetLaunchInstances(GameManagement.Instances);
        HomePage.SetSelectedInstance(GameManagement.SelectedInstance);
    }

    private void AccountPage_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountPageViewModel.SelectedAccount))
            UpdateAccountNavigationAvatar();
    }

    private void GameManagement_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameManagementViewModel.SelectedInstance))
            HomePage.SetSelectedInstance(GameManagement.SelectedInstance);

        if (e.PropertyName == nameof(GameManagementViewModel.ProgressPercent))
            ProgressPercent = GameManagement.ProgressPercent;
    }

    private async void DownloadPage_InstanceInstalled(object? sender, GameInstance instance)
    {
        if (GameManagement.Instances.All(existing => existing.Id != instance.Id))
            GameManagement.Instances.Add(instance);

        try
        {
            var saved = await GameManagement.SelectLaunchInstanceAsync(instance);
            if (!saved)
                GameManagement.SelectedInstance = instance;
        }
        catch (Exception)
        {
            GameManagement.SelectedInstance = instance;
        }

        GameSettingsPage.AddOrUpdateInstance(instance);
        HomePage.SetLaunchInstances(GameManagement.Instances);
        HomePage.SetSelectedInstance(instance);
    }

    private void UpdateSecondaryItems()
    {
        SecondaryItems.Clear();
        foreach (var item in NavigationCatalog.CreateSecondaryItems(CurrentPage))
            SecondaryItems.Add(item);
    }

    private void UpdateNavigationSelection()
    {
        foreach (var item in NavigationItems)
            item.IsSelected = NavigationCatalog.IsPage(item.Page, CurrentPage);

        DownloadTasksNavigationItem.IsSelected = NavigationCatalog.IsPage(
            DownloadTasksNavigationItem.Page,
            CurrentPage);
    }

    private void GameSettingsPage_LaunchInstanceRequested(GameInstance instance)
    {
        _ = HandleGameSettingsLaunchRequestAsync(instance);
    }

    private void GameSettingsPage_InstancesChanged()
    {
        _ = SyncInstancesFromGameSettingsAsync();
    }

    private async Task HandleGameSettingsLaunchRequestAsync(GameInstance instance)
    {
        try
        {
            var saved = await GameManagement.SelectLaunchInstanceAsync(instance);
            if (!saved)
            {
                statusService.Report(Strings.Status_LaunchInstanceSelectionFailed);
                return;
            }

            HomePage.SetLaunchInstances(GameManagement.Instances);
            HomePage.SetSelectedInstance(GameManagement.SelectedInstance);
            CurrentPage = NavigationCatalog.HomePage;
            UpdateSecondaryItems();
            UpdateNavigationSelection();
            statusService.Report(string.Format(Strings.Status_LaunchInstanceSelectedFormat, instance.Name));
        }
        catch (Exception)
        {
            statusService.Report(Strings.Status_LaunchInstanceSelectionFailed);
        }
    }

    private async Task SyncInstancesFromGameSettingsAsync()
    {
        try
        {
            await GameManagement.RefreshInstancesAsync();
            HomePage.SetLaunchInstances(GameManagement.Instances);
            HomePage.SetSelectedInstance(GameManagement.SelectedInstance);
        }
        catch (Exception)
        {
            statusService.Report(Strings.Status_LoadInstancesFailed);
        }
    }

    private void UpdateAccountNavigationAvatar()
    {
        var accountItem = NavigationItems.FirstOrDefault(item => item.Page == NavigationCatalog.AccountPage);
        if (accountItem is not null)
            accountItem.AvatarUrl = AccountPage.SelectedAccount?.AvatarUrl;
    }

    private async Task OpenGameSettingsForInstanceAsync(GameInstance? instance)
    {
        await GameSettingsPage.OpenInstanceDetailsAsync(instance);
        CurrentPage = NavigationCatalog.GameSettingsPage;
        UpdateSecondaryItems();
        UpdateNavigationSelection();
    }
}

