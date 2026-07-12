/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Models;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Resources;
using Launcher.App.ViewModels.Settings;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.Shell;

/// <summary>
/// 管理主窗口导航、全局对话框和浮动消息，并通过会话协调器连接各功能页面。
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    // Shell 只拥有跨页面 UI 状态，实例、下载、账户和设置业务仍由各自 ViewModel/Service 负责。
    private static readonly TimeSpan FloatingMessageDuration = TimeSpan.FromSeconds(2.2);

    private readonly ISettingsService settingsService;
    private readonly LauncherSessionCoordinator sessionCoordinator;
    private readonly IWindowService windowService;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger<MainViewModel> logger;
    private bool hasPrimedSettings;
    private bool hasInitialized;
    private bool isCloseConfirmed;
    private CancellationTokenSource? floatingMessageHideCancellation;
    private GameInstance? pendingJavaRequirementInstance;

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

    [ObservableProperty]
    private string floatingMessage = string.Empty;

    [ObservableProperty]
    private bool isFloatingMessageOpen;

    [ObservableProperty]
    private bool isJavaRequirementDialogOpen;

    [ObservableProperty]
    private string javaRequirementDialogTitle = Strings.Dialog_JavaRequirementNotMetTitle;

    [ObservableProperty]
    private string javaRequirementDialogMessage = string.Empty;

    [ObservableProperty]
    private bool isJavaRequirementForceLaunchAvailable;

    [ObservableProperty]
    private bool isDownloadCloseConfirmationDialogOpen;

    public MainViewModel(
        ISettingsService settingsService,
        LauncherSessionCoordinator sessionCoordinator,
        AccountPageViewModel accountPage,
        DownloadPageViewModel downloadPage,
        DownloadTasksPageViewModel downloadTasksPage,
        GameSettingsPageViewModel gameSettingsPage,
        ResourcesPageViewModel resourcesPage,
        SettingsPageViewModel settingsPage,
        GameManagementViewModel gameManagement,
        IWindowService windowService,
        IStatusService statusService,
        IFloatingMessageService floatingMessageService,
        IUiDispatcher uiDispatcher,
        IHomePageViewModelFactory homePageFactory,
        LaunchStatusDialogViewModel launchStatusDialog,
        UserAgreementDialogViewModel userAgreementDialog,
        ILogger<MainViewModel>? logger = null)
    {
        this.settingsService = settingsService;
        this.sessionCoordinator = sessionCoordinator;
        this.windowService = windowService;
        this.uiDispatcher = uiDispatcher;
        this.logger = logger ?? NullLogger<MainViewModel>.Instance;
        AccountPage = accountPage;
        DownloadPage = downloadPage;
        DownloadTasksPage = downloadTasksPage;
        GameSettingsPage = gameSettingsPage;
        ResourcesPage = resourcesPage;
        SettingsPage = settingsPage;
        GameManagement = gameManagement;
        LaunchStatusDialog = launchStatusDialog;
        UserAgreementDialog = userAgreementDialog;
        HomePage = homePageFactory.Create(
            AccountPage,
            percent => ProgressPercent = percent,
            sessionCoordinator.SelectLaunchInstanceAsync,
            sessionCoordinator.SetHomeLaunchMenuPinnedAsync,
            OpenGameSettingsForInstanceAsync);
        sessionCoordinator.Attach(HomePage);
        sessionCoordinator.NavigationRequested += SessionCoordinator_NavigationRequested;
        sessionCoordinator.ProgressChanged += progress => ProgressPercent = progress;
        HomePage.JavaRequirementNotMet += HomePage_JavaRequirementNotMet;
        HomePage.LaunchFailureReported += HomePage_LaunchFailureReported;

        statusService.MessageReported += message => StatusMessage = message;
        floatingMessageService.MessageRequested += ShowFloatingMessage;
        AccountPage.PropertyChanged += AccountPage_PropertyChanged;

        UpdateNavigationSelection();
    }

    public AccountPageViewModel AccountPage { get; }

    public HomePageViewModel HomePage { get; }

    public DownloadPageViewModel DownloadPage { get; }

    public DownloadTasksPageViewModel DownloadTasksPage { get; }

    public GameSettingsPageViewModel GameSettingsPage { get; }

    public ResourcesPageViewModel ResourcesPage { get; }

    public SettingsPageViewModel SettingsPage { get; }

    public GameManagementViewModel GameManagement { get; }

    public LaunchStatusDialogViewModel LaunchStatusDialog { get; }

    public UserAgreementDialogViewModel UserAgreementDialog { get; }

    public NavigationItem DownloadTasksNavigationItem { get; } = NavigationCatalog.CreateDownloadTasksItem();

    public ObservableCollection<NavigationItem> NavigationItems { get; } = new(NavigationCatalog.CreatePrimaryItems());

    public ObservableCollection<NavigationItem> SecondaryItems { get; } = [];

    public async Task PrimeAsync(LauncherSettings? initialSettings = null)
    {
        // Prime 使用已加载设置建立首屏，完整异步初始化随后执行，缩短窗口首次可见时间。
        if (hasPrimedSettings)
            return;

        Settings = initialSettings ?? await settingsService.LoadAsync();
        UserAgreementDialog.Prime(Settings);
        IsMenuExpanded = Settings.IsMenuExpanded;
        AccountPage.PrimeFromSettings(Settings);
        await sessionCoordinator.PrimeAsync(Settings);
        UpdateNavigationSelection();
        UpdateAccountNavigationAvatar();
        hasPrimedSettings = true;
    }

    public Task<bool> WaitForUserAgreementDecisionAsync() => UserAgreementDialog.WaitForDecisionAsync();

    [RelayCommand]
    public async Task InitializeAsync()
    {
        // SessionCoordinator 统一初始化共享状态，Shell 不自行重复加载实例或版本目录。
        await PrimeAsync();
        await AccountPage.InitializeAsync(Settings);
        await sessionCoordinator.InitializeAsync();
        UpdateSecondaryItems();
        UpdateNavigationSelection();
        UpdateAccountNavigationAvatar();
        hasInitialized = true;
    }

    [RelayCommand]
    private void Navigate(NavigationItem item)
    {
        SelectNavigationItem(item);
    }

    public void SelectNavigationItem(NavigationItem item)
    {
        // 主、次导航项共享一个选中事实，切换后同步两组稳定对象的选中标记。
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
            ObserveShellTask(GameSettingsPage.RefreshInstancesSilentlyAsync(), "refresh game settings instances");

        if (isRepeatingHomeClick && hasInitialized)
            ObserveShellTask(
                sessionCoordinator.SyncCurrentStateAsync(NavigationCatalog.HomePage),
                "refresh home instances");
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
        if (CanCloseWindow())
            windowService.Close();
    }

    public bool CanCloseWindow()
    {
        // 活动下载存在时显示确认对话框并取消本次关闭，避免窗口退出中断写入。
        if (isCloseConfirmed || !DownloadTasksPage.HasRunningTasks)
            return true;

        logger.LogInformation(
            "Launcher close requested while downloads are running. RunningTaskCount={RunningTaskCount}",
            DownloadTasksPage.RunningTaskCount);
        IsDownloadCloseConfirmationDialogOpen = true;
        return false;
    }

    [RelayCommand]
    private void CancelDownloadCloseConfirmation()
    {
        logger.LogInformation("Launcher close canceled because downloads are running.");
        IsDownloadCloseConfirmationDialogOpen = false;
    }

    [RelayCommand]
    private void ConfirmDownloadClose()
    {
        logger.LogInformation(
            "Launcher close confirmed while downloads are running. RunningTaskCount={RunningTaskCount}",
            DownloadTasksPage.RunningTaskCount);
        isCloseConfirmed = true;
        IsDownloadCloseConfirmationDialogOpen = false;
        DownloadTasksPage.CancelAllRunningTasks();
        windowService.Close();
    }

    [RelayCommand]
    private void CloseJavaRequirementDialog()
    {
        IsJavaRequirementDialogOpen = false;
        IsJavaRequirementForceLaunchAvailable = false;
        pendingJavaRequirementInstance = null;
    }

    [RelayCommand]
    private Task OpenJavaSettingsFromRequirementDialogAsync()
    {
        // 先关闭要求弹窗再导航，避免模态遮罩留在 Java 设置页上方。
        var targetInstance = pendingJavaRequirementInstance;
        IsJavaRequirementDialogOpen = false;
        IsJavaRequirementForceLaunchAvailable = false;
        pendingJavaRequirementInstance = null;

        if (targetInstance?.JavaSettingsMode is LaunchSettingsMode.PerInstance)
        {
            GameSettingsPage.ShowInstanceDetails(targetInstance, "java");
            CurrentPage = NavigationCatalog.GameSettingsPage;
        }
        else
        {
            SettingsPage.ShowJavaSection();
            CurrentPage = NavigationCatalog.SettingsPage;
        }

        UpdateSecondaryItems();
        UpdateNavigationSelection();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ForceLaunchFromJavaRequirementDialogAsync()
    {
        // 强制启动只绕过 Java 要求，仍由首页执行完整账户、实例和并发校验。
        var targetInstance = pendingJavaRequirementInstance;
        IsJavaRequirementDialogOpen = false;
        IsJavaRequirementForceLaunchAvailable = false;
        pendingJavaRequirementInstance = null;

        if (targetInstance is null)
            return;

        await HomePage.ForceLaunchIgnoringJavaRequirementAsync(targetInstance);
    }

    partial void OnCurrentPageChanged(string value)
    {
        UpdateNavigationSelection();
        ObserveShellTask(SyncCurrentStateAsync(), "synchronize current page state");
    }

    public Task SyncCurrentStateAsync()
    {
        return hasInitialized
            ? sessionCoordinator.SyncCurrentStateAsync(CurrentPage)
            : Task.CompletedTask;
    }

    private void AccountPage_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountPageViewModel.SelectedAccount))
            UpdateAccountNavigationAvatar();
    }

    private void UpdateSecondaryItems()
    {
        SecondaryItems.Clear();
        foreach (var item in NavigationCatalog.CreateSecondaryItems(CurrentPage))
            SecondaryItems.Add(item);
    }

    private void UpdateNavigationSelection()
    {
        // 原地更新稳定导航对象可保留控件动画、焦点和外部引用。
        foreach (var item in NavigationItems)
            item.IsSelected = NavigationCatalog.IsPage(item.Page, CurrentPage);

        DownloadTasksNavigationItem.IsSelected = NavigationCatalog.IsPage(
            DownloadTasksNavigationItem.Page,
            CurrentPage);
    }

    private void SessionCoordinator_NavigationRequested(string page)
    {
        CurrentPage = page;
        UpdateSecondaryItems();
        UpdateNavigationSelection();
    }

    private void HomePage_JavaRequirementNotMet(object? sender, JavaRequirementNotMetEventArgs e)
    {
        pendingJavaRequirementInstance = e.Instance;
        IsJavaRequirementForceLaunchAvailable = e.Reason is JavaRuntimeSelectionFailureReason.ManualRuntimeVersionTooLow;

        if (e.Reason is JavaRuntimeSelectionFailureReason.ManualRuntimeVersionTooLow)
        {
            JavaRequirementDialogTitle = Strings.Dialog_JavaManualVersionTooLowTitle;
            JavaRequirementDialogMessage = string.Format(
                Strings.Dialog_JavaManualVersionTooLowMessageFormat,
                string.IsNullOrWhiteSpace(e.Instance.Name) ? e.Instance.VersionName : e.Instance.Name,
                e.RequiredMajorVersion?.ToString() ?? Strings.Dialog_LaunchStatusUnknownExitCode,
                e.CurrentMajorVersion?.ToString() ?? Strings.Dialog_LaunchStatusUnknownExitCode);
        }
        else if (e.Reason is JavaRuntimeSelectionFailureReason.AutomaticRuntimeMissing)
        {
            JavaRequirementDialogTitle = Strings.Dialog_JavaRuntimeMissingTitle;
            JavaRequirementDialogMessage = e.RequiredMajorVersion is int missingRequiredMajorVersion
                ? string.Format(Strings.Dialog_JavaRuntimeMissingMessageFormat, missingRequiredMajorVersion)
                : Strings.Dialog_JavaRuntimeMissingMessage;
        }
        else
        {
            JavaRequirementDialogTitle = Strings.Dialog_JavaRequirementNotMetTitle;
            JavaRequirementDialogMessage = e.RequiredMajorVersion is int requiredMajorVersion
                ? string.Format(Strings.Dialog_JavaRequirementNotMetMessageFormat, requiredMajorVersion)
                : Strings.Dialog_JavaRequirementNotMetMessage;
        }

        IsJavaRequirementDialogOpen = true;
    }

    private void HomePage_LaunchFailureReported(object? sender, LaunchFailureReport report)
    {
        // Shell 只承载诊断弹窗，错误分类和脱敏内容已由启动服务准备。
        if (!uiDispatcher.HasAccess)
        {
            uiDispatcher.Post(() => HomePage_LaunchFailureReported(sender, report));
            return;
        }

        windowService.RestoreAndActivate();
        LaunchStatusDialog.Show(report);
    }

    private void UpdateAccountNavigationAvatar()
    {
        var accountItem = NavigationItems.FirstOrDefault(item => item.Page == NavigationCatalog.AccountPage);
        if (accountItem is not null)
            accountItem.AvatarUrl = AccountPage.SelectedAccount?.AvatarUrl;
    }

    private Task OpenGameSettingsForInstanceAsync(GameInstance? instance)
    {
        GameSettingsPage.ShowInstanceDetails(instance);
        CurrentPage = NavigationCatalog.GameSettingsPage;
        UpdateSecondaryItems();
        UpdateNavigationSelection();
        return Task.CompletedTask;
    }

    private void ShowFloatingMessage(string message)
    {
        // 新消息取消上一轮隐藏计时器，让快速连续提示从最后一次显示重新计时。
        if (!uiDispatcher.HasAccess)
        {
            uiDispatcher.Post(() => ShowFloatingMessage(message));
            return;
        }

        floatingMessageHideCancellation?.Cancel();
        floatingMessageHideCancellation?.Dispose();
        floatingMessageHideCancellation = null;

        if (string.IsNullOrWhiteSpace(message))
        {
            IsFloatingMessageOpen = false;
            FloatingMessage = string.Empty;
            return;
        }

        floatingMessageHideCancellation = new CancellationTokenSource();

        FloatingMessage = message;
        IsFloatingMessageOpen = true;
        ObserveShellTask(
            HideFloatingMessageAfterDelayAsync(floatingMessageHideCancellation.Token),
            "hide the floating message");
    }

    private async Task HideFloatingMessageAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(FloatingMessageDuration, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        uiDispatcher.Post(() =>
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            IsFloatingMessageOpen = false;
        });
    }

    private void ObserveShellTask(Task task, string operation)
    {
        // WPF 事件不能直接等待 Task，统一观察以避免导航事件产生未观察异常。
        _ = ObserveShellTaskAsync(task, operation);
    }

    private async Task ObserveShellTaskAsync(Task task, string operation)
    {
        try
        {
            await task;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to {Operation}.", operation);
        }
    }
}
