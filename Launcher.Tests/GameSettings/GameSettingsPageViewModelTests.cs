using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.GameSettings;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure;
using Launcher.Infrastructure.FileSystem;
using Launcher.Infrastructure.Persistence;
using Launcher.Tests.Helpers;

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
    public async Task GameSettingsPageUsesFixedInstanceSubtitleFormat()
    {
        var vanilla = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        var fabric = CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric);
        fabric.LoaderVersion = "0.16.10";
        var forge = CreateInstance("Forge Pack", "26.1.1", LoaderKind.Forge);
        forge.LoaderVersion = "63.0.2";
        var viewModel = CreateViewModel([vanilla, fabric, forge]);

        await viewModel.EnsureInstancesLoadedAsync();

        Assert.Equal("1.21.4", viewModel.VisibleInstances.Single(instance => instance.Name == "Vanilla World").Subtitle);
        Assert.Equal("1.20.1 Fabric 0.16.10", viewModel.VisibleInstances.Single(instance => instance.Name == "Fabric Pack").Subtitle);
        Assert.Equal("26.1.1 Forge 63.0.2", viewModel.VisibleInstances.Single(instance => instance.Name == "Forge Pack").Subtitle);
    }

    [Fact]
    public async Task GameSettingsPageHidesMixinSuffixFromFabricSubtitle()
    {
        var fabric = CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric);
        fabric.LoaderVersion = "0.16.10+mixin.0.8.7";
        var viewModel = CreateViewModel([fabric]);

        await viewModel.EnsureInstancesLoadedAsync();

        Assert.Equal("1.20.1 Fabric 0.16.10", viewModel.VisibleInstances.Single().Subtitle);
    }

    [Fact]
    public async Task GameSettingsPageShowsUnknownMinecraftVersionForImportedInstanceWithoutResolvedVersion()
    {
        var imported = CreateInstance("Imported Pack", string.Empty, LoaderKind.Fabric);
        imported.LoaderVersion = "0.16.10";
        var viewModel = CreateViewModel([imported]);

        await viewModel.EnsureInstancesLoadedAsync();

        Assert.Equal("未知版本 Fabric 0.16.10", viewModel.VisibleInstances.Single().Subtitle);
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
    public async Task DetailsDeleteGameCommandOpensDeleteDialogForSelectedInstance()
    {
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla));
        var viewModel = CreateViewModel(instanceService, new FakeGameVersionService([]), new FakeStatusService(), new FakeInstanceFolderService());

        await viewModel.EnsureInstancesLoadedAsync();
        var item = viewModel.VisibleInstances.Single();
        viewModel.SelectInstanceCommand.Execute(item);

        viewModel.Details.RequestDeleteInstanceCommand.Execute(null);

        Assert.True(viewModel.IsDeleteInstanceDialogOpen);
        Assert.Same(item, viewModel.InstancePendingDelete);
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

    [Fact]
    public async Task SelectInstanceCommandOpensDetailsStepAndUsesSelectedInstanceHeader()
    {
        var viewModel = CreateViewModel(
        [
            CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla),
            CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric)
        ]);

        await viewModel.EnsureInstancesLoadedAsync();
        var selected = viewModel.VisibleInstances.Single(instance => instance.Name == "Fabric Pack");

        viewModel.SelectInstanceCommand.Execute(selected);

        Assert.True(viewModel.IsDetailsStep);
        Assert.False(viewModel.IsListStep);
        Assert.Same(selected, viewModel.SelectedInstance);
        Assert.Equal("Fabric Pack", viewModel.PageTitle);
        Assert.Equal(selected.IconSource, viewModel.PageTitleIconSource);
        Assert.Equal("Fabric Pack", viewModel.Details.InstanceName);
        Assert.Equal(Strings.GameSettings_DetailGeneral, viewModel.Details.SectionTitle);
        Assert.IsType<InstanceGeneralSettingsViewModel>(viewModel.Details.CurrentSectionViewModel);
        Assert.True(viewModel.DetailSections.First(section => section.Id == "general").IsSelected);
        Assert.Equal(9, viewModel.DetailSections.Count);
        Assert.Equal(
            [
                Strings.GameSettings_DetailGeneral,
                Strings.GameSettings_DetailLaunch,
                Strings.GameSettings_DetailJava,
                Strings.GameSettings_DetailModManagement,
                Strings.GameSettings_DetailSaves,
                Strings.GameSettings_DetailShaders,
                Strings.GameSettings_DetailLoader,
                Strings.GameSettings_DetailAdvanced,
                Strings.GameSettings_DetailBackup
            ],
            viewModel.DetailSections.Select(section => section.Title));
    }

    [Fact]
    public async Task JavaDetailsSectionKeepsEntryAndShowsPlaceholder()
    {
        var viewModel = CreateViewModel([CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla)]);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        var section = viewModel.DetailSections.Single(section => section.Id == "java");

        viewModel.SelectDetailsSectionCommand.Execute(section);

        Assert.True(section.IsSelected);
        Assert.True(viewModel.Details.IsJavaSection);
        Assert.IsType<InstanceJavaSettingsViewModel>(viewModel.Details.CurrentSectionViewModel);
        Assert.Equal(Strings.GameSettings_DetailJava, viewModel.Details.SectionTitle);
        Assert.Equal(
            string.Format(Strings.GameSettings_DetailPlaceholderBodyFormat, Strings.GameSettings_DetailJava),
            viewModel.Details.SectionPlaceholderBody);
    }

    [Fact]
    public async Task ModManagementDetailsSectionUsesDedicatedViewModel()
    {
        var viewModel = CreateViewModel([CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric)]);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        var section = viewModel.DetailSections.Single(item => item.Id == "mod_management");

        viewModel.SelectDetailsSectionCommand.Execute(section);

        Assert.True(section.IsSelected);
        Assert.IsType<InstanceModManagementSettingsViewModel>(viewModel.Details.CurrentSectionViewModel);
        Assert.Same(viewModel.Details.ModManagement, viewModel.Details.CurrentSectionViewModel);
        Assert.Equal(Strings.GameSettings_DetailModManagement, viewModel.Details.SectionTitle);
    }

    [Fact]
    public async Task ModManagementViewModelLoadsRealModsForSelectedInstance()
    {
        var instance = CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric);
        var modService = new FakeModService();
        modService.ModsByInstanceId[instance.Id] =
        [
            CreateLocalMod("sodium-fabric-0.5.13.jar", true, instance.InstanceDirectory, loader: "fabric", modId: "sodium", version: "0.5.13"),
            CreateLocalMod("lithium-fabric-0.14.7.jar", false, instance.InstanceDirectory, loader: "forge", modId: "lithium", version: "0.14.7")
        ];
        var viewModel = CreateViewModel([instance], modService: modService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        await TestAsync.WaitForAsync(() => viewModel.Details.ModManagement.Mods.Count == 2);

        var modManagement = viewModel.Details.ModManagement;
        Assert.Equal(2, modManagement.InstalledModCount);
        Assert.Equal(1, modManagement.EnabledModCount);
        Assert.Equal(
            string.Format(Strings.GameSettings_ModManagementInstalledSummaryFormat, 2, 1),
            modManagement.InstalledSummaryText);
        Assert.Equal(
            ["sodium-fabric-0.5.13", "lithium-fabric-0.14.7"],
            modManagement.Mods.Select(mod => mod.Title));
        Assert.Equal(
            ["fabric-sodium-0.5.13", "forge-lithium-0.14.7"],
            modManagement.Mods.Select(mod => mod.Subtitle));
        Assert.Equal(
            [Strings.GameSettings_ModManagementEnabledState, Strings.GameSettings_ModManagementDisabledState],
            modManagement.Mods.Select(mod => mod.TrailingText));
        Assert.True(modManagement.HasMods);
        Assert.False(modManagement.CanShowModEmptyState);
        Assert.Same(modManagement.Mods[0], modManagement.SelectedMod);

        modManagement.SelectModCommand.Execute(modManagement.Mods[1]);

        Assert.Same(modManagement.Mods[1], modManagement.SelectedMod);
        Assert.True(modManagement.Mods[1].IsSelected);
        Assert.False(modManagement.Mods[0].IsSelected);
    }

    [Fact]
    public async Task ModManagementViewModelTogglesMultiSelectAndSelectsVisibleRows()
    {
        var instance = CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric);
        var modService = new FakeModService();
        modService.ModsByInstanceId[instance.Id] =
        [
            CreateLocalMod("sodium.jar", true, instance.InstanceDirectory, loader: "fabric", modId: "sodium", version: "1.0.0"),
            CreateLocalMod("lithium.jar", false, instance.InstanceDirectory, loader: "fabric", modId: "lithium", version: "1.0.0")
        ];
        var viewModel = CreateViewModel([instance], modService: modService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        await TestAsync.WaitForAsync(() => viewModel.Details.ModManagement.Mods.Count == 2);

        var modManagement = viewModel.Details.ModManagement;
        modManagement.ToggleMultiSelectModeCommand.Execute(null);
        modManagement.SelectModCommand.Execute(modManagement.Mods[0]);
        modManagement.SelectModCommand.Execute(modManagement.Mods[1]);

        Assert.True(modManagement.IsMultiSelectMode);
        Assert.Equal(2, modManagement.SelectedModCount);
        Assert.True(modManagement.HasSelectedMods);
        Assert.Null(modManagement.SelectedMod);
        Assert.True(modManagement.Mods.All(mod => mod.IsSelected));

        modManagement.ToggleMultiSelectModeCommand.Execute(null);

        Assert.False(modManagement.IsMultiSelectMode);
        Assert.Equal(0, modManagement.SelectedModCount);
        Assert.Single(modManagement.Mods.Where(mod => mod.IsSelected));
        Assert.NotNull(modManagement.SelectedMod);
    }

    [Fact]
    public async Task ModManagementSelectAllOnlyTargetsVisibleMods()
    {
        var instance = CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric);
        var modService = new FakeModService();
        modService.ModsByInstanceId[instance.Id] =
        [
            CreateLocalMod("sodium.jar", true, instance.InstanceDirectory, loader: "fabric", modId: "sodium", version: "1.0.0"),
            CreateLocalMod("lithium.jar", true, instance.InstanceDirectory, loader: "fabric", modId: "lithium", version: "1.0.0"),
            CreateLocalMod("forge-helper.jar", true, instance.InstanceDirectory, loader: "forge", modId: "forge-helper", version: "1.0.0")
        ];
        var viewModel = CreateViewModel([instance], modService: modService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        await TestAsync.WaitForAsync(() => viewModel.Details.ModManagement.Mods.Count == 3);

        var modManagement = viewModel.Details.ModManagement;
        modManagement.ModSearchQuery = "fabric";
        modManagement.ToggleMultiSelectModeCommand.Execute(null);
        modManagement.SelectAllModsCommand.Execute(null);

        Assert.Equal(2, modManagement.Mods.Count);
        Assert.Equal(2, modManagement.SelectedModCount);
        Assert.True(modManagement.AreAllVisibleModsSelected);
        Assert.All(modManagement.Mods, mod => Assert.True(mod.IsSelected));
    }

    [Fact]
    public async Task ModManagementSelectAllButtonTogglesToClearSelectionWhenAllVisibleModsAreSelected()
    {
        var instance = CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric);
        var modService = new FakeModService();
        modService.ModsByInstanceId[instance.Id] =
        [
            CreateLocalMod("sodium.jar", true, instance.InstanceDirectory, loader: "fabric", modId: "sodium", version: "1.0.0"),
            CreateLocalMod("lithium.jar", true, instance.InstanceDirectory, loader: "fabric", modId: "lithium", version: "1.0.0")
        ];
        var viewModel = CreateViewModel([instance], modService: modService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        await TestAsync.WaitForAsync(() => viewModel.Details.ModManagement.Mods.Count == 2);

        var modManagement = viewModel.Details.ModManagement;
        modManagement.ToggleMultiSelectModeCommand.Execute(null);

        Assert.Equal(Strings.GameSettings_ModManagementSelectAllButton, modManagement.SelectAllButtonText);

        modManagement.SelectAllModsCommand.Execute(null);

        Assert.True(modManagement.AreAllVisibleModsSelected);
        Assert.Equal(2, modManagement.SelectedModCount);
        Assert.Equal(Strings.GameSettings_ModManagementCancelSelectAllButton, modManagement.SelectAllButtonText);

        modManagement.SelectAllModsCommand.Execute(null);

        Assert.False(modManagement.AreAllVisibleModsSelected);
        Assert.Equal(0, modManagement.SelectedModCount);
        Assert.Equal(Strings.GameSettings_ModManagementSelectAllButton, modManagement.SelectAllButtonText);
        Assert.All(modManagement.Mods, mod => Assert.False(mod.IsSelected));
    }

    [Fact]
    public async Task ModManagementBatchDisableRefreshesAndKeepsMultiSelectMode()
    {
        var instance = CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric);
        var statusService = new FakeStatusService();
        var modService = new FakeModService();
        modService.ModsByInstanceId[instance.Id] =
        [
            CreateLocalMod("sodium.jar", true, instance.InstanceDirectory, loader: "fabric", modId: "sodium", version: "1.0.0"),
            CreateLocalMod("lithium.jar", true, instance.InstanceDirectory, loader: "fabric", modId: "lithium", version: "1.0.0")
        ];
        var viewModel = CreateViewModel([instance], statusService: statusService, modService: modService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        await TestAsync.WaitForAsync(() => viewModel.Details.ModManagement.Mods.Count == 2);

        var modManagement = viewModel.Details.ModManagement;
        modManagement.ToggleMultiSelectModeCommand.Execute(null);
        modManagement.SelectAllModsCommand.Execute(null);
        await modManagement.DisableSelectedModsCommand.ExecuteAsync(null);

        Assert.True(modManagement.IsMultiSelectMode);
        Assert.Equal(2, modManagement.SelectedModCount);
        Assert.Equal(0, modManagement.EnabledModCount);
        Assert.All(modManagement.Mods, mod => Assert.False(mod.IsEnabled));
        Assert.All(modManagement.Mods, mod => Assert.True(mod.IsSelected));
        Assert.All(modService.ModsByInstanceId[instance.Id], mod => Assert.False(mod.IsEnabled));
        Assert.Equal(
            string.Format(Strings.Status_SelectedModsDisabledFormat, 2),
            statusService.LastMessage);
    }

    [Fact]
    public async Task ModManagementBatchToggleKeepsItemOrderStable()
    {
        var instance = CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric);
        var modService = new FakeModService();
        modService.ModsByInstanceId[instance.Id] =
        [
            CreateLocalMod("apple-skin.jar", true, instance.InstanceDirectory, loader: "fabric", modId: "apple-skin", version: "1.0.0"),
            CreateLocalMod("zeta.jar", false, instance.InstanceDirectory, loader: "fabric", modId: "zeta", version: "1.0.0")
        ];
        var viewModel = CreateViewModel([instance], modService: modService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        await TestAsync.WaitForAsync(() => viewModel.Details.ModManagement.Mods.Count == 2);

        var modManagement = viewModel.Details.ModManagement;
        var originalOrder = modManagement.Mods.Select(mod => mod.Title).ToArray();

        modManagement.ToggleMultiSelectModeCommand.Execute(null);
        modManagement.SelectAllModsCommand.Execute(null);
        await modManagement.DisableSelectedModsCommand.ExecuteAsync(null);

        Assert.Equal(originalOrder, modManagement.Mods.Select(mod => mod.Title));

        await modManagement.EnableSelectedModsCommand.ExecuteAsync(null);

        Assert.Equal(originalOrder, modManagement.Mods.Select(mod => mod.Title));
    }

    [Fact]
    public async Task ModManagementDeleteUsesConfirmationDialogBeforeRemovingMods()
    {
        var instance = CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric);
        var modService = new FakeModService();
        modService.ModsByInstanceId[instance.Id] =
        [
            CreateLocalMod("sodium.jar", true, instance.InstanceDirectory, loader: "fabric", modId: "sodium", version: "1.0.0"),
            CreateLocalMod("lithium.jar", true, instance.InstanceDirectory, loader: "fabric", modId: "lithium", version: "1.0.0")
        ];
        var viewModel = CreateViewModel([instance], modService: modService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        await TestAsync.WaitForAsync(() => viewModel.Details.ModManagement.Mods.Count == 2);

        var modManagement = viewModel.Details.ModManagement;
        modManagement.ToggleMultiSelectModeCommand.Execute(null);
        modManagement.SelectAllModsCommand.Execute(null);
        modManagement.RequestDeleteSelectedModsCommand.Execute(null);

        Assert.True(viewModel.IsDeleteModsDialogOpen);
        Assert.Equal(
            string.Format(Strings.Dialog_DeleteMultipleModsMessageFormat, 2),
            viewModel.DeleteModsDialogMessage);

        viewModel.CancelDeleteModsDialogCommand.Execute(null);

        Assert.False(viewModel.IsDeleteModsDialogOpen);
        Assert.Equal(2, modService.ModsByInstanceId[instance.Id].Count);

        modManagement.RequestDeleteSelectedModsCommand.Execute(null);
        await viewModel.ConfirmDeleteModsDialogCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsDeleteModsDialogOpen);
        Assert.Empty(modService.ModsByInstanceId[instance.Id]);
        Assert.Empty(modManagement.Mods);
        Assert.False(modManagement.IsMultiSelectMode);
    }

    [Fact]
    public async Task ModManagementViewModelUsesResolvedIconSourceWhenAvailable()
    {
        var instance = CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric);
        var modService = new FakeModService();
        modService.ModsByInstanceId[instance.Id] =
        [
            CreateLocalMod(
                "with-icon.jar",
                true,
                instance.InstanceDirectory,
                "file:///C:/launcher/cache/mod-icon.png",
                "Pretty Mod Name",
                "fabric",
                "pretty-mod",
                "1.2.3")
        ];
        var viewModel = CreateViewModel([instance], modService: modService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        await TestAsync.WaitForAsync(() => viewModel.Details.ModManagement.Mods.Count == 1);

        var mod = Assert.Single(viewModel.Details.ModManagement.Mods);
        Assert.Equal("Pretty Mod Name", mod.Title);
        Assert.Equal("fabric-pretty-mod-1.2.3", mod.Subtitle);
        Assert.Equal("file:///C:/launcher/cache/mod-icon.png", mod.IconSource);
        Assert.Equal(string.Empty, mod.IconKey);
    }

    [Fact]
    public async Task ModManagementOpenFolderCommandOpensInstanceModsDirectory()
    {
        var instance = CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric);
        var folderService = new FakeInstanceFolderService();
        var viewModel = CreateViewModel([instance], folderService: folderService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());

        viewModel.Details.ModManagement.OpenModFolderCommand.Execute(null);

        Assert.Equal(Path.Combine(instance.InstanceDirectory, "mods"), folderService.LastOpenedPath);
    }

    [Fact]
    public async Task ModManagementImportLocalModCommandImportsSelectedModFile()
    {
        var instance = CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric);
        var statusService = new FakeStatusService();
        var modService = new FakeModService();
        var tempModPath = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "sodium.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(tempModPath)!);
        await File.WriteAllTextAsync(tempModPath, "fake mod");
        var filePickerService = new FakeFilePickerService
        {
            ModFilePath = tempModPath
        };
        var viewModel = CreateViewModel([instance], statusService: statusService, filePickerService: filePickerService, modService: modService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        await TestAsync.WaitForAsync(() => viewModel.Details.ModManagement is not null);

        await viewModel.Details.ModManagement.ImportLocalModCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Details.ModManagement.Mods);
        Assert.Equal("sodium", viewModel.Details.ModManagement.Mods[0].Title);
        Assert.Equal(1, viewModel.Details.ModManagement.InstalledModCount);
        Assert.Equal(1, viewModel.Details.ModManagement.EnabledModCount);
        Assert.Equal(Strings.Status_LocalModImported, statusService.LastMessage);
    }

    [Fact]
    public async Task ModManagementImportLocalModCommandPromptsBeforeReplacingExistingFile()
    {
        var instance = CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric);
        var statusService = new FakeStatusService();
        var modService = new FakeModService();
        modService.ModsByInstanceId[instance.Id] =
        [
            CreateLocalMod("sodium.jar", true, instance.InstanceDirectory, loader: "fabric", modId: "sodium", version: "1.0.0")
        ];
        var tempModPath = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "sodium.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(tempModPath)!);
        await File.WriteAllTextAsync(tempModPath, "replacement mod");
        var filePickerService = new FakeFilePickerService
        {
            ModFilePath = tempModPath
        };
        var viewModel = CreateViewModel([instance], statusService: statusService, filePickerService: filePickerService, modService: modService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        await TestAsync.WaitForAsync(() => viewModel.Details.ModManagement.Mods.Count == 1);

        await viewModel.Details.ModManagement.ImportLocalModCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsReplaceModImportDialogOpen);
        Assert.Equal(
            string.Format(Strings.Dialog_ReplaceModImportMessageFormat, "sodium.jar"),
            viewModel.ReplaceModImportDialogMessage);
        Assert.Single(viewModel.Details.ModManagement.Mods);

        viewModel.CancelReplaceModImportDialogCommand.Execute(null);

        Assert.False(viewModel.IsReplaceModImportDialogOpen);
        Assert.Single(viewModel.Details.ModManagement.Mods);

        await viewModel.Details.ModManagement.ImportLocalModCommand.ExecuteAsync(null);
        await viewModel.ConfirmReplaceModImportDialogCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsReplaceModImportDialogOpen);
        Assert.Single(viewModel.Details.ModManagement.Mods);
        Assert.Equal(Strings.Status_LocalModImported, statusService.LastMessage);
    }

    [Fact]
    public async Task ModManagementToggleModEnabledCommandUpdatesSingleItemWithoutChangingOrder()
    {
        var instance = CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric);
        var modService = new FakeModService();
        modService.ModsByInstanceId[instance.Id] =
        [
            CreateLocalMod("apple-skin.jar", true, instance.InstanceDirectory, loader: "fabric", modId: "apple-skin", version: "1.0.0"),
            CreateLocalMod("zeta.jar", false, instance.InstanceDirectory, loader: "fabric", modId: "zeta", version: "1.0.0")
        ];
        var viewModel = CreateViewModel([instance], modService: modService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        await TestAsync.WaitForAsync(() => viewModel.Details.ModManagement.Mods.Count == 2);

        var modManagement = viewModel.Details.ModManagement;
        var secondMod = modManagement.Mods[1];

        await modManagement.ToggleModEnabledCommand.ExecuteAsync(secondMod);

        Assert.Equal(["apple-skin", "zeta"], modManagement.Mods.Select(mod => mod.Title));
        Assert.True(modManagement.Mods[1].IsEnabled);
        Assert.Same(modManagement.Mods[1], modManagement.SelectedMod);

        var firstMod = modManagement.Mods[0];
        await modManagement.ToggleModEnabledCommand.ExecuteAsync(firstMod);

        Assert.Equal(["apple-skin", "zeta"], modManagement.Mods.Select(mod => mod.Title));
        Assert.False(modManagement.Mods[0].IsEnabled);
        Assert.Same(modManagement.Mods[0], modManagement.SelectedMod);
    }

    [Fact]
    public async Task ModManagementOpenModFileLocationCommandRevealsModFile()
    {
        var instance = CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric);
        var folderService = new FakeInstanceFolderService();
        var modService = new FakeModService();
        modService.ModsByInstanceId[instance.Id] =
        [
            CreateLocalMod("sodium.jar", true, instance.InstanceDirectory, loader: "fabric", modId: "sodium", version: "1.0.0")
        ];
        var viewModel = CreateViewModel([instance], folderService: folderService, modService: modService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        await TestAsync.WaitForAsync(() => viewModel.Details.ModManagement.Mods.Count == 1);

        viewModel.Details.ModManagement.OpenModFileLocationCommand.Execute(viewModel.Details.ModManagement.Mods[0]);

        Assert.Equal(viewModel.Details.ModManagement.Mods[0].FullPath, folderService.LastRevealedFilePath);
    }

    [Fact]
    public async Task ModManagementRequestDeleteModUsesSingleItemConfirmationDialog()
    {
        var instance = CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric);
        var modService = new FakeModService();
        modService.ModsByInstanceId[instance.Id] =
        [
            CreateLocalMod("sodium.jar", true, instance.InstanceDirectory, loader: "fabric", modId: "sodium", version: "1.0.0")
        ];
        var viewModel = CreateViewModel([instance], modService: modService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        await TestAsync.WaitForAsync(() => viewModel.Details.ModManagement.Mods.Count == 1);

        var mod = viewModel.Details.ModManagement.Mods[0];
        viewModel.Details.ModManagement.RequestDeleteModCommand.Execute(mod);

        Assert.True(viewModel.IsDeleteModsDialogOpen);
        Assert.Equal(
            string.Format(Strings.Dialog_DeleteSingleModMessageFormat, mod.Title),
            viewModel.DeleteModsDialogMessage);
    }

    [Fact]
    public async Task ModManagementViewModelRefreshesWhenModFolderChanges()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var instance = CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric);
            instance.InstanceDirectory = Path.Combine(tempRoot, "instances", "fabric-pack");
            Directory.CreateDirectory(instance.InstanceDirectory);
            Directory.CreateDirectory(Path.Combine(instance.InstanceDirectory, "mods"));

            var modService = new ModService(new LauncherPathProvider(tempRoot));
            var viewModel = CreateViewModel([instance], modService: modService);

            await viewModel.EnsureInstancesLoadedAsync();
            viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
            await TestAsync.WaitForAsync(() => viewModel.Details.ModManagement.InstalledModCount == 0);

            CreateModJar(
                Path.Combine(instance.InstanceDirectory, "mods", "sodium.jar"),
                """
                {
                  "schemaVersion": 1,
                  "id": "sodium",
                  "version": "1.0.0",
                  "name": "Sodium"
                }
                """);

            await TestAsync.WaitForAsync(() =>
                viewModel.Details.ModManagement.Mods.Count == 1
                && viewModel.Details.ModManagement.Mods[0].Title == "Sodium");

            var mod = Assert.Single(viewModel.Details.ModManagement.Mods);
            Assert.Equal("Sodium", mod.Title);
            Assert.Equal("fabric-sodium-1.0.0", mod.Subtitle);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ModManagementViewModelFallsBackToDefaultIconWhenResolvedIconMissing()
    {
        var instance = CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric);
        var modService = new FakeModService();
        modService.ModsByInstanceId[instance.Id] =
        [
            CreateLocalMod("without-icon.jar", true, instance.InstanceDirectory)
        ];
        var viewModel = CreateViewModel([instance], modService: modService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        await TestAsync.WaitForAsync(() => viewModel.Details.ModManagement.Mods.Count == 1);

        var mod = Assert.Single(viewModel.Details.ModManagement.Mods);
        Assert.Null(mod.IconSource);
        Assert.Equal("instance_setting_page/mod", mod.IconKey);
    }

    [Fact]
    public async Task ModManagementViewModelShowsEmptyStateWhenNoModsAvailable()
    {
        var instance = CreateInstance("Fabric Pack", "1.21.4", LoaderKind.Fabric);
        var modService = new FakeModService();
        modService.ModsByInstanceId[instance.Id] = [];
        var viewModel = CreateViewModel([instance], modService: modService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        await TestAsync.WaitForAsync(() => viewModel.Details.ModManagement.InstalledModCount == 0);

        Assert.Empty(viewModel.Details.ModManagement.Mods);
        Assert.True(viewModel.Details.ModManagement.IsModManagementSupported);
        Assert.True(viewModel.Details.ModManagement.CanShowModInfoSection);
        Assert.False(viewModel.Details.ModManagement.HasMods);
        Assert.False(viewModel.Details.ModManagement.HasInstalledMods);
        Assert.False(viewModel.Details.ModManagement.CanShowModListSection);
        Assert.True(viewModel.Details.ModManagement.CanShowNoModsEmptyState);
        Assert.False(viewModel.Details.ModManagement.CanShowModEmptyState);
        Assert.False(viewModel.Details.ModManagement.CanShowModUnavailableState);
        Assert.Equal(Strings.GameSettings_ModManagementEmptyMessage, viewModel.Details.ModManagement.ModEmptyMessage);
    }

    [Fact]
    public async Task ModManagementViewModelShowsUnavailableStateForVanillaInstance()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        var modService = new FakeModService();
        modService.ModsByInstanceId[instance.Id] = [];
        var viewModel = CreateViewModel([instance], modService: modService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        await TestAsync.WaitForAsync(() => viewModel.Details.ModManagement.InstalledModCount == 0);

        Assert.False(viewModel.Details.ModManagement.IsModManagementSupported);
        Assert.False(viewModel.Details.ModManagement.CanShowModInfoSection);
        Assert.False(viewModel.Details.ModManagement.CanShowModListSection);
        Assert.False(viewModel.Details.ModManagement.CanShowNoModsEmptyState);
        Assert.False(viewModel.Details.ModManagement.CanShowModEmptyState);
        Assert.True(viewModel.Details.ModManagement.CanShowModUnavailableState);
        Assert.Equal(Strings.GameSettings_ModManagementUnavailableMessage, viewModel.Details.ModManagement.ModUnavailableMessage);
    }

    [Fact]
    public async Task ModManagementViewModelFiltersModsBySearchQueryWithoutChangingSummary()
    {
        var instance = CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric);
        var modService = new FakeModService();
        modService.ModsByInstanceId[instance.Id] =
        [
            CreateLocalMod("sodium-fabric-0.5.13.jar", true, instance.InstanceDirectory, loader: "fabric", modId: "sodium", version: "0.5.13"),
            CreateLocalMod("lithium-fabric-0.14.7.jar", false, instance.InstanceDirectory, loader: "fabric", modId: "lithium", version: "0.14.7")
        ];
        var viewModel = CreateViewModel([instance], modService: modService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        await TestAsync.WaitForAsync(() => viewModel.Details.ModManagement.Mods.Count == 2);

        viewModel.Details.ModManagement.ModSearchQuery = "sodium";

        Assert.Single(viewModel.Details.ModManagement.Mods);
        Assert.Equal("sodium-fabric-0.5.13", viewModel.Details.ModManagement.Mods[0].Title);
        Assert.Equal(2, viewModel.Details.ModManagement.InstalledModCount);
        Assert.Equal(1, viewModel.Details.ModManagement.EnabledModCount);
        Assert.True(viewModel.Details.ModManagement.CanShowModListSection);
        Assert.False(viewModel.Details.ModManagement.CanShowNoModsEmptyState);
        Assert.False(viewModel.Details.ModManagement.CanShowModEmptyState);

        viewModel.Details.ModManagement.ModSearchQuery = "missing-mod";

        Assert.Empty(viewModel.Details.ModManagement.Mods);
        Assert.True(viewModel.Details.ModManagement.CanShowModListSection);
        Assert.False(viewModel.Details.ModManagement.CanShowNoModsEmptyState);
        Assert.True(viewModel.Details.ModManagement.CanShowModEmptyState);
        Assert.Equal(Strings.GameSettings_ModManagementSearchEmptyMessage, viewModel.Details.ModManagement.ModEmptyMessage);
        Assert.Equal(2, viewModel.Details.ModManagement.InstalledModCount);
        Assert.Equal(1, viewModel.Details.ModManagement.EnabledModCount);
    }

    [Fact]
    public async Task ModManagementViewModelReportsFriendlyErrorWhenModLoadFails()
    {
        var instance = CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric);
        var statusService = new FakeStatusService();
        var modService = new FakeModService
        {
            GetModsException = new InvalidOperationException("mod folder exploded")
        };
        var viewModel = CreateViewModel([instance], statusService: statusService, modService: modService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        await TestAsync.WaitForAsync(() => statusService.LastMessage == Strings.Status_LoadLocalModsFailed);

        Assert.Empty(viewModel.Details.ModManagement.Mods);
        Assert.Equal(Strings.Status_LoadLocalModsFailed, statusService.LastMessage);
    }

    [Fact]
    public async Task BackToInstanceListCommandReturnsToListStep()
    {
        var viewModel = CreateViewModel([CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla)]);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());

        viewModel.BackToInstanceListCommand.Execute(null);

        Assert.True(viewModel.IsListStep);
        Assert.False(viewModel.IsDetailsStep);
        Assert.Equal(Strings.GameSettings_AllCategory, viewModel.PageTitle);
        Assert.Null(viewModel.PageTitleIconSource);
    }

    [Fact]
    public async Task DescriptionAutoSavePersistsGeneralDescription()
    {
        var instanceService = new FakeGameInstanceService();
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        instanceService.CreatedInstances.Add(instance);
        var statusService = new FakeStatusService();
        var viewModel = CreateViewModel(instanceService, new FakeGameVersionService([]), statusService, new FakeInstanceFolderService());

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        viewModel.Details.DescriptionText = "A cool world";

        await TestAsync.WaitForAsync(() => instanceService.SaveCallCount == 1);

        Assert.Equal("A cool world", viewModel.SelectedInstance?.Instance.Description);
        Assert.Equal("A cool world", instanceService.LastSavedInstance?.Description);
        Assert.Equal(1, instanceService.SaveCallCount);
        Assert.Null(statusService.LastMessage);
    }

    [Fact]
    public async Task LaunchSettingsAutoSavePersistsSelectedOptions()
    {
        var instanceService = new FakeGameInstanceService();
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        instanceService.CreatedInstances.Add(instance);
        var viewModel = CreateViewModel(instanceService, new FakeGameVersionService([]), new FakeStatusService(), new FakeInstanceFolderService());

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        viewModel.SelectDetailsSectionCommand.Execute(viewModel.DetailSections.Single(section => section.Id == "launch"));

        Assert.Equal(LaunchSettingsMode.UseGlobal, viewModel.Details.SelectedLaunchSettingsModeOption?.Mode);
        Assert.False(viewModel.Details.AreLaunchSettingsOverridesEnabled);

        viewModel.Details.SelectedLaunchSettingsModeOption = viewModel.Details.LaunchSettingsModeOptions
            .Single(option => option.Mode == LaunchSettingsMode.PerInstance);

        viewModel.Details.LaunchCheckFilesBeforeLaunchEnabled = false;
        viewModel.Details.LaunchAutoRepairMissingFilesEnabled = false;
        viewModel.Details.LaunchMinimizeLauncherAfterLaunchEnabled = true;
        viewModel.Details.LaunchFullScreenEnabled = true;
        viewModel.Details.LaunchPreLaunchCommand = "echo before";
        viewModel.Details.LaunchWaitForPreLaunchCommand = false;
        viewModel.Details.LaunchPostExitCommand = "echo after";
        viewModel.Details.LaunchJvmArguments = "-Dfoo=bar";
        viewModel.Details.LaunchGameArguments = "--demo";
        viewModel.Details.SelectedMemoryModeOption = viewModel.Details.MemoryModeOptions
            .Single(option => option.Mode == MemorySettingsMode.Manual);
        viewModel.Details.MemoryMb = 5120;

        await TestAsync.WaitForAsync(() =>
            instanceService.SaveCallCount >= 11
            && instanceService.LastSavedInstance is not null
            && instanceService.LastSavedInstance.LaunchSettingsMode == LaunchSettingsMode.PerInstance
            && instanceService.LastSavedInstance.MemorySettingsMode == MemorySettingsMode.Manual
            && instanceService.LastSavedInstance.MemoryMb == 5120
            && !instanceService.LastSavedInstance.CheckFilesBeforeLaunch
            && !instanceService.LastSavedInstance.AutoRepairMissingFiles
            && instanceService.LastSavedInstance.MinimizeLauncherAfterLaunch
            && instanceService.LastSavedInstance.LaunchFullScreen
            && instanceService.LastSavedInstance.PreLaunchCommand == "echo before"
            && !instanceService.LastSavedInstance.WaitForPreLaunchCommand
            && instanceService.LastSavedInstance.PostExitCommand == "echo after"
            && instanceService.LastSavedInstance.JvmArguments == "-Dfoo=bar"
            && instanceService.LastSavedInstance.GameArguments == "--demo");

        Assert.Equal(LaunchSettingsMode.PerInstance, viewModel.SelectedInstance?.Instance.LaunchSettingsMode);
        Assert.Equal(MemorySettingsMode.Manual, viewModel.SelectedInstance?.Instance.MemorySettingsMode);
        Assert.Equal(5120, viewModel.SelectedInstance?.Instance.MemoryMb);
        Assert.False(viewModel.SelectedInstance?.Instance.CheckFilesBeforeLaunch);
        Assert.False(viewModel.SelectedInstance?.Instance.AutoRepairMissingFiles);
        Assert.True(viewModel.SelectedInstance?.Instance.MinimizeLauncherAfterLaunch);
        Assert.True(viewModel.SelectedInstance?.Instance.LaunchFullScreen);
        Assert.Equal("echo before", viewModel.SelectedInstance?.Instance.PreLaunchCommand);
        Assert.False(viewModel.SelectedInstance?.Instance.WaitForPreLaunchCommand);
        Assert.Equal("echo after", viewModel.SelectedInstance?.Instance.PostExitCommand);
        Assert.Equal("-Dfoo=bar", viewModel.SelectedInstance?.Instance.JvmArguments);
        Assert.Equal("--demo", viewModel.SelectedInstance?.Instance.GameArguments);
    }

    [Fact]
    public async Task PerInstanceLaunchCheckSynchronizesAutoRepair()
    {
        var instanceService = new FakeGameInstanceService();
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        instance.CheckFilesBeforeLaunch = true;
        instance.AutoRepairMissingFiles = true;
        instanceService.CreatedInstances.Add(instance);
        var viewModel = CreateViewModel(instanceService, new FakeGameVersionService([]), new FakeStatusService(), new FakeInstanceFolderService());

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        viewModel.Details.SelectedLaunchSettingsModeOption = viewModel.Details.LaunchSettingsModeOptions
            .Single(option => option.Mode == LaunchSettingsMode.PerInstance);

        viewModel.Details.LaunchCheckFilesBeforeLaunchEnabled = false;

        Assert.False(viewModel.Details.LaunchAutoRepairMissingFilesEnabled);
        Assert.False(viewModel.Details.CanEditAutoRepairMissingFiles);

        viewModel.Details.LaunchCheckFilesBeforeLaunchEnabled = true;

        Assert.True(viewModel.Details.LaunchAutoRepairMissingFilesEnabled);
        Assert.True(viewModel.Details.CanEditAutoRepairMissingFiles);
        await TestAsync.WaitForAsync(() =>
            instanceService.LastSavedInstance is not null
            && instanceService.LastSavedInstance.CheckFilesBeforeLaunch
            && instanceService.LastSavedInstance.AutoRepairMissingFiles);
    }

    [Fact]
    public async Task UseGlobalLaunchSettingsSynchronizesEditorValuesFromGlobalSettings()
    {
        var instanceService = new FakeGameInstanceService();
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        instance.LaunchSettingsMode = LaunchSettingsMode.PerInstance;
        instance.CheckFilesBeforeLaunch = true;
        instance.AutoRepairMissingFiles = true;
        instance.MinimizeLauncherAfterLaunch = false;
        instance.LaunchFullScreen = false;
        instanceService.CreatedInstances.Add(instance);
        var viewModel = CreateViewModel(instanceService, new FakeGameVersionService([]), new FakeStatusService(), new FakeInstanceFolderService());
        viewModel.PrimeFromSettings(new LauncherSettings
        {
            DefaultCheckFilesBeforeLaunch = false,
            DefaultAutoRepairMissingFiles = false,
            DefaultMinimizeLauncherAfterLaunch = true,
            DefaultLaunchFullScreen = true,
            DefaultPreLaunchCommand = "echo global-before",
            DefaultWaitForPreLaunchCommand = false,
            DefaultPostExitCommand = "echo global-after",
            DefaultJvmArguments = "-Dglobal=true",
            DefaultGameArguments = "--global",
            DefaultMemorySettingsMode = MemorySettingsMode.Auto,
            DefaultMemoryMb = 8192
        });

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());

        viewModel.Details.SelectedLaunchSettingsModeOption = viewModel.Details.LaunchSettingsModeOptions
            .Single(option => option.Mode == LaunchSettingsMode.UseGlobal);

        Assert.False(viewModel.Details.LaunchCheckFilesBeforeLaunchEnabled);
        Assert.False(viewModel.Details.LaunchAutoRepairMissingFilesEnabled);
        Assert.True(viewModel.Details.LaunchMinimizeLauncherAfterLaunchEnabled);
        Assert.True(viewModel.Details.LaunchFullScreenEnabled);
        Assert.Equal("echo global-before", viewModel.Details.LaunchPreLaunchCommand);
        Assert.False(viewModel.Details.LaunchWaitForPreLaunchCommand);
        Assert.Equal("echo global-after", viewModel.Details.LaunchPostExitCommand);
        Assert.Equal("-Dglobal=true", viewModel.Details.LaunchJvmArguments);
        Assert.Equal("--global", viewModel.Details.LaunchGameArguments);
        Assert.Equal(MemorySettingsMode.Auto, viewModel.Details.SelectedMemoryModeOption?.Mode);
        Assert.Equal(8192, viewModel.Details.MemoryMb);
        Assert.False(viewModel.Details.IsMemorySliderEnabled);
        Assert.False(viewModel.Details.IsMemorySliderVisible);
        Assert.True(viewModel.Details.IsAutomaticMemorySummaryVisible);
        Assert.False(viewModel.Details.AreLaunchSettingsOverridesEnabled);

        await TestAsync.WaitForAsync(() =>
            instanceService.LastSavedInstance is not null
            && instanceService.LastSavedInstance.LaunchSettingsMode == LaunchSettingsMode.UseGlobal
            && !instanceService.LastSavedInstance.CheckFilesBeforeLaunch
            && !instanceService.LastSavedInstance.AutoRepairMissingFiles
            && instanceService.LastSavedInstance.MinimizeLauncherAfterLaunch
            && instanceService.LastSavedInstance.LaunchFullScreen
            && instanceService.LastSavedInstance.PreLaunchCommand == "echo global-before"
            && !instanceService.LastSavedInstance.WaitForPreLaunchCommand
            && instanceService.LastSavedInstance.PostExitCommand == "echo global-after"
            && instanceService.LastSavedInstance.JvmArguments == "-Dglobal=true"
            && instanceService.LastSavedInstance.GameArguments == "--global"
            && instanceService.LastSavedInstance.MemorySettingsMode == MemorySettingsMode.Auto
            && instanceService.LastSavedInstance.MemoryMb == 8192);
    }

    [Fact]
    public async Task UseGlobalManualMemoryShowsReadOnlySlider()
    {
        var instanceService = new FakeGameInstanceService();
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        instance.LaunchSettingsMode = LaunchSettingsMode.UseGlobal;
        instanceService.CreatedInstances.Add(instance);
        var viewModel = CreateViewModel(instanceService, new FakeGameVersionService([]), new FakeStatusService(), new FakeInstanceFolderService());
        viewModel.PrimeFromSettings(new LauncherSettings
        {
            DefaultMemorySettingsMode = MemorySettingsMode.Manual,
            DefaultMemoryMb = 8192
        });

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());

        Assert.Equal(MemorySettingsMode.Manual, viewModel.Details.SelectedMemoryModeOption?.Mode);
        Assert.Equal(8192, viewModel.Details.MemoryMb);
        Assert.True(viewModel.Details.IsMemorySliderVisible);
        Assert.False(viewModel.Details.IsMemorySliderEnabled);
        Assert.False(viewModel.Details.IsAutomaticMemorySummaryVisible);
    }

    [Fact]
    public async Task InstanceJavaSettingsDefaultToUseGlobalAndAutomatic()
    {
        var viewModel = CreateViewModel([CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla)]);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());

        Assert.Equal(LaunchSettingsMode.UseGlobal, viewModel.Details.SelectedInstanceJavaSettingsModeOption?.Mode);
        Assert.False(viewModel.Details.AreInstanceJavaSettingsOverridesEnabled);
        Assert.Equal(Strings.Settings_JavaSelectionAuto, viewModel.Details.SelectedInstanceJavaSelectionOption?.Title);
        Assert.Null(viewModel.Details.SelectedInstanceJavaRuntime);
    }

    [Fact]
    public async Task UseGlobalInstanceJavaSettingsShowsGlobalJavaSelection()
    {
        var globalJavaPath = @"C:\Global\jdk-21\bin\java.exe";
        var instanceJavaPath = @"C:\Instance\jdk-17\bin\java.exe";
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        instance.JavaSettingsMode = LaunchSettingsMode.UseGlobal;
        instance.JavaSelectionMode = JavaSelectionMode.Manual;
        instance.SelectedJavaExecutablePath = instanceJavaPath;
        var javaRuntimeDiscoveryService = new FakeJavaRuntimeDiscoveryService
        {
            Runtimes =
            [
                CreateJavaRuntime(globalJavaPath, 21),
                CreateJavaRuntime(instanceJavaPath, 17)
            ]
        };
        var viewModel = CreateViewModel(
            [instance],
            javaRuntimeDiscoveryService: javaRuntimeDiscoveryService);
        viewModel.PrimeFromSettings(new LauncherSettings
        {
            JavaSelectionMode = JavaSelectionMode.Manual,
            SelectedJavaExecutablePath = globalJavaPath
        });

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        await viewModel.Details.InstanceJavaSettings.RefreshJavaRuntimesForDisplayAsync();

        Assert.False(viewModel.Details.AreInstanceJavaSettingsOverridesEnabled);
        Assert.False(viewModel.Details.CanInteractWithInstanceJavaRuntimeList);
        Assert.Equal(Strings.Settings_JavaSelectionManual, viewModel.Details.SelectedInstanceJavaSelectionOption?.Title);
        Assert.Equal(globalJavaPath, viewModel.Details.SelectedInstanceJavaRuntime?.ExecutablePath);
        Assert.Equal(instanceJavaPath, viewModel.SelectedInstance?.Instance.SelectedJavaExecutablePath);
    }

    [Fact]
    public async Task UseGlobalInstanceJavaSettingsUpdatesWhenGlobalJavaSettingsChange()
    {
        var globalJavaPath = @"C:\Global\jdk-21\bin\java.exe";
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        instance.JavaSettingsMode = LaunchSettingsMode.UseGlobal;
        var javaRuntimeDiscoveryService = new FakeJavaRuntimeDiscoveryService
        {
            Runtimes =
            [
                CreateJavaRuntime(globalJavaPath, 21)
            ]
        };
        var viewModel = CreateViewModel(
            [instance],
            javaRuntimeDiscoveryService: javaRuntimeDiscoveryService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());

        viewModel.PrimeFromSettings(new LauncherSettings
        {
            JavaSelectionMode = JavaSelectionMode.Manual,
            SelectedJavaExecutablePath = globalJavaPath
        });
        await viewModel.Details.InstanceJavaSettings.RefreshJavaRuntimesForDisplayAsync();

        Assert.Equal(globalJavaPath, viewModel.Details.SelectedInstanceJavaRuntime?.ExecutablePath);

        viewModel.PrimeFromSettings(new LauncherSettings
        {
            JavaSelectionMode = JavaSelectionMode.Auto,
            SelectedJavaExecutablePath = globalJavaPath
        });

        Assert.Equal(Strings.Settings_JavaSelectionAuto, viewModel.Details.SelectedInstanceJavaSelectionOption?.Title);
        Assert.Null(viewModel.Details.SelectedInstanceJavaRuntime);
    }

    [Fact]
    public async Task InstanceJavaAutomaticModeRefreshesWithoutSelectedRuntime()
    {
        var javaRuntimeDiscoveryService = new FakeJavaRuntimeDiscoveryService
        {
            Runtimes =
            [
                CreateJavaRuntime(@"C:\Java\jdk-21\bin\java.exe", 21)
            ]
        };
        var viewModel = CreateViewModel(
            [CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla)],
            javaRuntimeDiscoveryService: javaRuntimeDiscoveryService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        viewModel.Details.SelectedInstanceJavaSettingsModeOption = viewModel.Details.LaunchSettingsModeOptions
            .Single(option => option.Mode == LaunchSettingsMode.PerInstance);
        await viewModel.Details.RefreshInstanceJavaRuntimesCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Details.InstanceJavaRuntimes);
        Assert.Null(viewModel.Details.SelectedInstanceJavaRuntime);
    }

    [Fact]
    public async Task InstanceJavaManualModeSelectsFirstRuntimeAndSavesPath()
    {
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla));
        var firstRuntime = CreateJavaRuntime(@"C:\Java\jdk-21\bin\java.exe", 21);
        var javaRuntimeDiscoveryService = new FakeJavaRuntimeDiscoveryService
        {
            Runtimes =
            [
                firstRuntime,
                CreateJavaRuntime(@"C:\Java\jdk-17\bin\java.exe", 17)
            ]
        };
        var viewModel = CreateViewModel(
            instanceService,
            new FakeGameVersionService([]),
            new FakeStatusService(),
            new FakeInstanceFolderService(),
            javaRuntimeDiscoveryService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        viewModel.Details.SelectedInstanceJavaSettingsModeOption = viewModel.Details.LaunchSettingsModeOptions
            .Single(option => option.Mode == LaunchSettingsMode.PerInstance);
        await viewModel.Details.RefreshInstanceJavaRuntimesCommand.ExecuteAsync(null);

        viewModel.Details.SelectedInstanceJavaSelectionOption = viewModel.Details.InstanceJavaSelectionOptions
            .Single(option => option.Id == "manual");

        Assert.Equal(firstRuntime.ExecutablePath, viewModel.Details.SelectedInstanceJavaRuntime?.ExecutablePath);
        await TestAsync.WaitForAsync(() =>
            instanceService.LastSavedInstance is not null
            && instanceService.LastSavedInstance.JavaSettingsMode == LaunchSettingsMode.PerInstance
            && instanceService.LastSavedInstance.JavaSelectionMode == JavaSelectionMode.Manual
            && instanceService.LastSavedInstance.SelectedJavaExecutablePath == firstRuntime.ExecutablePath);
    }

    [Fact]
    public async Task InstanceJavaSwitchingBackToAutomaticClearsSelectionButKeepsSavedPath()
    {
        var instanceService = new FakeGameInstanceService();
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        instance.JavaSettingsMode = LaunchSettingsMode.PerInstance;
        instance.JavaSelectionMode = JavaSelectionMode.Manual;
        instance.SelectedJavaExecutablePath = @"C:\Java\jdk-17\bin\java.exe";
        instanceService.CreatedInstances.Add(instance);
        var javaRuntimeDiscoveryService = new FakeJavaRuntimeDiscoveryService
        {
            Runtimes =
            [
                CreateJavaRuntime(instance.SelectedJavaExecutablePath, 17)
            ]
        };
        var viewModel = CreateViewModel(
            instanceService,
            new FakeGameVersionService([]),
            new FakeStatusService(),
            new FakeInstanceFolderService(),
            javaRuntimeDiscoveryService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        await viewModel.Details.RefreshInstanceJavaRuntimesCommand.ExecuteAsync(null);
        Assert.NotNull(viewModel.Details.SelectedInstanceJavaRuntime);

        viewModel.Details.SelectedInstanceJavaSelectionOption = viewModel.Details.InstanceJavaSelectionOptions
            .Single(option => option.Id == "auto");

        Assert.Null(viewModel.Details.SelectedInstanceJavaRuntime);
        await TestAsync.WaitForAsync(() =>
            instanceService.LastSavedInstance is not null
            && instanceService.LastSavedInstance.JavaSelectionMode == JavaSelectionMode.Auto);
        Assert.Equal(@"C:\Java\jdk-17\bin\java.exe", viewModel.SelectedInstance?.Instance.SelectedJavaExecutablePath);
    }

    [Fact]
    public async Task InstanceJavaSettingsSaveKeepsCurrentJavaSection()
    {
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla));
        var viewModel = CreateViewModel(instanceService, new FakeGameVersionService([]), new FakeStatusService(), new FakeInstanceFolderService());

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        var javaSection = viewModel.DetailSections.Single(section => section.Id == "java");
        viewModel.SelectDetailsSectionCommand.Execute(javaSection);

        viewModel.Details.SelectedInstanceJavaSettingsModeOption = viewModel.Details.LaunchSettingsModeOptions
            .Single(option => option.Mode == LaunchSettingsMode.PerInstance);

        await TestAsync.WaitForAsync(() =>
            instanceService.LastSavedInstance is not null
            && instanceService.LastSavedInstance.JavaSettingsMode == LaunchSettingsMode.PerInstance);

        Assert.True(viewModel.Details.IsJavaSection);
        Assert.True(javaSection.IsSelected);
        Assert.Equal(Strings.GameSettings_DetailJava, viewModel.Details.SectionTitle);
    }

    [Fact]
    public async Task OpenInstanceDirectoryCommandUsesFolderServiceFromGeneralSection()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        Directory.CreateDirectory(instance.InstanceDirectory);
        var folderService = new FakeInstanceFolderService();
        var viewModel = CreateViewModel([instance], [], new FakeStatusService(), folderService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());

        viewModel.Details.OpenInstanceDirectoryCommand.Execute(null);

        Assert.Equal(instance.InstanceDirectory, folderService.LastOpenedPath);
    }

    [Fact]
    public async Task SelectDetailsSectionCommandUpdatesDetailsContentWithoutChangingHeaderTitle()
    {
        var viewModel = CreateViewModel([CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla)]);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        var targetSection = viewModel.DetailSections.Single(section => section.Id == "loader");

        viewModel.SelectDetailsSectionCommand.Execute(targetSection);

        Assert.Equal("Vanilla World", viewModel.PageTitle);
        Assert.Equal(Strings.GameSettings_DetailLoader, viewModel.Details.SectionTitle);
        Assert.Equal(
            string.Format(Strings.GameSettings_DetailPlaceholderBodyFormat, Strings.GameSettings_DetailLoader),
            viewModel.Details.SectionPlaceholderBody);
        Assert.True(targetSection.IsSelected);
        Assert.False(viewModel.DetailSections.First(section => section.Id == "general").IsSelected);
    }

    [Fact]
    public async Task ConfirmEditInstanceDialogRenamesSelectedInstanceAndRaisesSyncEvent()
    {
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla));
        var statusService = new FakeStatusService();
        var viewModel = CreateViewModel(instanceService, new FakeGameVersionService([]), statusService, new FakeInstanceFolderService());
        var syncRequested = 0;
        viewModel.InstancesChanged += () => syncRequested++;

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        viewModel.Details.RequestEditInstanceCommand.Execute(null);
        viewModel.EditDialog.InstanceName = "Renamed World";
        viewModel.EditDialog.SelectedIconOption = viewModel.EditDialog.IconOptions.Single(option => option.IconSource == "/Assets/Icons/block/diamond_block.png");

        await viewModel.ConfirmEditInstanceDialogCommand.ExecuteAsync(null);

        Assert.Equal("Renamed World", viewModel.SelectedInstance?.Name);
        Assert.Equal("Renamed World", viewModel.SelectedInstance?.VersionName);
        Assert.Equal("/Assets/Icons/block/diamond_block.png", viewModel.SelectedInstance?.Instance.IconSource);
        Assert.True(viewModel.IsDetailsStep);
        Assert.Equal("Renamed World", viewModel.PageTitle);
        Assert.Equal("Renamed World", viewModel.Details.InstanceName);
        Assert.Equal(1, syncRequested);
        Assert.Equal(viewModel.SelectedInstance?.Instance.Id, instanceService.LastRenamedInstanceId);
        Assert.Equal("Renamed World", instanceService.LastRenamedName);
        Assert.Equal("/Assets/Icons/block/diamond_block.png", instanceService.LastRenamedIconSource);
        Assert.Equal(string.Format(Strings.Status_InstanceRenamedFormat, "Renamed World"), statusService.LastMessage);
        Assert.False(viewModel.EditDialog.IsEditInstanceDialogOpen);
        Assert.True(viewModel.EditDialog.IsEditInstanceSuccessful);
    }

    private static GameSettingsPageViewModel CreateViewModel(
        IReadOnlyList<GameInstance> instances,
        IReadOnlyList<MinecraftVersionInfo>? versions = null,
        FakeStatusService? statusService = null,
        FakeInstanceFolderService? folderService = null,
        FakeJavaRuntimeDiscoveryService? javaRuntimeDiscoveryService = null,
        FakeFilePickerService? filePickerService = null,
        FakeFloatingMessageService? floatingMessageService = null,
        IModService? modService = null)
    {
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.AddRange(instances);
        return CreateViewModel(
            instanceService,
            new FakeGameVersionService(versions ?? []),
            statusService ?? new FakeStatusService(),
            folderService ?? new FakeInstanceFolderService(),
            javaRuntimeDiscoveryService ?? new FakeJavaRuntimeDiscoveryService(),
            filePickerService ?? new FakeFilePickerService(),
            floatingMessageService ?? new FakeFloatingMessageService(),
            modService ?? new FakeModService());
    }

    private static GameSettingsPageViewModel CreateViewModel(
        IGameInstanceService instanceService,
        IGameVersionService gameVersionService,
        FakeStatusService statusService,
        FakeInstanceFolderService folderService,
        FakeJavaRuntimeDiscoveryService? javaRuntimeDiscoveryService = null,
        FakeFilePickerService? filePickerService = null,
        FakeFloatingMessageService? floatingMessageService = null,
        IModService? modService = null)
    {
        var resolvedModService = modService ?? new FakeModService();
        return new GameSettingsPageViewModel(
            instanceService,
            gameVersionService,
            statusService,
            folderService,
            new FakeSystemMemoryService(),
            resolvedModService,
            new LocalModsViewModel(resolvedModService, statusService),
            javaRuntimeDiscoveryService ?? new FakeJavaRuntimeDiscoveryService(),
            filePickerService ?? new FakeFilePickerService(),
            floatingMessageService ?? new FakeFloatingMessageService());
    }

    private static void CreateModJar(string path, string fabricMetadataJson)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var archive = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create);
        var entry = archive.CreateEntry("fabric.mod.json");
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(fabricMetadataJson);
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
            Description = string.Empty,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            InstanceDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"))
        };
    }

    private static JavaRuntimeInfo CreateJavaRuntime(string executablePath, int majorVersion)
    {
        var installationDirectory = Path.GetDirectoryName(Path.GetDirectoryName(executablePath)) ?? string.Empty;
        return new JavaRuntimeInfo(
            $"Java {majorVersion}",
            $"{majorVersion}.0.0",
            majorVersion,
            "x64",
            executablePath,
            installationDirectory,
            "Test");
    }

    private static LocalMod CreateLocalMod(
        string fileName,
        bool isEnabled,
        string instanceDirectory,
        string? iconSource = null,
        string? displayName = null,
        string? loader = "fabric",
        string? modId = null,
        string? version = "1.2.3")
    {
        var normalizedFileName = isEnabled || fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : fileName + ".disabled";
        var directory = Path.Combine(instanceDirectory, "mods");
        var fullPath = Path.Combine(directory, normalizedFileName);
        return new LocalMod
        {
            Name = displayName ?? Path.GetFileNameWithoutExtension(fileName),
            Loader = loader,
            ModId = modId ?? Path.GetFileNameWithoutExtension(fileName).Split('-')[0],
            Version = version,
            FileName = normalizedFileName,
            FullPath = fullPath,
            IconSource = iconSource,
            IsEnabled = isEnabled,
            SizeBytes = 1024,
            Source = "Local"
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
        public string? LastRevealedFilePath { get; private set; }

        public bool DirectoryExists(string folderPath)
        {
            return !string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath);
        }

        public string EnsureDirectoryExists(string folderPath)
        {
            var normalizedFolderPath = Path.GetFullPath(folderPath);
            Directory.CreateDirectory(normalizedFolderPath);
            return normalizedFolderPath;
        }

        public bool TryOpen(string folderPath)
        {
            LastOpenedPath = folderPath;
            return true;
        }

        public bool TryRevealFile(string filePath)
        {
            LastRevealedFilePath = filePath;
            return true;
        }
    }

    private sealed class FakeJavaRuntimeDiscoveryService : IJavaRuntimeDiscoveryService
    {
        public IReadOnlyList<JavaRuntimeInfo> Runtimes { get; init; } = [];

        public JavaRuntimeInfo ImportedRuntime { get; init; } = new(
            "Java",
            null,
            null,
            "unknown",
            @"C:\Java\bin\java.exe",
            @"C:\Java",
            "ManualImport");

        public string? LastMinecraftDirectory { get; private set; }

        public string? LastImportedExecutablePath { get; private set; }

        public Task<IReadOnlyList<JavaRuntimeInfo>> DiscoverAsync(
            string? minecraftDirectory,
            CancellationToken cancellationToken = default)
        {
            LastMinecraftDirectory = minecraftDirectory;
            return Task.FromResult(Runtimes);
        }

        public Task<JavaRuntimeInfo> DiscoverExecutableAsync(
            string executablePath,
            CancellationToken cancellationToken = default)
        {
            LastImportedExecutablePath = executablePath;
            return Task.FromResult(ImportedRuntime);
        }
    }

    private sealed class FakeFilePickerService : IFilePickerService
    {
        public string? JavaExecutablePath { get; init; }
        public string? ModFilePath { get; init; }
        public string? FolderPath { get; init; }

        public string? PickMinecraftSkin()
        {
            return null;
        }

        public string? PickJavaExecutable()
        {
            return JavaExecutablePath;
        }

        public string? PickModFile()
        {
            return ModFilePath;
        }

        public string? PickFolder(string title, string? initialDirectory = null)
        {
            return FolderPath;
        }
    }

    private sealed class FakeFloatingMessageService : IFloatingMessageService
    {
        public event Action<string>? MessageRequested;

        public string? LastMessage { get; private set; }

        public void Show(string message)
        {
            LastMessage = message;
            MessageRequested?.Invoke(message);
        }
    }

    private sealed class FakeSystemMemoryService : ISystemMemoryService
    {
        public SystemMemorySnapshot GetSnapshot()
        {
            return new SystemMemorySnapshot(
                TotalMemoryBytes: 16L * 1024L * 1024L * 1024L,
                AvailableMemoryBytes: 8L * 1024L * 1024L * 1024L);
        }
    }

    private sealed class FakeModService : IModService
    {
        public Dictionary<string, List<LocalMod>> ModsByInstanceId { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Exception? GetModsException { get; init; }

        public Task<IReadOnlyList<LocalMod>> GetModsAsync(GameInstance instance, CancellationToken cancellationToken = default)
        {
            if (GetModsException is not null)
                throw GetModsException;

            return Task.FromResult(
                ModsByInstanceId.TryGetValue(instance.Id, out var mods)
                    ? (IReadOnlyList<LocalMod>)mods.Select(CloneLocalMod).ToArray()
                    : (IReadOnlyList<LocalMod>)[]);
        }

        public Task<LocalMod> ImportAsync(
            GameInstance instance,
            string sourceJarPath,
            bool overwriteExisting = false,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(sourceJarPath))
                throw new ModFileImportNotFoundException(sourceJarPath);

            if (!ModsByInstanceId.TryGetValue(instance.Id, out var mods))
            {
                mods = [];
                ModsByInstanceId[instance.Id] = mods;
            }

            var fileName = Path.GetFileName(sourceJarPath);
            var imported = new LocalMod
            {
                Name = Path.GetFileNameWithoutExtension(fileName),
                Loader = "fabric",
                ModId = Path.GetFileNameWithoutExtension(fileName),
                Version = "1.0.0",
                FileName = fileName,
                FullPath = Path.Combine(instance.InstanceDirectory, "mods", fileName),
                IsEnabled = true,
                SizeBytes = new FileInfo(sourceJarPath).Length,
                Source = "Local"
            };
            var existingIndex = mods.FindIndex(mod =>
                string.Equals(mod.FileName, fileName, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                if (overwriteExisting)
                    mods[existingIndex] = imported;
            }
            else
            {
                mods.Add(imported);
            }

            return Task.FromResult(CloneLocalMod(imported));
        }

        public Task SetEnabledAsync(LocalMod mod, bool enabled, CancellationToken cancellationToken = default)
        {
            foreach (var pair in ModsByInstanceId)
            {
                var index = pair.Value.FindIndex(candidate =>
                    string.Equals(candidate.FullPath, mod.FullPath, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                    continue;

                var updated = CloneLocalMod(pair.Value[index]);
                updated.IsEnabled = enabled;
                updated.FullPath = enabled
                    ? updated.FullPath[..^".disabled".Length]
                    : updated.FullPath + ".disabled";
                updated.FileName = Path.GetFileName(updated.FullPath);
                pair.Value[index] = updated;
                return Task.CompletedTask;
            }

            throw new InvalidOperationException($"Mod not found: {mod.FullPath}");
        }

        public Task DeleteAsync(LocalMod mod, CancellationToken cancellationToken = default)
        {
            foreach (var pair in ModsByInstanceId)
            {
                pair.Value.RemoveAll(candidate =>
                    string.Equals(candidate.FullPath, mod.FullPath, StringComparison.OrdinalIgnoreCase));
            }

            return Task.CompletedTask;
        }

        private static LocalMod CloneLocalMod(LocalMod mod)
        {
            return new LocalMod
            {
                Name = mod.Name,
                Loader = mod.Loader,
                ModId = mod.ModId,
                Version = mod.Version,
                FileName = mod.FileName,
                FullPath = mod.FullPath,
                IconSource = mod.IconSource,
                IsEnabled = mod.IsEnabled,
                SizeBytes = mod.SizeBytes,
                Source = mod.Source
            };
        }
    }

    private sealed class ThrowingGameVersionService : IGameVersionService
    {
        public Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            CancellationToken cancellationToken = default,
            int downloadSpeedLimitMbPerSecond = 0)
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
            CancellationToken cancellationToken = default,
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            throw exception;
        }

        public Task SaveInstanceAsync(GameInstance instance, CancellationToken cancellationToken = default)
        {
            throw exception;
        }

        public Task<GameInstance> RenameInstanceAsync(
            string instanceId,
            string? newName,
            string? newIconSource,
            CancellationToken cancellationToken = default)
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
