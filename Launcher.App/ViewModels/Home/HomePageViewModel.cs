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
using Launcher.Application.Accounts;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.Home;

/// <summary>
/// 管理首页启动入口、启动进度和启动失败交互；实例列表与固定菜单状态委托给子 ViewModel。
/// </summary>
public sealed partial class HomePageViewModel : ObservableObject
{
    // LaunchService 负责启动业务，本类只把领域进度和异常映射为 UI 状态、弹窗与窗口行为。
    private readonly ILaunchService launchService;
    private readonly AccountPageViewModel accountPage;
    private readonly IStatusService statusService;
    private readonly IFloatingMessageService floatingMessageService;
    private readonly IWindowService windowService;
    private readonly IUiDispatcher uiDispatcher;
    private readonly Action<double> reportProgressPercent;
    private readonly Func<GameInstance?, Task> openGameSettingsForInstance;
    private readonly ILogger<HomePageViewModel> logger;
    private LauncherSettings settings = new();
    // 一个首页只允许一个活动启动会话；CTS 的存在也作为取消按钮所需的会话身份。
    private CancellationTokenSource? launchCancellationTokenSource;

    [ObservableProperty]
    private bool isLaunching;

    [ObservableProperty]
    private string launchStatusMessage = string.Empty;

    [ObservableProperty]
    private double launchProgressPercent;

    [ObservableProperty]
    private string launchDownloadSpeedText = string.Empty;

