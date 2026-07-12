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

using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.Download;

/// <summary>
/// 协调版本列表、实例安装选项、安装状态和本地整合包导入四个下载页子流程。
/// </summary>
public sealed partial class DownloadPageViewModel : ObservableObject, IDisposable
{
    // 子 ViewModel 各自拥有业务状态，本类只维护页面步骤与跨子流程事件转发。
    private readonly IFloatingMessageService floatingMessageService;
    private readonly ILogger<DownloadPageViewModel> logger;
    private CancellationTokenSource? optionsNavigationCancellation;
    private string lastLocalImportDropHintMessage = string.Empty;

    [ObservableProperty]
    private DownloadPageStep currentStep = DownloadPageStep.VersionList;

    [ObservableProperty]
    private int contentRefreshToken;

    public DownloadPageViewModel(
        IGameVersionService gameVersionService,
        IGameInstanceService instanceService,
        DownloadTasksPageViewModel downloadTasksPage,
        IEnumerable<ILoaderProvider> loaderProviders)
        : this(
            gameVersionService,
            instanceService,
            downloadTasksPage,
            loaderProviders,
            ImmediateUiDispatcher.Instance,
            NullFloatingMessageService.Instance,
            NullInstanceFolderService.Instance,
            NullFilePickerService.Instance,
            NullLocalModpackImportService.Instance,
            RejectingExistingFilePathValidator.Instance)
    {
    }

    public DownloadPageViewModel(
        IGameVersionService gameVersionService,
        IGameInstanceService instanceService,
        DownloadTasksPageViewModel downloadTasksPage,
        IEnumerable<ILoaderProvider> loaderProviders,
        IUiDispatcher uiDispatcher)
        : this(
            gameVersionService,
            instanceService,
            downloadTasksPage,
            loaderProviders,
            uiDispatcher,
            NullFloatingMessageService.Instance,
            NullInstanceFolderService.Instance,
            NullFilePickerService.Instance,
            NullLocalModpackImportService.Instance,
            RejectingExistingFilePathValidator.Instance)
    {
    }

    public DownloadPageViewModel(
        IGameVersionService gameVersionService,
        IGameInstanceService instanceService,
        DownloadTasksPageViewModel downloadTasksPage,
        IEnumerable<ILoaderProvider> loaderProviders,
        IUiDispatcher uiDispatcher,
        IFloatingMessageService floatingMessageService,
        IInstanceFolderService instanceFolderService,
        IFilePickerService filePickerService,
        ILocalModpackImportService localModpackImportService,
        IExistingFilePathValidator existingFilePathValidator,
        IModrinthService? modrinthService = null,
        ILogger<DownloadLocalImportDialogViewModel>? localImportLogger = null,
        ILogger<DownloadInstallViewModel>? installLogger = null,
        ILogger<DownloadPageViewModel>? logger = null)
    {
        this.floatingMessageService = floatingMessageService;
        this.logger = logger ?? NullLogger<DownloadPageViewModel>.Instance;
        var instanceNameTracker = new DownloadInstanceNameTracker();
        VersionList = new DownloadVersionListViewModel(gameVersionService, uiDispatcher);
        InstanceOptions = new DownloadInstanceOptionsViewModel(
            instanceService,
            loaderProviders,
            instanceNameTracker,
            modrinthService,
            this.logger);
        InstallState = new DownloadInstallViewModel(
            instanceService,
            downloadTasksPage,
            instanceNameTracker,
            uiDispatcher,
            floatingMessageService,
            installLogger);
        ModpackManualDownloadsDialog = new DownloadModpackManualDownloadsDialogViewModel(
            instanceFolderService,
            floatingMessageService);
        LocalImportDialog = new DownloadLocalImportDialogViewModel(
            filePickerService,
            localModpackImportService,
            downloadTasksPage,
            uiDispatcher,
            floatingMessageService,
            ModpackManualDownloadsDialog,
            existingFilePathValidator,
            localImportLogger);

        VersionList.VersionSelected += VersionList_VersionSelected;
        VersionList.LocalImportRequested += VersionList_LocalImportRequested;
        VersionList.CategoryContentRefreshRequested += VersionList_CategoryContentRefreshRequested;
        VersionList.PropertyChanged += VersionList_PropertyChanged;
        InstanceOptions.InstallAvailabilityChanged += InstanceOptions_InstallAvailabilityChanged;
        InstallState.InstanceInstalled += InstallState_InstanceInstalled;
        InstallState.NameAvailabilityChanged += InstallState_NameAvailabilityChanged;
        LocalImportDialog.ModpackImported += LocalImportDialog_ModpackImported;
    }

