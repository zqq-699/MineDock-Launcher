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

public sealed partial class DownloadPageViewModel : ObservableObject, IDisposable
{
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
            modrinthService);
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
        CancelOptionsNavigation();
        CurrentStep = DownloadPageStep.VersionList;
        InstanceOptions.Deactivate();
        VersionList.ClearSelectedVersion();
    }

    [RelayCommand(CanExecute = nameof(CanInstallSelectedVersion), AllowConcurrentExecutions = true)]
    private async Task InstallAsync()
    {
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
        CancelOptionsNavigation();
        var cancellation = new CancellationTokenSource();
        optionsNavigationCancellation = cancellation;
        try
        {
            await InstanceOptions.PrepareAsync(version, cancellation.Token);
            if (!ReferenceEquals(optionsNavigationCancellation, cancellation)
                || !ReferenceEquals(VersionList.SelectedMinecraftVersion, version))
            {
                return;
            }
            CurrentStep = DownloadPageStep.InstanceOptions;
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
        switch (e.PropertyName)
        {
            case nameof(DownloadVersionListViewModel.SelectedVersionCategory):
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
        var cancellation = Interlocked.Exchange(ref optionsNavigationCancellation, null);
        cancellation?.Cancel();
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
