/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Reflection;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Download;
using Launcher.App.ViewModels.GameSettings;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Tests.Fakes;

namespace Launcher.Tests.ViewModels.GameSettings;

public sealed class GameSettingsDetailsWatcherLifecycleTests : TestTempDirectory
{
    [Theory]
    [InlineData("mod_management", InstanceDirectoryKind.Mods)]
    [InlineData("saves", InstanceDirectoryKind.Saves)]
    [InlineData("resource_packs", InstanceDirectoryKind.ResourcePacks)]
    [InlineData("shaders", InstanceDirectoryKind.ShaderPacks)]
    public async Task ResourceSectionOwnsExactlyItsWatcher(string sectionId, InstanceDirectoryKind expectedKind)
    {
        var monitor = new RecordingDirectoryMonitor();
        using var details = CreateDetails(monitor);
        details.SetSelectedInstance(CreateInstanceItem());
        details.SetPageActive(true);

        details.SetSelectedSection(CreateSection(sectionId));
        await details.CurrentSectionViewModel!.OnSectionActivatedAsync();

        Assert.Equal([expectedKind], monitor.ActiveKinds);
        Assert.False(monitor.StartedBeforePreviousWatchWasDisposed);

        details.SetSelectedSection(CreateSection("general"));
        Assert.Empty(monitor.ActiveKinds);
        Assert.True(GetHasLoaded(details, expectedKind));
        var entranceAnimationToken = GetEntranceAnimationToken(details, expectedKind);

        details.SetSelectedSection(CreateSection(sectionId));
        var silentRefresh = details.CurrentSectionViewModel!.OnSectionActivatedAsync();
        Assert.True(GetHasLoaded(details, expectedKind));
        Assert.False(GetIsLoading(details, expectedKind));
        Assert.Equal(entranceAnimationToken, GetEntranceAnimationToken(details, expectedKind));
        await silentRefresh;
        Assert.Equal(entranceAnimationToken, GetEntranceAnimationToken(details, expectedKind));
        details.SetPageActive(false);
    }

    [Theory]
    [InlineData("general")]
    [InlineData("launch")]
    [InlineData("java")]
    [InlineData("backup")]
    [InlineData("export")]
    public void NonResourceSectionKeepsAllContentWatchersStopped(string sectionId)
    {
        var monitor = new RecordingDirectoryMonitor();
        using var details = CreateDetails(monitor);
        details.SetSelectedInstance(CreateInstanceItem());
        details.SetSelectedSection(CreateSection(sectionId));
        details.SetPageActive(true);

        Assert.Empty(monitor.ActiveKinds);
        details.SetPageActive(false);
    }

    [Fact]
    public void HidingDetailsPageStopsCurrentResourceWatcher()
    {
        var monitor = new RecordingDirectoryMonitor();
        using var details = CreateDetails(monitor);
        details.SetSelectedInstance(CreateInstanceItem());
        details.SetSelectedSection(CreateSection("saves"));
        details.SetPageActive(true);
        Assert.Equal([InstanceDirectoryKind.Saves], monitor.ActiveKinds);

        details.SetPageActive(false);

        Assert.Empty(monitor.ActiveKinds);
    }