    public event EventHandler<GameInstance>? InstanceInstalled;

    public DownloadVersionListViewModel VersionList { get; }

    public DownloadInstanceOptionsViewModel InstanceOptions { get; }

    public DownloadInstallViewModel InstallState { get; }

    public DownloadModpackManualDownloadsDialogViewModel ModpackManualDownloadsDialog { get; }

    public DownloadLocalImportDialogViewModel LocalImportDialog { get; }

    public bool IsVersionListStep => CurrentStep is DownloadPageStep.VersionList;

    public bool IsInstanceOptionsStep => CurrentStep is DownloadPageStep.InstanceOptions;

    public bool IsDownloadContentVisible => IsInstanceOptionsStep || VersionList.HasVisibleVersions;

    public bool CanInstallSelectedVersion => InstanceOptions.CanInstall;

    public string InstallButtonText => Strings.Download_InstallButton;

    public string PageTitle => IsInstanceOptionsStep
        ? VersionList.SelectedMinecraftVersion?.Name ?? string.Empty
        : VersionList.SelectedVersionCategory?.Title ?? string.Empty;

    public string? PageTitleIconSource => IsInstanceOptionsStep
        ? VersionList.SelectedMinecraftVersion?.IconSource
        : null;

    public void PrimeFromSettings(LauncherSettings settings)
    {
        // 下载源和限速同时影响在线安装与本地整合包依赖下载，必须传播给两个入口。
        ApplyDownloadSourcePreference(settings.DownloadSourcePreference);
        ApplyDownloadSpeedLimit(settings.DownloadSpeedLimitMbPerSecond);
    }

    public void ApplyDownloadSourcePreference(DownloadSourcePreference preference)
    {
        LocalImportDialog.ApplyDownloadSourcePreference(preference);
        VersionList.ApplyDownloadSourcePreference(preference);
        InstanceOptions.ApplyDownloadSourcePreference(preference);
        if (IsInstanceOptionsStep && VersionList.SelectedMinecraftVersion is null)
            BackToVersionList();
    }

    public void ApplyDownloadSpeedLimit(int downloadSpeedLimitMbPerSecond)
    {
        var normalized = Math.Max(downloadSpeedLimitMbPerSecond, 0);
        LocalImportDialog.ApplyDownloadSpeedLimit(normalized);
        VersionList.ApplyDownloadSpeedLimit(normalized);
        InstanceOptions.ApplyDownloadSpeedLimit(normalized);
    }

    public bool CanHandleLocalImportDrop(IReadOnlyList<string> paths)
    {
        return CanHandleLocalImportDropCore(paths);
    }

    public bool UpdateLocalImportDropState(IReadOnlyList<string> paths)
    {
        // DragOver 只做轻量格式判断和提示，不在高频事件中打开压缩包或执行识别。
        var canAccept = CanHandleLocalImportDropCore(paths);
        ApplyLocalImportDropHint(canAccept
            ? Strings.GameSettings_DropReleaseToImportMessage
            : Strings.GameSettings_DropUnsupportedFileMessage);
        return canAccept;
    }

    public void ClearLocalImportDropState()
    {
        ApplyLocalImportDropHint(string.Empty);
    }

    public async Task<bool> HandleLocalImportDropAsync(IReadOnlyList<string> paths)
    {
        // Drop 后才进入实际识别；返回值表示是否接管文件，便于外层清除拖放视觉状态。
        if (!CanHandleLocalImportDropCore(paths))
            return false;
        try
        {
            return await LocalImportDialog.ImportDroppedFilesAsync(paths);
        }
        finally
        {
            ClearLocalImportDropState();
        }
    }

