using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.GameSettings;

public sealed class GameSettingsPageViewModelTests
{
    [Fact]
    public void GameSettingsPageUsesDedicatedCategoryIcons()
    {
        var viewModel = CreateViewModel([]);

        Assert.Equal(
            "general/general_all_application",
            viewModel.InstanceCategories.Single(category => category.Id == "all").IconKey);
        Assert.Equal(
            "general/general_extention",
            viewModel.InstanceCategories.Single(category => category.Id == "mod_loader").IconKey);
    }

    [Fact]
    public async Task GameSettingsPageShowsAllDownloadedInstancesByDefault()
    {
        var viewModel = CreateViewModel(
        [
            CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla),
            CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric)
        ],
        [
            new MinecraftVersionInfo("1.21.4", "Release", false),
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);

        await viewModel.EnsureInstancesLoadedAsync();

        Assert.Equal(Strings.GameSettings_AllCategory, viewModel.PageTitle);
        Assert.True(viewModel.HasVisibleInstances);
        Assert.Equal(["Vanilla World", "Fabric Pack"], viewModel.VisibleInstances.Select(instance => instance.Name));
        Assert.False(viewModel.HasInstanceEmptyMessage);
    }

    [Fact]
    public async Task GameSettingsPageOrdersNewlyCreatedInstancesFirst()
    {
        var older = CreateInstance("Older World", "1.20.1", LoaderKind.Vanilla);
        older.CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var newer = CreateInstance("Newer World", "1.21.4", LoaderKind.Fabric);
        newer.CreatedAt = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var viewModel = CreateViewModel([older, newer]);

        await viewModel.EnsureInstancesLoadedAsync();

        Assert.Equal(["Newer World", "Older World"], viewModel.VisibleInstances.Select(instance => instance.Name));
    }

    [Fact]
    public async Task GameSettingsPageShowsOnlyModLoaderInstancesForModCategory()
    {
        var viewModel = CreateViewModel(
        [
            CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla),
            CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric),
            CreateInstance("Forge Pack", "1.19.2", LoaderKind.Forge)
        ]);

        await viewModel.EnsureInstancesLoadedAsync();
        await viewModel.SelectInstanceCategoryCommand.ExecuteAsync(viewModel.InstanceCategories.Single(category => category.Id == "mod_loader"));

        Assert.Equal(["Fabric Pack", "Forge Pack"], viewModel.VisibleInstances.Select(instance => instance.Name));
        Assert.All(viewModel.VisibleInstances, instance => Assert.True(instance.HasModLoader));
    }

    [Fact]
    public async Task GameSettingsPageFiltersInstancesByMinecraftVersionType()
    {
        var viewModel = CreateViewModel(
        [
            CreateInstance("Release World", "1.21.4", LoaderKind.Vanilla),
            CreateInstance("Snapshot World", "24w45a", LoaderKind.Vanilla),
            CreateInstance("Beta World", "b1.7.3", LoaderKind.Vanilla),
            CreateInstance("Alpha World", "a1.2.6", LoaderKind.Vanilla)
        ],
        [
            new MinecraftVersionInfo("1.21.4", "Release", false),
            new MinecraftVersionInfo("24w45a", "Snapshot", false),
            new MinecraftVersionInfo("b1.7.3", "old_beta", false),
            new MinecraftVersionInfo("a1.2.6", "old_alpha", false)
        ]);

        await viewModel.EnsureInstancesLoadedAsync();

        await viewModel.SelectInstanceCategoryCommand.ExecuteAsync(viewModel.InstanceCategories.Single(category => category.Id == "release"));
        Assert.Equal(["Release World"], viewModel.VisibleInstances.Select(instance => instance.Name));

        await viewModel.SelectInstanceCategoryCommand.ExecuteAsync(viewModel.InstanceCategories.Single(category => category.Id == "snapshot"));
        Assert.Equal(["Snapshot World"], viewModel.VisibleInstances.Select(instance => instance.Name));

        await viewModel.SelectInstanceCategoryCommand.ExecuteAsync(viewModel.InstanceCategories.Single(category => category.Id == "old_beta"));
        Assert.Equal(["Beta World"], viewModel.VisibleInstances.Select(instance => instance.Name));

        await viewModel.SelectInstanceCategoryCommand.ExecuteAsync(viewModel.InstanceCategories.Single(category => category.Id == "old_alpha"));
        Assert.Equal(["Alpha World"], viewModel.VisibleInstances.Select(instance => instance.Name));
    }

    [Fact]
    public async Task GameSettingsPageUsesDiscoveredLoaderAndVersionTypeForCategories()
    {
        var vanillaRelease = CreateInstance("Imported Vanilla", "1.21.4", LoaderKind.Vanilla);
        vanillaRelease.VersionType = "release";
        var fabricSnapshot = CreateInstance("Imported Fabric", "1.21.4", LoaderKind.Fabric);
        fabricSnapshot.VersionType = "snapshot";
        var viewModel = CreateViewModel([vanillaRelease, fabricSnapshot], []);

        await viewModel.EnsureInstancesLoadedAsync();

        await viewModel.SelectInstanceCategoryCommand.ExecuteAsync(viewModel.InstanceCategories.Single(category => category.Id == "mod_loader"));
        Assert.Equal(["Imported Fabric"], viewModel.VisibleInstances.Select(instance => instance.Name));

        await viewModel.SelectInstanceCategoryCommand.ExecuteAsync(viewModel.InstanceCategories.Single(category => category.Id == "snapshot"));
        Assert.Equal(["Imported Fabric"], viewModel.VisibleInstances.Select(instance => instance.Name));

        await viewModel.SelectInstanceCategoryCommand.ExecuteAsync(viewModel.InstanceCategories.Single(category => category.Id == "release"));
        Assert.Equal(["Imported Vanilla"], viewModel.VisibleInstances.Select(instance => instance.Name));
    }

    [Fact]
    public async Task GameSettingsPageUsesLoaderDefaultIconsForModdedInstances()
    {
        var viewModel = CreateViewModel(
        [
            CreateInstance("Fabric Pack", "1.21.4", LoaderKind.Fabric),
            CreateInstance("Forge Pack", "1.20.1", LoaderKind.Forge)
        ]);

        await viewModel.EnsureInstancesLoadedAsync();

        Assert.Equal("/Assets/Icons/block/fabric.png", viewModel.VisibleInstances[0].IconSource);
        Assert.Equal("/Assets/Icons/block/Anvil.png", viewModel.VisibleInstances[1].IconSource);
    }

    [Fact]
    public async Task GameSettingsPageSearchesInstanceMetadata()
    {
        var viewModel = CreateViewModel(
        [
            CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla),
            CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric)
        ],
        [
            new MinecraftVersionInfo("1.21.4", "Release", false),
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);

        await viewModel.EnsureInstancesLoadedAsync();

        viewModel.InstanceSearchQuery = "fabric";
        Assert.Equal(["Fabric Pack"], viewModel.VisibleInstances.Select(instance => instance.Name));

        viewModel.InstanceSearchQuery = "1.21";
        Assert.Equal(["Vanilla World"], viewModel.VisibleInstances.Select(instance => instance.Name));
    }

    [Fact]
    public async Task GameSettingsPageRequestsEntranceAnimationForInitialLoadAndCategorySwitch()
    {
        var viewModel = CreateViewModel(
        [
            CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla),
            CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric)
        ]);

        Assert.Equal(0, viewModel.ListEntranceAnimationToken);

        await viewModel.EnsureInstancesLoadedAsync();

        Assert.Equal(1, viewModel.ListEntranceAnimationToken);

        await viewModel.SelectInstanceCategoryCommand.ExecuteAsync(viewModel.InstanceCategories.Single(category => category.Id == "mod_loader"));

        Assert.Equal(2, viewModel.ListEntranceAnimationToken);

        await viewModel.SelectInstanceCategoryCommand.ExecuteAsync(viewModel.SelectedInstanceCategory);

        Assert.Equal(2, viewModel.ListEntranceAnimationToken);

        await viewModel.RefreshInstancesForPageActivationAsync();

        Assert.Equal(2, viewModel.ListEntranceAnimationToken);

        viewModel.InstanceSearchQuery = "fabric";

        Assert.Equal(2, viewModel.ListEntranceAnimationToken);
    }

    [Fact]
    public async Task GameSettingsPageRefreshesInstancesWhenCategoryIsClickedAgain()
    {
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla));
        instanceService.CreatedInstances.Add(CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric));
        var viewModel = CreateViewModel(instanceService, new FakeGameVersionService([]), new FakeStatusService(), new FakeInstanceFolderService());

        await viewModel.EnsureInstancesLoadedAsync();
        Assert.Equal(["Vanilla World", "Fabric Pack"], viewModel.VisibleInstances.Select(instance => instance.Name));
        Assert.Equal(1, instanceService.GetInstancesCallCount);
        Assert.Equal(1, viewModel.ListEntranceAnimationToken);

        instanceService.CreatedInstances.RemoveAll(instance => instance.Name == "Fabric Pack");
        await viewModel.SelectInstanceCategoryCommand.ExecuteAsync(viewModel.SelectedInstanceCategory);

        Assert.Equal(["Vanilla World"], viewModel.VisibleInstances.Select(instance => instance.Name));
        Assert.Equal(2, instanceService.GetInstancesCallCount);
        Assert.Equal(1, viewModel.ListEntranceAnimationToken);
    }

    [Fact]
    public async Task GameSettingsPageClearsVisibleInstancesBeforeRequestingEntranceAnimation()
    {
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla));
        instanceService.CreatedInstances.Add(CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric));
        var viewModel = CreateViewModel(instanceService, new FakeGameVersionService([]), new FakeStatusService(), new FakeInstanceFolderService());

        await viewModel.EnsureInstancesLoadedAsync();

        var changedProperties = new List<string>();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(GameSettingsPageViewModel.VisibleInstances)
                or nameof(GameSettingsPageViewModel.ListEntranceAnimationToken))
            {
                changedProperties.Add(e.PropertyName);
            }
        };

        await viewModel.SelectInstanceCategoryCommand.ExecuteAsync(viewModel.InstanceCategories.Single(category => category.Id == "mod_loader"));

        Assert.Equal(
            [
                nameof(GameSettingsPageViewModel.VisibleInstances),
                nameof(GameSettingsPageViewModel.ListEntranceAnimationToken),
                nameof(GameSettingsPageViewModel.VisibleInstances)
            ],
            changedProperties);
        Assert.Equal(["Fabric Pack"], viewModel.VisibleInstances.Select(instance => instance.Name));
    }

    [Fact]
    public async Task GameSettingsPageSilentRefreshDoesNotClearVisibleInstancesOrReplayEntranceAnimation()
    {
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla));
        instanceService.CreatedInstances.Add(CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric));
        var viewModel = CreateViewModel(instanceService, new FakeGameVersionService([]), new FakeStatusService(), new FakeInstanceFolderService());

        await viewModel.EnsureInstancesLoadedAsync();

        var changedProperties = new List<string>();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(GameSettingsPageViewModel.VisibleInstances)
                or nameof(GameSettingsPageViewModel.ListEntranceAnimationToken))
            {
                changedProperties.Add(e.PropertyName);
            }
        };

        instanceService.CreatedInstances.RemoveAll(instance => instance.Name == "Fabric Pack");
        await viewModel.RefreshInstancesSilentlyAsync();

        Assert.Equal([nameof(GameSettingsPageViewModel.VisibleInstances)], changedProperties);
        Assert.Equal(1, viewModel.ListEntranceAnimationToken);
        Assert.Equal(["Vanilla World"], viewModel.VisibleInstances.Select(instance => instance.Name));
    }

    [Fact]
    public async Task GameSettingsPageSilentRefreshReusesVisibleItems()
    {
        var instanceService = new FakeGameInstanceService();
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        instanceService.CreatedInstances.Add(instance);
        var viewModel = CreateViewModel(instanceService, new FakeGameVersionService([]), new FakeStatusService(), new FakeInstanceFolderService());

        await viewModel.EnsureInstancesLoadedAsync();
        var visibleItem = viewModel.VisibleInstances.Single();
        var updatedInstance = CreateInstance("Renamed World", "1.21.4", LoaderKind.Vanilla);
        updatedInstance.Id = instance.Id;
        instanceService.CreatedInstances[0] = updatedInstance;

        await viewModel.RefreshInstancesSilentlyAsync();

        Assert.Same(visibleItem, viewModel.VisibleInstances.Single());
        Assert.Equal("Renamed World", viewModel.VisibleInstances.Single().Name);
        Assert.Equal(1, viewModel.ListEntranceAnimationToken);
    }

    [Fact]
    public async Task GameSettingsPageShowsFriendlyErrorWhenInstancesFailToLoad()
    {
        var viewModel = CreateViewModel(
            new ThrowingGameInstanceService(new InvalidOperationException("disk exploded")),
            new FakeGameVersionService([]),
            new FakeStatusService(),
            new FakeInstanceFolderService());

        await viewModel.EnsureInstancesLoadedAsync();

        Assert.Empty(viewModel.VisibleInstances);
        Assert.True(viewModel.HasInstanceLoadError);
        Assert.Equal(Strings.Status_LoadInstancesFailed, viewModel.InstanceLoadError);
        Assert.DoesNotContain("disk exploded", viewModel.InstanceLoadError);
    }

    [Fact]
    public async Task GameSettingsPageKeepsInstancesWhenVersionIndexFails()
    {
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric));
        var viewModel = CreateViewModel(
            instanceService,
            new ThrowingGameVersionService(),
            new FakeStatusService(),
            new FakeInstanceFolderService());

        await viewModel.EnsureInstancesLoadedAsync();

        Assert.Equal(["Fabric Pack"], viewModel.VisibleInstances.Select(instance => instance.Name));
        Assert.False(viewModel.HasInstanceLoadError);

        await viewModel.SelectInstanceCategoryCommand.ExecuteAsync(viewModel.InstanceCategories.Single(category => category.Id == "release"));
        Assert.Empty(viewModel.VisibleInstances);

        await viewModel.SelectInstanceCategoryCommand.ExecuteAsync(viewModel.InstanceCategories.Single(category => category.Id == "mod_loader"));
        Assert.Equal(["Fabric Pack"], viewModel.VisibleInstances.Select(instance => instance.Name));
    }

    [Fact]
    public async Task OpenDeleteInstanceDialogTracksPendingInstance()
    {
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.AddRange(
        [
            CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla),
            CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric)
        ]);
        var viewModel = CreateViewModel(instanceService, new FakeGameVersionService([]), new FakeStatusService(), new FakeInstanceFolderService());

        await viewModel.EnsureInstancesLoadedAsync();
        var item = viewModel.VisibleInstances.First();

        viewModel.OpenDeleteInstanceDialogCommand.Execute(item);

        Assert.True(viewModel.IsDeleteInstanceDialogOpen);
        Assert.Equal(item.Name, viewModel.InstancePendingDelete?.Name);
        Assert.Contains(item.Name, viewModel.DeleteInstanceDialogMessage);
    }

    [Fact]
    public async Task CancelDeleteInstanceDialogClosesWithoutDeleting()
    {
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla));
        var viewModel = CreateViewModel(instanceService, new FakeGameVersionService([]), new FakeStatusService(), new FakeInstanceFolderService());

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.OpenDeleteInstanceDialogCommand.Execute(viewModel.VisibleInstances.Single());

        viewModel.CancelDeleteInstanceDialogCommand.Execute(null);

        Assert.False(viewModel.IsDeleteInstanceDialogOpen);
        Assert.Null(viewModel.InstancePendingDelete);
        Assert.Equal(0, instanceService.DeleteCallCount);
    }

    [Fact]
    public async Task ConfirmDeleteInstanceDialogRefreshesVisibleInstancesAndRaisesSyncEvent()
    {
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.AddRange(
        [
            CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla),
            CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric)
        ]);
        var statusService = new FakeStatusService();
        var viewModel = CreateViewModel(instanceService, new FakeGameVersionService([]), statusService, new FakeInstanceFolderService());
        var syncRequested = 0;
        viewModel.InstancesChanged += () => syncRequested++;

        await viewModel.EnsureInstancesLoadedAsync();
        var item = viewModel.VisibleInstances.Single(instance => instance.Name == "Fabric Pack");
        viewModel.OpenDeleteInstanceDialogCommand.Execute(item);

        await viewModel.ConfirmDeleteInstanceDialogCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsDeleteInstanceDialogOpen);
        Assert.Single(viewModel.VisibleInstances);
        Assert.Equal("Vanilla World", viewModel.VisibleInstances.Single().Name);
        Assert.Equal(1, instanceService.DeleteCallCount);
        Assert.Equal(1, syncRequested);
        Assert.Equal(string.Format(Strings.Status_InstanceDeletedFormat, "Fabric Pack"), statusService.LastMessage);
    }

    [Fact]
    public async Task ConfirmDeleteInstanceDialogDoesNotReplayListEntranceAnimation()
    {
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.AddRange(
        [
            CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla),
            CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric)
        ]);
        var viewModel = CreateViewModel(instanceService, new FakeGameVersionService([]), new FakeStatusService(), new FakeInstanceFolderService());

        await viewModel.EnsureInstancesLoadedAsync();
        Assert.Equal(1, viewModel.ListEntranceAnimationToken);
        Assert.Equal(1, instanceService.GetInstancesCallCount);

        var item = viewModel.VisibleInstances.Single(instance => instance.Name == "Fabric Pack");
        viewModel.OpenDeleteInstanceDialogCommand.Execute(item);

        await viewModel.ConfirmDeleteInstanceDialogCommand.ExecuteAsync(null);

        Assert.Equal(1, viewModel.ListEntranceAnimationToken);
        Assert.Single(viewModel.VisibleInstances);
        Assert.Equal(1, instanceService.GetInstancesCallCount);
    }

    [Fact]
    public async Task OpenInstanceFolderCommandUsesFolderService()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        Directory.CreateDirectory(instance.InstanceDirectory);
        var folderService = new FakeInstanceFolderService();
        var viewModel = CreateViewModel([instance], [], new FakeStatusService(), folderService);

        await viewModel.EnsureInstancesLoadedAsync();

        viewModel.OpenInstanceFolderCommand.Execute(viewModel.VisibleInstances.Single());

        Assert.Equal(instance.InstanceDirectory, folderService.LastOpenedPath);
    }

    [Fact]
    public async Task OpenInstanceFolderCommandReportsFriendlyErrorWhenFolderIsMissing()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        instance.InstanceDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var statusService = new FakeStatusService();
        var folderService = new FakeInstanceFolderService();
        var viewModel = CreateViewModel([instance], [], statusService, folderService);

        await viewModel.EnsureInstancesLoadedAsync();

        viewModel.OpenInstanceFolderCommand.Execute(viewModel.VisibleInstances.Single());

        Assert.Null(folderService.LastOpenedPath);
        Assert.Equal(Strings.Status_InstanceFolderNotFound, statusService.LastMessage);
    }

    [Fact]
    public async Task SelectInstanceAndGoHomeCommandRaisesLaunchNavigationRequest()
    {
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla));
        var viewModel = CreateViewModel(instanceService, new FakeGameVersionService([]), new FakeStatusService(), new FakeInstanceFolderService());
        GameInstance? requestedInstance = null;
        viewModel.LaunchInstanceRequested += instance => requestedInstance = instance;

        await viewModel.EnsureInstancesLoadedAsync();

        viewModel.SelectInstanceAndGoHomeCommand.Execute(viewModel.VisibleInstances.Single());

        Assert.NotNull(requestedInstance);
        Assert.Equal("Vanilla World", requestedInstance!.Name);
    }

    private static GameSettingsPageViewModel CreateViewModel(
        IReadOnlyList<GameInstance> instances,
        IReadOnlyList<MinecraftVersionInfo>? versions = null,
        FakeStatusService? statusService = null,
        FakeInstanceFolderService? folderService = null)
    {
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.AddRange(instances);
        return CreateViewModel(instanceService, new FakeGameVersionService(versions ?? []), statusService ?? new FakeStatusService(), folderService ?? new FakeInstanceFolderService());
    }

    private static GameSettingsPageViewModel CreateViewModel(
        IGameInstanceService instanceService,
        IGameVersionService gameVersionService,
        FakeStatusService statusService,
        FakeInstanceFolderService folderService)
    {
        return new GameSettingsPageViewModel(instanceService, gameVersionService, statusService, folderService);
    }

    private static GameInstance CreateInstance(string name, string minecraftVersion, LoaderKind loader)
    {
        return new GameInstance
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            MinecraftVersion = minecraftVersion,
            Loader = loader,
            LoaderVersion = loader is LoaderKind.Vanilla ? null : "latest",
            VersionName = name,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            InstanceDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"))
        };
    }

    private sealed class FakeStatusService : IStatusService
    {
        public event Action<string>? MessageReported;

        public string? LastMessage { get; private set; }

        public void Report(string message)
        {
            LastMessage = message;
            MessageReported?.Invoke(message);
        }
    }

    private sealed class FakeInstanceFolderService : IInstanceFolderService
    {
        public string? LastOpenedPath { get; private set; }

        public bool TryOpen(string folderPath)
        {
            LastOpenedPath = folderPath;
            return true;
        }
    }

    private sealed class ThrowingGameVersionService : IGameVersionService
    {
        public Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("version index unavailable");
        }
    }

    private sealed class ThrowingGameInstanceService : IGameInstanceService
    {
        private readonly Exception exception;

        public ThrowingGameInstanceService(Exception exception)
        {
            this.exception = exception;
        }

        public Task<IReadOnlyList<GameInstance>> GetInstancesAsync(CancellationToken cancellationToken = default)
        {
            throw exception;
        }

        public Task<GameInstance?> GetDefaultInstanceAsync(CancellationToken cancellationToken = default)
        {
            throw exception;
        }

        public Task<GameInstance> CreateInstanceAsync(
            string minecraftVersion,
            LoaderKind loader,
            string? loaderVersion,
            string? name,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default)
        {
            throw exception;
        }

        public Task SaveInstanceAsync(GameInstance instance, CancellationToken cancellationToken = default)
        {
            throw exception;
        }

        public Task<bool> SetDefaultInstanceAsync(string instanceId, CancellationToken cancellationToken = default)
        {
            throw exception;
        }

        public Task<bool> DeleteInstanceAsync(string instanceId, CancellationToken cancellationToken = default)
        {
            throw exception;
        }
    }
}