    public HomePageViewModel(
        ILaunchService launchService,
        IGameVersionService gameVersionService,
        AccountPageViewModel accountPage,
        IStatusService statusService,
        IFloatingMessageService floatingMessageService,
        IWindowService windowService,
        IUiDispatcher uiDispatcher,
        Action<double> reportProgressPercent,
        Func<GameInstance, Task<bool>> selectLaunchInstance,
        Func<bool, Task<bool>> setLaunchMenuPinned,
        Func<GameInstance?, Task> openGameSettingsForInstance,
        ILogger<HomePageViewModel>? logger = null)
    {
        this.launchService = launchService;
        this.accountPage = accountPage;
        this.statusService = statusService;
        this.floatingMessageService = floatingMessageService;
        this.windowService = windowService;
        this.uiDispatcher = uiDispatcher;
        this.reportProgressPercent = reportProgressPercent;
        this.openGameSettingsForInstance = openGameSettingsForInstance;
        this.logger = logger ?? NullLogger<HomePageViewModel>.Instance;

        LaunchGames = new HomeLaunchGameListViewModel(
            gameVersionService,
            statusService,
            selectLaunchInstance,
            setLaunchMenuPinned);
        LaunchGames.PropertyChanged += LaunchGames_PropertyChanged;

        accountPage.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AccountPageViewModel.SelectedAccount))
                NotifyAccountStateChanged();
        };
    }

    public HomeLaunchGameListViewModel LaunchGames { get; }

    public bool HasSelectedAccount => accountPage.SelectedAccount is not null;

    public GameInstance? SelectedInstance => LaunchGames.SelectedInstance;

    public bool CanLaunchSelectedGame => HasSelectedAccount && SelectedInstance is not null && !IsLaunching;

    public bool HasLaunchProgress => IsLaunching && !string.IsNullOrWhiteSpace(LaunchStatusMessage);

    public bool HasLaunchDownloadSpeedText => IsLaunching && !string.IsNullOrWhiteSpace(LaunchDownloadSpeedText);

    public ObservableCollection<HomeLaunchInstanceItem> LaunchInstances => LaunchGames.LaunchInstances;

    public bool HasLaunchInstances => LaunchGames.HasLaunchInstances;

    public bool HasNoLaunchInstances => LaunchGames.HasNoLaunchInstances;

    public HomeLaunchInstanceItem? SelectedLaunchInstanceItem => LaunchGames.SelectedLaunchInstanceItem;

    public bool HasSelectedLaunchInstance => LaunchGames.HasSelectedLaunchInstance;

    public bool IsLaunchMenuPinned => LaunchGames.IsLaunchMenuPinned;

    public IAsyncRelayCommand SelectLaunchInstanceCommand => LaunchGames.SelectLaunchInstanceCommand;

    public IAsyncRelayCommand ToggleLaunchMenuPinnedCommand => LaunchGames.ToggleLaunchMenuPinnedCommand;

    public bool CanOpenSelectedInstanceSettings => SelectedInstance is not null && !IsLaunching;

    public event EventHandler<JavaRequirementNotMetEventArgs>? JavaRequirementNotMet;

    public event EventHandler<LaunchFailureReport>? LaunchFailureReported;

    public string? HomeAvatarUrl
    {
        get
        {
            var account = accountPage.SelectedAccount;
            if (account is null)
                return null;

            if (!string.IsNullOrWhiteSpace(account.AvatarSource))
                return account.AvatarSource;

            if (account.IsOffline || string.IsNullOrWhiteSpace(account.Uuid))
                return "https://minotar.net/avatar/Steve/576.png";

            return $"https://crafatar.com/avatars/{account.Uuid}?size=576&overlay";
        }
    }

    public string HomeAccountDisplayName => accountPage.SelectedAccount?.DisplayName ?? Strings.Home_NoAccountSelected;

    public string HomeVersionDisplayName
    {
        get
        {
            if (SelectedInstance is null)
                return Strings.Home_NoVersionSelected;

            if (!string.IsNullOrWhiteSpace(SelectedInstance.Name))
                return SelectedInstance.Name;

            if (!string.IsNullOrWhiteSpace(SelectedInstance.VersionName))
                return SelectedInstance.VersionName;

            return string.IsNullOrWhiteSpace(SelectedInstance.MinecraftVersion)
                ? Strings.Home_NoVersionSelected
                : SelectedInstance.MinecraftVersion;
        }
    }

    public void Initialize(LauncherSettings launcherSettings, GameInstance? instance)
    {
        // 初始化顺序先设置列表策略再选实例，使子 ViewModel 能按最新固定菜单偏好构造可见项。
        settings = launcherSettings;
        LaunchGames.SetLaunchMenuPinned(launcherSettings.IsHomeLaunchMenuPinned);
        SetSelectedInstance(instance);
        NotifyAccountStateChanged();
        NotifyInstanceStateChanged();
    }

    public void SetSettings(LauncherSettings launcherSettings)
    {
        settings = launcherSettings;
        LaunchGames.SetLaunchMenuPinned(launcherSettings.IsHomeLaunchMenuPinned);
    }

    public void SetSelectedInstance(GameInstance? instance)
    {
        LaunchGames.SetSelectedInstance(instance);
    }

    public void SetLaunchInstances(IEnumerable<GameInstance> instances)
    {
        LaunchGames.SetLaunchInstances(instances);
    }

    public Task EnsureVersionTypesLoadedAsync(CancellationToken cancellationToken = default)
    {
        return LaunchGames.EnsureVersionTypesLoadedAsync(cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanLaunchSelectedGame))]
    private async Task LaunchAsync()
    {
        await LaunchCoreAsync(options: null, forcedInstance: null);
    }

    public Task ForceLaunchIgnoringJavaRequirementAsync(GameInstance instance)
    {
        return LaunchCoreAsync(
            new LaunchRequestOptions(IgnoreJavaVersionRequirement: true),
            instance);
    }

    private async Task LaunchCoreAsync(LaunchRequestOptions? options, GameInstance? forcedInstance)
    {
        // 强制启动只绕过 Java 版本要求，不绕过账户、实例或并发启动这些基本前置条件。
        var account = accountPage.SelectedAccount;
        var launchInstance = forcedInstance ?? SelectedInstance;
        if (IsLaunching || launchInstance is null || account is null)
        {
            statusService.Report(Strings.Status_NoLaunchableInstance);
            return;
        }

        try
        {
            // BeginLaunchProgress 在调用服务前建立取消与进度状态，确保准备阶段也能响应取消。
            var cancellationTokenSource = BeginLaunchProgress();
            var session = await launchService.LaunchAsync(
                launchInstance,
                account,
                settings,
                CreateProgress(),
                options,
                cancellationTokenSource.Token);
            // LaunchAsync 只保证进程已成功创建；运行期异常退出由独立观察任务继续报告。
            ObserveGameExit(session);

            if (ShouldMinimizeLauncherAfterLaunch(launchInstance))
                windowService.Minimize();
        }
        catch (OperationCanceledException exception) when (launchCancellationTokenSource?.IsCancellationRequested == true)
        {
            logger.LogDebug(
                exception,
                "Launch cancellation completed. InstanceId={InstanceId} InstanceName={InstanceName}",
                launchInstance.Id,
                launchInstance.Name);
            statusService.Report(Strings.Status_LaunchCanceled);
            floatingMessageService.Show(Strings.Status_LaunchCanceled);
        }
        catch (LaunchAccountSessionException)
        {
            statusService.Report(Strings.Status_LaunchAccountUnavailable);
        }
        catch (LaunchFailedException exception)
        {
            // Java 自动发现失败需要用户决策，其余启动失败使用统一诊断报告，避免展示底层异常文本。
            if (exception.InnerException is JavaRuntimeSelectionException javaException
                && ShouldShowJavaRequirementDialog(javaException.Reason))
            {
                JavaRequirementNotMet?.Invoke(this, new JavaRequirementNotMetEventArgs(
                    javaException.RequiredMajorVersion,
                    javaException.Reason,
                    launchInstance,
                    javaException.CurrentMajorVersion));
                statusService.Report(Strings.Status_JavaSelectionFailed);
                return;
            }

            ReportLaunchFailure(exception.Report);
        }
        catch (LaunchProcessExitedException exception)
        {
            ReportLaunchFailure(exception.Report);
        }
        catch (InstanceRepairException)
        {
            statusService.Report(Strings.Status_LaunchInstanceRepairFailed);
        }
        catch (JavaRuntimeSelectionException exception)
        {
            if (ShouldShowJavaRequirementDialog(exception.Reason))
                JavaRequirementNotMet?.Invoke(this, new JavaRequirementNotMetEventArgs(
                    exception.RequiredMajorVersion,
                    exception.Reason,
                    launchInstance,
                    exception.CurrentMajorVersion));

            statusService.Report(Strings.Status_JavaSelectionFailed);
        }
        catch (Exception)
        {
            statusService.Report(Strings.Status_LaunchFailed);
        }
        finally
        {
            // 此处只结束“启动中”进度；Minecraft 进程生命周期由 GameLaunchSession 独立持有。
            ResetLaunchProgress();
        }
    }

    [RelayCommand(CanExecute = nameof(IsLaunching))]
    private void CancelLaunch()
    {
        var cancellationTokenSource = launchCancellationTokenSource;
        if (cancellationTokenSource is null || cancellationTokenSource.IsCancellationRequested)
            return;

        var instance = SelectedInstance;
        logger.LogInformation(
            "Launch cancellation requested. InstanceId={InstanceId} InstanceName={InstanceName} VersionName={VersionName}",
            instance?.Id,
            instance?.Name,
            instance?.VersionName);
        // 仅发出协作式取消，清理由 LaunchService 的阶段边界和 finally 负责。
        cancellationTokenSource.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanOpenSelectedInstanceSettings))]
    private Task OpenSelectedInstanceSettingsAsync()
    {
        return openGameSettingsForInstance(SelectedInstance);
    }

    private IProgress<LauncherProgress> CreateProgress()
    {
        return new Progress<LauncherProgress>(progress =>
        {
            // Progress<T> 回到创建它的 UI 上下文；结束后的迟到进度必须丢弃，不能重新点亮进度条。
            if (!IsLaunching)
                return;

            var message = FormatLaunchProgress(progress);
            LaunchStatusMessage = message;
            if (progress.DownloadSpeedText is not null)
                LaunchDownloadSpeedText = progress.DownloadSpeedText;

            if (progress.Percent is double percent)
                LaunchProgressPercent = Math.Clamp(percent, 0, 100);

            statusService.Report(message);
            reportProgressPercent(LaunchProgressPercent);
        });
    }

    partial void OnIsLaunchingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanLaunchSelectedGame));
        OnPropertyChanged(nameof(HasLaunchProgress));
        OnPropertyChanged(nameof(HasLaunchDownloadSpeedText));
        OnPropertyChanged(nameof(CanOpenSelectedInstanceSettings));
        LaunchCommand.NotifyCanExecuteChanged();
        CancelLaunchCommand.NotifyCanExecuteChanged();
        OpenSelectedInstanceSettingsCommand.NotifyCanExecuteChanged();
    }

    partial void OnLaunchStatusMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasLaunchProgress));
    }

    partial void OnLaunchDownloadSpeedTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasLaunchDownloadSpeedText));
    }

    private void LaunchGames_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 对外属性多为子 ViewModel 投影，需要显式转发通知，否则首页 Binding 不会感知子对象变化。
        switch (e.PropertyName)
        {
            case nameof(HomeLaunchGameListViewModel.SelectedInstance):
                OnPropertyChanged(nameof(SelectedInstance));
                NotifyInstanceStateChanged();
                break;
            case nameof(HomeLaunchGameListViewModel.HasLaunchInstances):
                OnPropertyChanged(nameof(HasLaunchInstances));
                break;
            case nameof(HomeLaunchGameListViewModel.HasNoLaunchInstances):
                OnPropertyChanged(nameof(HasNoLaunchInstances));
                break;
            case nameof(HomeLaunchGameListViewModel.SelectedLaunchInstanceItem):
                OnPropertyChanged(nameof(SelectedLaunchInstanceItem));
                break;
            case nameof(HomeLaunchGameListViewModel.HasSelectedLaunchInstance):
                OnPropertyChanged(nameof(HasSelectedLaunchInstance));
                break;
            case nameof(HomeLaunchGameListViewModel.IsLaunchMenuPinned):
                OnPropertyChanged(nameof(IsLaunchMenuPinned));
                break;
        }
    }

    private void NotifyAccountStateChanged()
    {
        OnPropertyChanged(nameof(HasSelectedAccount));
        OnPropertyChanged(nameof(HomeAvatarUrl));
        OnPropertyChanged(nameof(HomeAccountDisplayName));
        OnPropertyChanged(nameof(CanLaunchSelectedGame));
        LaunchCommand.NotifyCanExecuteChanged();
    }

    private void NotifyInstanceStateChanged()
    {
        OnPropertyChanged(nameof(HomeVersionDisplayName));
        OnPropertyChanged(nameof(CanLaunchSelectedGame));
        OnPropertyChanged(nameof(CanOpenSelectedInstanceSettings));
        LaunchCommand.NotifyCanExecuteChanged();
        OpenSelectedInstanceSettingsCommand.NotifyCanExecuteChanged();
    }

    private CancellationTokenSource BeginLaunchProgress()
    {
        // 上一次会话理论上已结束，仍先释放旧 CTS，避免异常路径遗留句柄。
        launchCancellationTokenSource?.Dispose();
        launchCancellationTokenSource = new CancellationTokenSource();
        IsLaunching = true;
        LaunchProgressPercent = 0;
        LaunchDownloadSpeedText = string.Empty;
        LaunchStatusMessage = Strings.Status_LaunchPreparing;
        statusService.Report(LaunchStatusMessage);
        reportProgressPercent(LaunchProgressPercent);
        return launchCancellationTokenSource;
    }

    private void ResetLaunchProgress()
    {
        launchCancellationTokenSource?.Dispose();
        launchCancellationTokenSource = null;
        LaunchProgressPercent = 0;
        reportProgressPercent(0);
        LaunchDownloadSpeedText = string.Empty;
        LaunchStatusMessage = string.Empty;
        IsLaunching = false;
    }

    private bool ShouldMinimizeLauncherAfterLaunch(GameInstance instance)
    {
        return instance.LaunchSettingsMode is LaunchSettingsMode.UseGlobal
            ? settings.DefaultMinimizeLauncherAfterLaunch
            : instance.MinimizeLauncherAfterLaunch;
    }

    private static string FormatLaunchProgress(LauncherProgress progress)
    {
        // 已知阶段使用本地化稳定文案；仅对扩展阶段回退到服务提供的消息。
        return progress.Stage switch
        {
            LaunchProgressStages.CheckingInstance => Strings.Status_LaunchCheckingInstance,
            LaunchProgressStages.RepairingMetadata => Strings.Status_LaunchRepairingMetadata,
            LaunchProgressStages.RepairingJar => Strings.Status_LaunchRepairingJar,
            LaunchProgressStages.RepairingLibraries => Strings.Status_LaunchRepairingLibraries,
            LaunchProgressStages.RepairingAssets => Strings.Status_LaunchRepairingAssets,
            LaunchProgressStages.RepairingLogging => Strings.Status_LaunchRepairingLogging,
            LaunchProgressStages.CheckingJava => Strings.Status_LaunchCheckingJava,
            LaunchProgressStages.RunningPreLaunchCommand => Strings.Status_LaunchRunningPreLaunchCommand,
            LaunchProgressStages.PreparingProcess => Strings.Status_LaunchPreparingProcess,
            LaunchProgressStages.StartingProcess => Strings.Status_LaunchStartingProcess,
            LaunchProgressStages.CheckingFiles => Strings.Status_LaunchCheckingFiles,
            LaunchProgressStages.DownloadingFiles or LaunchProgressStages.DownloadSpeed => Strings.Status_LaunchDownloadingFiles,
            _ when !string.IsNullOrWhiteSpace(progress.Message) => progress.Message,
            _ => Strings.Status_LaunchPreparing
        };
    }

    private void ObserveGameExit(GameLaunchSession session)
    {
        // 不阻塞 UI 等待游戏退出。TryMarkExitHandled 保证启动期和运行期诊断竞态下只弹一次失败。
        _ = Task.Run(async () =>
        {
            try
            {
                var exitResult = await session.ExitTask;
                if (!exitResult.IsFailure
                    || exitResult.FailureReport is null
                    || !session.TryMarkExitHandled())
                {
                    return;
                }

                ReportLaunchFailure(exitResult.FailureReport);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Failed to observe Minecraft process exit. InstanceId={InstanceId}",
                    session.InstanceId);
            }
        });
    }

    private void ReportLaunchFailure(LaunchFailureReport report)
    {
        // 退出观察发生在线程池，所有状态服务与事件回调都必须切回 UI 调度器。
        if (!uiDispatcher.HasAccess)
        {
            uiDispatcher.Post(() => ReportLaunchFailure(report));
            return;
        }

        statusService.Report(GetLaunchFailureStatus(report.Kind));
        LaunchFailureReported?.Invoke(this, report);
    }

    private static string GetLaunchFailureStatus(LaunchFailureKind kind)
    {
        return kind switch
        {
            LaunchFailureKind.StartupProcessExited => Strings.Status_LaunchProcessExited,
            LaunchFailureKind.RuntimeAbnormalExit => Strings.Status_LaunchRuntimeAbnormalExit,
            LaunchFailureKind.StartupAbnormalExit => Strings.Status_LaunchAbnormalExit,
            _ => Strings.Status_LaunchFailed
        };
    }

    private static bool IsAutomaticJavaRuntimeDiscoveryFailure(JavaRuntimeSelectionFailureReason reason)
    {
        return reason is JavaRuntimeSelectionFailureReason.AutomaticRuntimeMissing
            or JavaRuntimeSelectionFailureReason.AutomaticRuntimeNotFound;
    }

    private static bool ShouldShowJavaRequirementDialog(JavaRuntimeSelectionFailureReason reason)
    {
        return IsAutomaticJavaRuntimeDiscoveryFailure(reason)
            || reason is JavaRuntimeSelectionFailureReason.ManualRuntimeVersionTooLow;
    }
}

public sealed class JavaRequirementNotMetEventArgs : EventArgs
{
    public JavaRequirementNotMetEventArgs(
        int? requiredMajorVersion,
        JavaRuntimeSelectionFailureReason reason,
        GameInstance instance,
        int? currentMajorVersion = null)
    {
        RequiredMajorVersion = requiredMajorVersion;
        Reason = reason;
        Instance = instance;
        CurrentMajorVersion = currentMajorVersion;
    }

    public int? RequiredMajorVersion { get; }

    public JavaRuntimeSelectionFailureReason Reason { get; }

    public GameInstance Instance { get; }

    public int? CurrentMajorVersion { get; }
}