    [Fact]
    public async Task ReenteringResourceSectionShowsCacheWhileSilentScanRuns()
    {
        var monitor = new RecordingDirectoryMonitor();
        var saveService = new RecordingSaveService();
        var originalPath = Path.Combine(TempRoot, "original");
        saveService.Items = [CreateSave("Original", originalPath)];
        using var details = CreateDetails(monitor, saveService: saveService);
        details.SetSelectedInstance(CreateInstanceItem());
        details.SetPageActive(true);
        details.SetSelectedSection(CreateSection("saves"));
        await details.SaveManagement.OnSectionActivatedAsync();
        var cachedItem = Assert.Single(details.SaveManagement.Saves);
        var entranceAnimationToken = details.SaveManagement.ListEntranceAnimationToken;

        details.SetSelectedSection(CreateSection("general"));
        Assert.True(details.SaveManagement.HasLoadedSaves);
        saveService.Items =
        [
            CreateSave("Original updated", originalPath),
            CreateSave("Added while hidden", Path.Combine(TempRoot, "hidden"))
        ];
        var releaseSilentRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        saveService.WaitBeforeSecondCall = releaseSilentRefresh.Task;
        details.SetSelectedSection(CreateSection("saves"));
        await saveService.WaitForCallAsync(2);
        var silentRefresh = details.SaveManagement.OnSectionActivatedAsync();

        Assert.True(details.SaveManagement.HasLoadedSaves);
        Assert.False(details.SaveManagement.IsLoadingSaves);
        Assert.Same(cachedItem, Assert.Single(details.SaveManagement.Saves));
        Assert.Equal(entranceAnimationToken, details.SaveManagement.ListEntranceAnimationToken);

        releaseSilentRefresh.TrySetResult();
        await silentRefresh;

        Assert.Equal(2, saveService.CallCount);
        Assert.Equal(2, details.SaveManagement.InstalledSaveCount);
        Assert.Same(
            cachedItem,
            details.SaveManagement.Saves.Single(save =>
                string.Equals(save.FullPath, originalPath, StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(entranceAnimationToken, details.SaveManagement.ListEntranceAnimationToken);
        details.SetPageActive(false);
    }

    [Fact]
    public async Task FirstResourceEntryStillUsesInitialLoadingState()
    {
        var monitor = new RecordingDirectoryMonitor();
        var saveService = new RecordingSaveService
        {
            Items = [CreateSave("First", Path.Combine(TempRoot, "first"))]
        };
        var releaseInitialLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        saveService.WaitBeforeFirstCall = releaseInitialLoad.Task;
        using var details = CreateDetails(monitor, saveService: saveService);
        details.SetSelectedInstance(CreateInstanceItem());
        details.SetPageActive(true);
        details.SetSelectedSection(CreateSection("saves"));
        await saveService.WaitForCallAsync(1);
        var initialLoad = details.SaveManagement.OnSectionActivatedAsync();

        Assert.True(details.SaveManagement.IsLoadingSaves);
        Assert.False(details.SaveManagement.HasLoadedSaves);

        releaseInitialLoad.TrySetResult();
        await initialLoad;

        Assert.False(details.SaveManagement.IsLoadingSaves);
        Assert.True(details.SaveManagement.HasLoadedSaves);
        Assert.Single(details.SaveManagement.Saves);
        details.SetPageActive(false);
    }

    [Fact]
    public async Task ReenteringLargeModSectionShowsCacheBeforeSilentScanCompletes()
    {
        var monitor = new RecordingDirectoryMonitor();
        var modService = new RecordingModService();
        var originalPath = Path.Combine(TempRoot, "original.jar");
        modService.Items = [CreateMod("Original", originalPath)];
        using var details = CreateDetails(monitor, modService: modService);
        details.SetSelectedInstance(CreateInstanceItem());
        details.SetPageActive(true);
        details.SetSelectedSection(CreateSection("mod_management"));
        await details.ModManagement.OnSectionActivatedAsync();
        var cachedItem = Assert.Single(details.ModManagement.Mods);
        var entranceAnimationToken = details.ModManagement.ListEntranceAnimationToken;
        Assert.Equal(1, modService.CallCount);

        details.SetSelectedSection(CreateSection("general"));
        modService.Items =
        [
            CreateMod("Original updated", originalPath),
            CreateMod("Added while hidden", Path.Combine(TempRoot, "added.jar"))
        ];
        var releaseSilentRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        modService.WaitBeforeSecondCall = releaseSilentRefresh.Task;
        details.SetSelectedSection(CreateSection("mod_management"));
        await modService.WaitForCallAsync(2);
        var silentRefresh = details.ModManagement.OnSectionActivatedAsync();

        Assert.True(details.ModManagement.HasLoadedMods);
        Assert.False(details.ModManagement.IsLoadingMods);
        Assert.Same(cachedItem, Assert.Single(details.ModManagement.Mods));
        Assert.Equal(entranceAnimationToken, details.ModManagement.ListEntranceAnimationToken);

        releaseSilentRefresh.TrySetResult();
        await silentRefresh;

        Assert.Equal(2, details.ModManagement.InstalledModCount);
        Assert.Same(
            cachedItem,
            details.ModManagement.Mods.Single(mod =>
                string.Equals(mod.FullPath, originalPath, StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(entranceAnimationToken, details.ModManagement.ListEntranceAnimationToken);
        details.SetPageActive(false);
    }

    [Fact]
    public async Task FailedSilentRefreshKeepsCachedContent()
    {
        var monitor = new RecordingDirectoryMonitor();
        var saveService = new RecordingSaveService
        {
            Items = [CreateSave("Cached", Path.Combine(TempRoot, "cached"))]
        };
        using var details = CreateDetails(monitor, saveService: saveService);
        details.SetSelectedInstance(CreateInstanceItem());
        details.SetPageActive(true);
        details.SetSelectedSection(CreateSection("saves"));
        await details.SaveManagement.OnSectionActivatedAsync();
        var cachedItem = Assert.Single(details.SaveManagement.Saves);

        details.SetSelectedSection(CreateSection("general"));
        saveService.FailSecondCall = true;
        details.SetSelectedSection(CreateSection("saves"));
        await details.SaveManagement.OnSectionActivatedAsync();

        Assert.True(details.SaveManagement.HasLoadedSaves);
        Assert.False(details.SaveManagement.IsLoadingSaves);
        Assert.Same(cachedItem, Assert.Single(details.SaveManagement.Saves));
        details.SetPageActive(false);
    }

    [Fact]
    public async Task CommittedDeletionDoesNotRestartOldInstanceWatcher()
    {
        var monitor = new RecordingDirectoryMonitor();
        var instanceDirectory = Directory.CreateDirectory(Path.Combine(TempRoot, "deleted-instance")).FullName;
        var instance = CreateInstanceItem(instanceDirectory);
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(instance.Instance);
        instanceService.DeleteCallback = () => Directory.Delete(instanceDirectory, recursive: true);
        using var details = CreateDetails(monitor, instanceService: instanceService);
        details.SetSelectedInstance(instance);
        details.SetSelectedSection(CreateSection("mod_management"));
        details.SetPageActive(true);
        Assert.Equal(1, monitor.WatchStartCount);
        var dialogs = new GameSettingsDialogsViewModel(instanceService, Stub<IStatusService>(), details);
        dialogs.OpenDeleteInstance(instance);

        await dialogs.ConfirmDeleteInstanceDialogCommand.ExecuteAsync(null);

        Assert.False(Directory.Exists(instanceDirectory));
        Assert.Empty(monitor.ActiveKinds);
        Assert.Equal(1, monitor.WatchStartCount);
        Assert.Null(details.SelectedInstance);
        details.SetPageActive(false);
    }

    [Fact]
    public async Task DeletionDialogRemainsBusyUntilBackendCommits()
    {
        var monitor = new RecordingDirectoryMonitor();
        var instance = CreateInstanceItem();
        var deletionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deletionCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var instanceService = new FakeGameInstanceService
        {
            DeleteHandler = async (_, _) =>
            {
                deletionStarted.TrySetResult();
                return await deletionCompletion.Task;
            }
        };
        instanceService.CreatedInstances.Add(instance.Instance);
        using var details = CreateDetails(monitor, instanceService: instanceService);
        details.SetSelectedInstance(instance);
        details.SetSelectedSection(CreateSection("mod_management"));
        details.SetPageActive(true);
        var dialogs = new GameSettingsDialogsViewModel(instanceService, Stub<IStatusService>(), details);
        GameSettingsInstanceItem? deletedInstance = null;
        dialogs.InstanceDeleted += deleted => deletedInstance = deleted;
        dialogs.OpenDeleteInstance(instance);

        var deletion = dialogs.ConfirmDeleteInstanceDialogCommand.ExecuteAsync(null);
        await deletionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(dialogs.IsDeleteInstanceDialogOpen);
        Assert.True(dialogs.IsDeleteInstanceDialogBusy);
        Assert.False(dialogs.HasDeleteInstanceDialogError);
        Assert.Same(instance, dialogs.InstancePendingDelete);
        Assert.Equal(Strings.Dialog_DeleteInstanceBusyTitle, dialogs.DeleteInstanceDialogTitle);
        Assert.Equal(
            string.Format(Strings.Dialog_DeleteInstanceBusyMessageFormat, instance.Name),
            dialogs.DeleteInstanceDialogMessage);
        Assert.Equal(Strings.Dialog_DeleteInstanceBusyTitle, dialogs.DeleteInstanceDialogActionText);
        Assert.False(dialogs.CanShowDeleteInstanceCancelButton);
        Assert.False(dialogs.CancelDeleteInstanceDialogCommand.CanExecute(null));
        Assert.False(dialogs.ConfirmDeleteInstanceDialogCommand.CanExecute(null));

        dialogs.CancelDeleteInstanceDialogCommand.Execute(null);
        dialogs.OpenDeleteInstance(CreateInstanceItem(Path.Combine(TempRoot, "other-instance")));
        await dialogs.ConfirmDeleteInstanceDialogCommand.ExecuteAsync(null);
        Assert.True(dialogs.IsDeleteInstanceDialogOpen);
        Assert.Same(instance, dialogs.InstancePendingDelete);
        Assert.Equal(1, instanceService.DeleteCallCount);

        deletionCompletion.SetResult(true);
        await deletion;

        Assert.False(dialogs.IsDeleteInstanceDialogOpen);
        Assert.False(dialogs.IsDeleteInstanceDialogBusy);
        Assert.Null(dialogs.InstancePendingDelete);
        Assert.Same(instance, deletedInstance);
        Assert.Null(details.SelectedInstance);
        Assert.Empty(monitor.ActiveKinds);
        details.SetPageActive(false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedDeletionRemainsOpenAndCanBeRetried(bool throwException)
    {
        var monitor = new RecordingDirectoryMonitor();
        var instance = CreateInstanceItem();
        var instanceService = new FakeGameInstanceService
        {
            DeleteHandler = throwException
                ? (_, _) => Task.FromException<bool>(new IOException("delete failed"))
                : (_, _) => Task.FromResult(false)
        };
        instanceService.CreatedInstances.Add(instance.Instance);
        using var details = CreateDetails(monitor, instanceService: instanceService);
        details.SetSelectedInstance(instance);
        details.SetSelectedSection(CreateSection("mod_management"));
        details.SetPageActive(true);
        var dialogs = new GameSettingsDialogsViewModel(instanceService, Stub<IStatusService>(), details);
        dialogs.OpenDeleteInstance(instance);

        await dialogs.ConfirmDeleteInstanceDialogCommand.ExecuteAsync(null);

        Assert.True(dialogs.IsDeleteInstanceDialogOpen);
        Assert.False(dialogs.IsDeleteInstanceDialogBusy);
        Assert.True(dialogs.HasDeleteInstanceDialogError);
        Assert.Same(instance, dialogs.InstancePendingDelete);
        Assert.Equal(Strings.Dialog_DeleteInstanceFailedTitle, dialogs.DeleteInstanceDialogTitle);
        Assert.Equal(Strings.Status_DeleteInstanceFailed, dialogs.DeleteInstanceDialogMessage);
        Assert.Equal(Strings.Retry_Button, dialogs.DeleteInstanceDialogActionText);
        Assert.True(dialogs.CanShowDeleteInstanceCancelButton);
        Assert.True(dialogs.CancelDeleteInstanceDialogCommand.CanExecute(null));
        Assert.True(dialogs.ConfirmDeleteInstanceDialogCommand.CanExecute(null));
        Assert.Equal([InstanceDirectoryKind.Mods], monitor.ActiveKinds);
        Assert.Same(instance, details.SelectedInstance);

        instanceService.DeleteHandler = (_, _) => Task.FromResult(true);
        await dialogs.ConfirmDeleteInstanceDialogCommand.ExecuteAsync(null);

        Assert.False(dialogs.IsDeleteInstanceDialogOpen);
        Assert.False(dialogs.IsDeleteInstanceDialogBusy);
        Assert.False(dialogs.HasDeleteInstanceDialogError);
        Assert.Null(dialogs.InstancePendingDelete);
        Assert.Equal(2, instanceService.DeleteCallCount);
        Assert.Empty(monitor.ActiveKinds);
        Assert.Null(details.SelectedInstance);
        details.SetPageActive(false);
    }

    [Fact]
    public async Task FailedDeletionRestoresOnlyCurrentResourceWatcher()
    {
        var monitor = new RecordingDirectoryMonitor();
        var instance = CreateInstanceItem();
        var instanceService = new FakeGameInstanceService();
        using var details = CreateDetails(monitor, instanceService: instanceService);
        details.SetSelectedInstance(instance);
        details.SetSelectedSection(CreateSection("mod_management"));
        details.SetPageActive(true);
        var dialogs = new GameSettingsDialogsViewModel(instanceService, Stub<IStatusService>(), details);
        dialogs.OpenDeleteInstance(instance);

        await dialogs.ConfirmDeleteInstanceDialogCommand.ExecuteAsync(null);

        Assert.True(dialogs.IsDeleteInstanceDialogOpen);
        Assert.True(dialogs.HasDeleteInstanceDialogError);
        Assert.Equal([InstanceDirectoryKind.Mods], monitor.ActiveKinds);
        Assert.Equal(2, monitor.WatchStartCount);
        Assert.Same(instance, details.SelectedInstance);
        details.SetPageActive(false);
    }

    [Fact]
    public void RenameRecoveryWatchesOnlyTheNewInstancePath()
    {
        var monitor = new RecordingDirectoryMonitor();
        var original = CreateInstanceItem(Path.Combine(TempRoot, "old"));
        using var details = CreateDetails(monitor);
        details.SetSelectedInstance(original);
        details.SetSelectedSection(CreateSection("saves"));
        details.SetPageActive(true);

        details.SuspendLocalWatchersForInstanceMove();
        var renamed = CreateInstanceItem(Path.Combine(TempRoot, "new")).Instance;
        original.Update(renamed, "release");
        details.SetSelectedInstance(original);
        details.ResumeLocalWatchersAfterInstanceMove();

        Assert.Equal([InstanceDirectoryKind.Saves], monitor.ActiveKinds);
        Assert.Equal(2, monitor.WatchStartCount);
        Assert.Equal(renamed.InstanceDirectory, monitor.WatchedDirectories.Last(), ignoreCase: true);
        details.SetPageActive(false);
    }

    private static GameSettingsDetailsViewModel CreateDetails(
        RecordingDirectoryMonitor monitor,
        ILocalSaveService? saveService = null,
        IGameInstanceService? instanceService = null,
        IModService? modService = null)
    {
        var statusService = Stub<IStatusService>();
        var resolvedModService = modService ?? Stub<IModService>();
        var launchSettingsModService = Stub<IModService>();
        var localMods = new LocalModsViewModel(resolvedModService, statusService, monitor);
        var localSaves = new LocalSavesViewModel(saveService ?? Stub<ILocalSaveService>(), statusService, monitor);
        var localResourcePacks = new LocalResourcePacksViewModel(
            Stub<ILocalResourcePackService>(),
            statusService,
            monitor);
        var localShaderPacks = new LocalShaderPacksViewModel(
            Stub<ILocalShaderPackService>(),
            statusService,
            monitor);

        return new GameSettingsDetailsViewModel(
            null!,
            instanceService ?? Stub<IGameInstanceService>(),
            statusService,
            Stub<IInstanceFolderService>(),
            Stub<ISystemMemoryService>(),
            launchSettingsModService,
            Stub<IInstanceBackupService>(),
            new DownloadTasksPageViewModel(),
            localMods,
            localSaves,
            localResourcePacks,
            localShaderPacks,
            Stub<IJavaRuntimeDiscoveryService>(),
            Stub<IFilePickerService>(),
            Stub<IInstanceContentImportPathValidator>(),
            Stub<IFloatingMessageService>(),
            ImmediateUiDispatcher.Instance);
    }

    private static GameSettingsInstanceItem CreateInstanceItem(string? instanceDirectory = null) => new(
        new GameInstance
        {
            Id = "instance",
            Name = "Instance",
            VersionName = "1.21",
            MinecraftVersion = "1.21",
            Loader = LoaderKind.Fabric,
            InstanceDirectory = instanceDirectory
                ?? Path.Combine(Path.GetTempPath(), "launcher-tests", "watcher-lifecycle")
        },
        "release");

    private static GameSettingsDetailSectionItem CreateSection(string id) => new(id, id, string.Empty);

    private static bool GetHasLoaded(GameSettingsDetailsViewModel details, InstanceDirectoryKind kind) => kind switch
    {
        InstanceDirectoryKind.Mods => details.ModManagement.HasLoadedMods,
        InstanceDirectoryKind.Saves => details.SaveManagement.HasLoadedSaves,
        InstanceDirectoryKind.ResourcePacks => details.ResourcePackManagement.HasLoadedResourcePacks,
        InstanceDirectoryKind.ShaderPacks => details.ShaderPackManagement.HasLoadedShaderPacks,
        _ => false
    };

    private static bool GetIsLoading(GameSettingsDetailsViewModel details, InstanceDirectoryKind kind) => kind switch
    {
        InstanceDirectoryKind.Mods => details.ModManagement.IsLoadingMods,
        InstanceDirectoryKind.Saves => details.SaveManagement.IsLoadingSaves,
        InstanceDirectoryKind.ResourcePacks => details.ResourcePackManagement.IsLoadingResourcePacks,
        InstanceDirectoryKind.ShaderPacks => details.ShaderPackManagement.IsLoadingShaderPacks,
        _ => false
    };

    private static int GetEntranceAnimationToken(GameSettingsDetailsViewModel details, InstanceDirectoryKind kind) => kind switch
    {
        InstanceDirectoryKind.Mods => details.ModManagement.ListEntranceAnimationToken,
        InstanceDirectoryKind.Saves => details.SaveManagement.ListEntranceAnimationToken,
        InstanceDirectoryKind.ResourcePacks => details.ResourcePackManagement.ListEntranceAnimationToken,
        InstanceDirectoryKind.ShaderPacks => details.ShaderPackManagement.ListEntranceAnimationToken,
        _ => 0
    };

    private static LocalSave CreateSave(string name, string fullPath) => new()
    {
        Name = name,
        DirectoryName = Path.GetFileName(fullPath),
        FullPath = fullPath
    };

    private static LocalMod CreateMod(string name, string fullPath) => new()
    {
        Name = name,
        FileName = Path.GetFileName(fullPath),
        FullPath = fullPath,
        IsEnabled = true
    };

    private static T Stub<T>() where T : class => DispatchProxy.Create<T, DefaultInterfaceProxy>();

    public class DefaultInterfaceProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var returnType = targetMethod?.ReturnType;
            if (returnType is null || returnType == typeof(void))
                return null;
            if (returnType == typeof(Task))
                return Task.CompletedTask;
            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var resultType = returnType.GetGenericArguments()[0];
                return typeof(Task)
                    .GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(resultType)
                    .Invoke(null, [CreateDefaultResult(resultType)]);
            }

            return CreateDefaultResult(returnType);
        }

        private static object? CreateDefaultResult(Type resultType)
        {
            if (resultType.IsGenericType
                && resultType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
            {
                return Array.CreateInstance(resultType.GetGenericArguments()[0], 0);
            }

            return resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
        }
    }

    private sealed class RecordingDirectoryMonitor : IInstanceDirectoryMonitor
    {
        private readonly List<RecordingWatch> activeWatches = [];

        public IReadOnlyList<InstanceDirectoryKind> ActiveKinds => activeWatches
            .Where(watch => !watch.IsDisposed)
            .Select(watch => watch.Kind)
            .ToList();

        public bool StartedBeforePreviousWatchWasDisposed { get; private set; }
        public int WatchStartCount { get; private set; }
        public IReadOnlyList<string> WatchedDirectories => activeWatches.Select(watch => watch.Directory).ToList();

        public IInstanceDirectoryWatch Watch(GameInstance instance, InstanceDirectoryKind directoryKind)
        {
            if (activeWatches.Any(watch => !watch.IsDisposed))
                StartedBeforePreviousWatchWasDisposed = true;
            WatchStartCount++;
            var watch = new RecordingWatch(directoryKind, instance.InstanceDirectory);
            activeWatches.Add(watch);
            return watch;
        }
    }

    private sealed class RecordingSaveService : ILocalSaveService
    {
        private readonly TaskCompletionSource firstCall = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource secondCall = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<LocalSave> Items { get; set; } = [];
        public int CallCount { get; private set; }
        public Task? WaitBeforeFirstCall { get; set; }
        public Task? WaitBeforeSecondCall { get; set; }
        public bool FailSecondCall { get; set; }

        public async Task<IReadOnlyList<LocalSave>> GetSavesAsync(
            GameInstance instance,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (CallCount >= 1)
                firstCall.TrySetResult();
            if (CallCount >= 2)
                secondCall.TrySetResult();
            if (CallCount == 1 && WaitBeforeFirstCall is not null)
                await WaitBeforeFirstCall;
            if (CallCount == 2 && WaitBeforeSecondCall is not null)
                await WaitBeforeSecondCall;
            if (CallCount == 2 && FailSecondCall)
                throw new IOException("Controlled silent refresh failure.");
            return Items;
        }

        public Task WaitForCallAsync(int callCount) => (callCount <= 1 ? firstCall : secondCall).Task;

        public Task<LocalSaveImportResult> ImportFromArchiveAsync(
            GameInstance instance,
            string archivePath,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeleteAsync(LocalSave save, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(IEnumerable<LocalSave> saves, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingModService : IModService
    {
        private readonly TaskCompletionSource firstCall = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource secondCall = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<LocalMod> Items { get; set; } = [];
        public int CallCount { get; private set; }
        public Task? WaitBeforeSecondCall { get; set; }

        public async Task<IReadOnlyList<LocalMod>> GetModsAsync(
            GameInstance instance,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (CallCount >= 1)
                firstCall.TrySetResult();
            if (CallCount >= 2)
                secondCall.TrySetResult();
            if (CallCount == 2 && WaitBeforeSecondCall is not null)
                await WaitBeforeSecondCall;
            return Items;
        }

        public Task WaitForCallAsync(int callCount) => (callCount <= 1 ? firstCall : secondCall).Task;

        public Task<LocalMod> ImportAsync(
            GameInstance instance,
            string sourceJarPath,
            bool overwriteExisting = false,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SetEnabledAsync(
            LocalMod mod,
            bool enabled,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeleteAsync(LocalMod mod, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingWatch(InstanceDirectoryKind kind, string directory) : IInstanceDirectoryWatch
    {
        public InstanceDirectoryKind Kind { get; } = kind;
        public string Directory { get; } = directory;
        public bool IsDisposed { get; private set; }
        public event EventHandler<InstanceDirectoryChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
