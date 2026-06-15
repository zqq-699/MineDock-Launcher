using Launcher.App.Resources;
using Launcher.App.ViewModels;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests;

public sealed class GameSettingsPageViewModelTests
{
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

        viewModel.InstanceSearchQuery = "fabric";

        Assert.Equal(2, viewModel.ListEntranceAnimationToken);
    }

    [Fact]
    public async Task GameSettingsPageRefreshesInstancesWhenCategoryIsClickedAgain()
    {
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla));
        instanceService.CreatedInstances.Add(CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric));
        var viewModel = new GameSettingsPageViewModel(
            instanceService,
            new FakeGameVersionService([]));

        await viewModel.EnsureInstancesLoadedAsync();
        Assert.Equal(["Vanilla World", "Fabric Pack"], viewModel.VisibleInstances.Select(instance => instance.Name));
        Assert.Equal(1, instanceService.GetInstancesCallCount);

        instanceService.CreatedInstances.RemoveAll(instance => instance.Name == "Fabric Pack");
        await viewModel.SelectInstanceCategoryCommand.ExecuteAsync(viewModel.SelectedInstanceCategory);

        Assert.Equal(["Vanilla World"], viewModel.VisibleInstances.Select(instance => instance.Name));
        Assert.Equal(2, instanceService.GetInstancesCallCount);
    }

    [Fact]
    public async Task GameSettingsPageClearsVisibleInstancesBeforeRequestingEntranceAnimation()
    {
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla));
        instanceService.CreatedInstances.Add(CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric));
        var viewModel = new GameSettingsPageViewModel(
            instanceService,
            new FakeGameVersionService([]));

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
        await viewModel.SelectInstanceCategoryCommand.ExecuteAsync(viewModel.SelectedInstanceCategory);

        Assert.Equal(
            [
                nameof(GameSettingsPageViewModel.VisibleInstances),
                nameof(GameSettingsPageViewModel.ListEntranceAnimationToken),
                nameof(GameSettingsPageViewModel.VisibleInstances)
            ],
            changedProperties);
        Assert.Equal(["Vanilla World"], viewModel.VisibleInstances.Select(instance => instance.Name));
    }

    [Fact]
    public async Task GameSettingsPageShowsFriendlyErrorWhenInstancesFailToLoad()
    {
        var viewModel = new GameSettingsPageViewModel(
            new ThrowingGameInstanceService(new InvalidOperationException("disk exploded")),
            new FakeGameVersionService([]));

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
        var viewModel = new GameSettingsPageViewModel(
            instanceService,
            new ThrowingGameVersionService());

        await viewModel.EnsureInstancesLoadedAsync();

        Assert.Equal(["Fabric Pack"], viewModel.VisibleInstances.Select(instance => instance.Name));
        Assert.False(viewModel.HasInstanceLoadError);

        await viewModel.SelectInstanceCategoryCommand.ExecuteAsync(viewModel.InstanceCategories.Single(category => category.Id == "release"));
        Assert.Empty(viewModel.VisibleInstances);

        await viewModel.SelectInstanceCategoryCommand.ExecuteAsync(viewModel.InstanceCategories.Single(category => category.Id == "mod_loader"));
        Assert.Equal(["Fabric Pack"], viewModel.VisibleInstances.Select(instance => instance.Name));
    }

    private static GameSettingsPageViewModel CreateViewModel(
        IReadOnlyList<GameInstance> instances,
        IReadOnlyList<MinecraftVersionInfo>? versions = null)
    {
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.AddRange(instances);
        return new GameSettingsPageViewModel(
            instanceService,
            new FakeGameVersionService(versions ?? []));
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
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };
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
    }
}