    public Task EnsureVersionsLoadedAsync(CancellationToken cancellationToken = default)
    {
        return VersionList.EnsureVersionsLoadedAsync(cancellationToken);
    }

    public void Dispose()
    {
        CancelOptionsNavigation();
        VersionList.VersionSelected -= VersionList_VersionSelected;
        VersionList.LocalImportRequested -= VersionList_LocalImportRequested;
        VersionList.CategoryContentRefreshRequested -= VersionList_CategoryContentRefreshRequested;
        VersionList.PropertyChanged -= VersionList_PropertyChanged;
        InstanceOptions.InstallAvailabilityChanged -= InstanceOptions_InstallAvailabilityChanged;
        InstallState.InstanceInstalled -= InstallState_InstanceInstalled;
        InstallState.NameAvailabilityChanged -= InstallState_NameAvailabilityChanged;
        LocalImportDialog.ModpackImported -= LocalImportDialog_ModpackImported;
        VersionList.Dispose();
        InstanceOptions.Dispose();
    }

    [RelayCommand]
    private void BackToVersionList()
    {
        ShowVersionList();
    }

    [RelayCommand(CanExecute = nameof(CanInstallSelectedVersion), AllowConcurrentExecutions = true)]
    private async Task InstallAsync()
    {
        // 安装按钮委托给 InstallState，本页只负责防重复提交和步骤切换。
        var request = InstanceOptions.CreateInstallRequest();
        if (request is null)
            return;

        CurrentStep = DownloadPageStep.VersionList;
        ContentRefreshToken++;
        await InstallState.InstallAsync(request);
    }

    partial void OnCurrentStepChanged(DownloadPageStep value)
    {
        OnPropertyChanged(nameof(IsVersionListStep));
        OnPropertyChanged(nameof(IsInstanceOptionsStep));
        OnPropertyChanged(nameof(IsDownloadContentVisible));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageTitleIconSource));
        OnPropertyChanged(nameof(CanInstallSelectedVersion));
        InstallCommand.NotifyCanExecuteChanged();
    }

    private void VersionList_VersionSelected(DownloadMinecraftVersionItem version)
    {
        _ = OpenInstanceOptionsAsync(version);
    }

    private async Task OpenInstanceOptionsAsync(DownloadMinecraftVersionItem version)
    {
        // 版本切换会触发 Loader 查询，先取消上一次导航，防止旧结果晚到覆盖当前页。
        CancelOptionsNavigation();
        var cancellation = new CancellationTokenSource();
        optionsNavigationCancellation = cancellation;
        try
        {
            var preparation = InstanceOptions.PrepareAsync(version, cancellation.Token);
            if (!ReferenceEquals(optionsNavigationCancellation, cancellation)
                || !ReferenceEquals(VersionList.SelectedMinecraftVersion, version))
            {
                await preparation;
                return;
            }

            CurrentStep = DownloadPageStep.InstanceOptions;
            await preparation;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to prepare instance installation options. MinecraftVersion={MinecraftVersion}",
                version.Name);
            if (ReferenceEquals(optionsNavigationCancellation, cancellation))
                CurrentStep = DownloadPageStep.InstanceOptions;
        }
        finally
        {
            Interlocked.CompareExchange(ref optionsNavigationCancellation, null, cancellation);
            cancellation.Dispose();
        }
    }

    private void VersionList_LocalImportRequested()
    {
        LocalImportDialog.Open();
    }

    private void VersionList_CategoryContentRefreshRequested()
    {
        ContentRefreshToken++;
    }

    private void VersionList_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 页面公开属性是子列表状态投影，需要显式转发通知保持 Binding 更新。
        switch (e.PropertyName)
        {
            case nameof(DownloadVersionListViewModel.SelectedVersionCategory):
                if (IsInstanceOptionsStep)
                    ShowVersionList();
                OnPropertyChanged(nameof(PageTitle));
                break;
            case nameof(DownloadVersionListViewModel.SelectedMinecraftVersion):
                OnPropertyChanged(nameof(PageTitle));
                OnPropertyChanged(nameof(PageTitleIconSource));
                break;
            case nameof(DownloadVersionListViewModel.VisibleVersions):
            case nameof(DownloadVersionListViewModel.HasVisibleVersions):
                OnPropertyChanged(nameof(IsDownloadContentVisible));
                break;
        }
    }

    private void InstanceOptions_InstallAvailabilityChanged()
    {
        OnPropertyChanged(nameof(CanInstallSelectedVersion));
        InstallCommand.NotifyCanExecuteChanged();
    }

    private void InstallState_NameAvailabilityChanged()
    {
        InstanceOptions.NotifyNameAvailabilityChanged();
    }

    private void InstallState_InstanceInstalled(object? sender, GameInstance instance)
    {
        InstanceInstalled?.Invoke(this, instance);
    }

    private void LocalImportDialog_ModpackImported(object? sender, GameInstance instance)
    {
        InstanceInstalled?.Invoke(this, instance);
    }

    private bool CanHandleLocalImportDropCore(IReadOnlyList<string> paths)
    {
        if (!LocalImportDialog.CanAcceptDroppedFiles(paths))
            return false;
        var extension = Path.GetExtension(paths[0]);
        return string.Equals(extension, ".mrpack", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyLocalImportDropHint(string message)
    {
        if (string.Equals(lastLocalImportDropHintMessage, message, StringComparison.Ordinal))
            return;
        lastLocalImportDropHintMessage = message;
        floatingMessageService.Show(message);
    }

    private void CancelOptionsNavigation()
    {
        // CTS 只归本页面步骤所有；子 ViewModel 仍负责其内部网络请求生命周期。
        var cancellation = Interlocked.Exchange(ref optionsNavigationCancellation, null);
        cancellation?.Cancel();
    }

    private void ShowVersionList()
    {
        // 返回列表时清除安装选项瞬态状态，但保留版本缓存和滚动位置。
        CancelOptionsNavigation();
        CurrentStep = DownloadPageStep.VersionList;
        InstanceOptions.Deactivate();
        VersionList.ClearSelectedVersion();
    }

    private sealed class NullFilePickerService : IFilePickerService
    {
        public static NullFilePickerService Instance { get; } = new();
        private NullFilePickerService() { }
        public string? PickMinecraftSkin() => null;
        public string? PickJavaExecutable() => null;
        public string? PickLocalImportFile() => null;
        public string? PickModFile() => null;
        public string? PickSaveArchive() => null;
        public string? PickResourcePackArchive() => null;
        public string? PickShaderPackArchive() => null;
        public string? PickModpackExportArchive(string defaultFileName, ModpackExportKind kind) => null;
        public string? PickFolder(string title, string? initialDirectory = null) => null;
    }

    private sealed class NullInstanceFolderService : IInstanceFolderService
    {
        public static NullInstanceFolderService Instance { get; } = new();
        private NullInstanceFolderService() { }
        public bool DirectoryExists(string folderPath) => false;
        public string EnsureDirectoryExists(string folderPath) => folderPath;
        public bool TryOpen(string folderPath) => false;
        public bool TryOpenFile(string filePath) => false;
        public bool TryRevealFile(string filePath) => false;
    }

    private sealed class NullLocalModpackImportService : ILocalModpackImportService
    {
        public static NullLocalModpackImportService Instance { get; } = new();
        private NullLocalModpackImportService() { }
        public Task<ModpackRecognitionResult> RecognizeArchiveAsync(
            string archivePath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ModpackRecognitionResult.Failure(ModpackRecognitionFailureReason.UnsupportedArchive));
        }

        public Task<ModpackImportResult> ImportFromArchiveAsync(
            string archivePath,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default,
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            return Task.FromResult(ModpackImportResult.Failure(ModpackImportFailureReason.UnsupportedArchive));
        }
    }

    private sealed class NullFloatingMessageService : IFloatingMessageService
    {
        public static NullFloatingMessageService Instance { get; } = new();
        public event Action<string>? MessageRequested { add { } remove { } }
        private NullFloatingMessageService() { }
        public void Show(string message) { }
    }

    private sealed class RejectingExistingFilePathValidator : IExistingFilePathValidator
    {
        public static RejectingExistingFilePathValidator Instance { get; } = new();
        public bool TryNormalize(string? path, out string normalizedPath)
        {
            normalizedPath = string.Empty;
            return false;
        }
    }
}
