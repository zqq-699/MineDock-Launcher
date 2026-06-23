using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.GameSettings;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure;
using Launcher.Infrastructure.FileSystem;
using Launcher.Infrastructure.Persistence;
using Launcher.Tests.Helpers;
using System.Formats.Tar;
using System.IO.Compression;

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
            CreateInstance("Forge Pack", "1.20.1", LoaderKind.Forge),
            CreateInstance("NeoForge Pack", "1.20.4", LoaderKind.NeoForge)
        ]);

        await viewModel.EnsureInstancesLoadedAsync();

        Assert.Equal("/Assets/Icons/block/fabric.png", viewModel.VisibleInstances[0].IconSource);
        Assert.Equal("/Assets/Icons/block/Anvil.png", viewModel.VisibleInstances[1].IconSource);
        Assert.Equal("/Assets/Icons/block/neo_logo.png", viewModel.VisibleInstances[2].IconSource);
    }

    [Fact]
    public async Task GameSettingsPageUsesFixedInstanceSubtitleFormat()
    {
        var vanilla = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        var fabric = CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric);
        fabric.LoaderVersion = "0.16.10";
        var forge = CreateInstance("Forge Pack", "26.1.1", LoaderKind.Forge);
        forge.LoaderVersion = "63.0.2";
        var neoForge = CreateInstance("NeoForge Pack", "1.20.4", LoaderKind.NeoForge);
        neoForge.LoaderVersion = "20.4.237";
        var viewModel = CreateViewModel([vanilla, fabric, forge, neoForge]);

        await viewModel.EnsureInstancesLoadedAsync();

        Assert.Equal("1.21.4", viewModel.VisibleInstances.Single(instance => instance.Name == "Vanilla World").Subtitle);
        Assert.Equal("1.20.1 Fabric 0.16.10", viewModel.VisibleInstances.Single(instance => instance.Name == "Fabric Pack").Subtitle);
        Assert.Equal("26.1.1 Forge 63.0.2", viewModel.VisibleInstances.Single(instance => instance.Name == "Forge Pack").Subtitle);
        Assert.Equal("1.20.4 NeoForge 20.4.237", viewModel.VisibleInstances.Single(instance => instance.Name == "NeoForge Pack").Subtitle);
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
        Assert.Equal(10, viewModel.DetailSections.Count);
        Assert.Equal(
            [
                Strings.GameSettings_DetailGeneral,
                Strings.GameSettings_DetailLaunch,
                Strings.GameSettings_DetailJava,
                Strings.GameSettings_DetailModManagement,
                Strings.GameSettings_DetailSaves,
                Strings.GameSettings_DetailResourcePacks,
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
    public async Task SaveManagementDetailsSectionUsesDedicatedViewModel()
    {
        var viewModel = CreateViewModel([CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla)]);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        var section = viewModel.DetailSections.Single(item => item.Id == "saves");

        viewModel.SelectDetailsSectionCommand.Execute(section);

        Assert.True(section.IsSelected);
        Assert.IsType<InstanceSaveManagementSettingsViewModel>(viewModel.Details.CurrentSectionViewModel);
        Assert.Same(viewModel.Details.SaveManagement, viewModel.Details.CurrentSectionViewModel);
        Assert.Equal(Strings.GameSettings_DetailSaves, viewModel.Details.SectionTitle);
    }

    [Fact]
    public async Task SaveManagementViewModelLoadsRealSavesForSelectedInstance()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        var saveService = new FakeSaveService();
        var firstCreatedAt = new DateTimeOffset(2026, 1, 3, 10, 20, 30, TimeSpan.Zero);
        var secondCreatedAt = new DateTimeOffset(2026, 1, 2, 8, 9, 10, TimeSpan.Zero);
        saveService.SavesByInstanceId[instance.Id] =
        [
            CreateLocalSave("Cherry Grove", instance.InstanceDirectory, iconSource: @"C:\temp\cherry.png", createdAt: firstCreatedAt),
            CreateLocalSave("Starter Base", instance.InstanceDirectory, createdAt: secondCreatedAt)
        ];
        var viewModel = CreateViewModel([instance], saveService: saveService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        viewModel.SelectDetailsSectionCommand.Execute(viewModel.DetailSections.Single(section => section.Id == "saves"));
        await TestAsync.WaitForAsync(() => viewModel.Details.SaveManagement.Saves.Count == 2);

        var saveManagement = viewModel.Details.SaveManagement;
        Assert.Equal(2, saveManagement.InstalledSaveCount);
        Assert.Equal(
            string.Format(Strings.GameSettings_SaveManagementInstalledSummaryFormat, 2),
            saveManagement.InstalledSummaryText);
        Assert.Equal(["Cherry Grove", "Starter Base"], saveManagement.Saves.Select(save => save.Title));
        Assert.Equal(
            [firstCreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), secondCreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")],
            saveManagement.Saves.Select(save => save.TrailingText));
        Assert.Equal(string.Empty, saveManagement.Saves[0].IconKey);
        Assert.Equal("instance_setting_page/saves", saveManagement.Saves[1].IconKey);
        Assert.True(saveManagement.ImportLocalSaveCommand.CanExecute(null));
        Assert.True(saveManagement.HasSaves);
        Assert.False(saveManagement.CanShowSaveEmptyState);
        Assert.Same(saveManagement.Saves[0], saveManagement.SelectedSave);
        Assert.All(saveManagement.Saves, save => Assert.False(save.IsSelected));
    }

    [Fact]
    public async Task SaveManagementSearchAndSelectAllOnlyTargetVisibleSaves()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        var saveService = new FakeSaveService();
        saveService.SavesByInstanceId[instance.Id] =
        [
            CreateLocalSave("Alpha Base", instance.InstanceDirectory),
            CreateLocalSave("Beta Base", instance.InstanceDirectory),
            CreateLocalSave("Creative Flat", instance.InstanceDirectory)
        ];
        var viewModel = CreateViewModel([instance], saveService: saveService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        viewModel.SelectDetailsSectionCommand.Execute(viewModel.DetailSections.Single(section => section.Id == "saves"));
        await TestAsync.WaitForAsync(() => viewModel.Details.SaveManagement.Saves.Count == 3);

        var saveManagement = viewModel.Details.SaveManagement;
        saveManagement.SaveSearchQuery = "Base";
        saveManagement.ToggleMultiSelectModeCommand.Execute(null);
        saveManagement.SelectAllSavesCommand.Execute(null);

        Assert.Equal(2, saveManagement.Saves.Count);
        Assert.Equal(2, saveManagement.SelectedSaveCount);
        Assert.True(saveManagement.AreAllVisibleSavesSelected);
        Assert.All(saveManagement.Saves, save => Assert.True(save.IsSelected));
    }

    [Fact]
    public async Task SaveManagementSearchReusesVisibleItemViewModels()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        var saveService = new FakeSaveService();
        saveService.SavesByInstanceId[instance.Id] =
        [
            CreateLocalSave("Alpha Base", instance.InstanceDirectory),
            CreateLocalSave("Beta Base", instance.InstanceDirectory)
        ];
        var viewModel = CreateViewModel([instance], saveService: saveService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        var saveManagement = OpenSaveManagementSection(viewModel);
        await TestAsync.WaitForAsync(() => saveManagement.Saves.Count == 2);

        var alphaItem = saveManagement.Saves.Single(save => save.Title == "Alpha Base");

        saveManagement.SaveSearchQuery = "Alpha";

        Assert.Single(saveManagement.Saves);
        Assert.Same(alphaItem, saveManagement.Saves.Single());
        Assert.Same(alphaItem, saveManagement.SelectedSave);

        saveManagement.SaveSearchQuery = string.Empty;

        Assert.Equal(2, saveManagement.Saves.Count);
        Assert.Same(alphaItem, saveManagement.Saves.Single(save => save.Title == "Alpha Base"));
    }

    [Fact]
    public async Task SaveManagementDeleteUsesConfirmationDialogBeforeRemovingSaves()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        var saveService = new FakeSaveService();
        saveService.SavesByInstanceId[instance.Id] =
        [
            CreateLocalSave("Alpha Base", instance.InstanceDirectory),
            CreateLocalSave("Beta Base", instance.InstanceDirectory)
        ];
        var viewModel = CreateViewModel([instance], saveService: saveService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        viewModel.SelectDetailsSectionCommand.Execute(viewModel.DetailSections.Single(section => section.Id == "saves"));
        await TestAsync.WaitForAsync(() => viewModel.Details.SaveManagement.Saves.Count == 2);

        var saveManagement = viewModel.Details.SaveManagement;
        saveManagement.ToggleMultiSelectModeCommand.Execute(null);
        saveManagement.SelectAllSavesCommand.Execute(null);
        saveManagement.RequestDeleteSelectedSavesCommand.Execute(null);

        Assert.True(viewModel.IsDeleteModsDialogOpen);
        Assert.Equal(Strings.Dialog_DeleteSavesTitle, viewModel.DeleteModsDialogTitle);
        Assert.Equal(
            string.Format(Strings.Dialog_DeleteMultipleSavesMessageFormat, 2),
            viewModel.DeleteModsDialogMessage);

        viewModel.CancelDeleteModsDialogCommand.Execute(null);

        Assert.False(viewModel.IsDeleteModsDialogOpen);
        Assert.Equal(2, saveService.SavesByInstanceId[instance.Id].Count);

        saveManagement.RequestDeleteSelectedSavesCommand.Execute(null);
        await viewModel.ConfirmDeleteModsDialogCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsDeleteModsDialogOpen);
        Assert.Empty(saveService.SavesByInstanceId[instance.Id]);
        Assert.Empty(saveManagement.Saves);
        Assert.False(saveManagement.IsMultiSelectMode);
    }

    [Fact]
    public async Task SaveManagementSingleDeleteUsesConfirmationDialogBeforeRemovingSave()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        var saveService = new FakeSaveService();
        saveService.SavesByInstanceId[instance.Id] =
        [
            CreateLocalSave("Alpha Base", instance.InstanceDirectory),
            CreateLocalSave("Beta Base", instance.InstanceDirectory)
        ];
        var viewModel = CreateViewModel([instance], saveService: saveService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        viewModel.SelectDetailsSectionCommand.Execute(viewModel.DetailSections.Single(section => section.Id == "saves"));
        await TestAsync.WaitForAsync(() => viewModel.Details.SaveManagement.Saves.Count == 2);

        var saveManagement = viewModel.Details.SaveManagement;
        var targetSave = saveManagement.Saves.Single(save => save.Title == "Alpha Base");

        saveManagement.RequestDeleteSaveCommand.Execute(targetSave);

        Assert.True(viewModel.IsDeleteModsDialogOpen);
        Assert.Equal(
            string.Format(Strings.Dialog_DeleteSingleSaveMessageFormat, "Alpha Base"),
            viewModel.DeleteModsDialogMessage);

        await viewModel.ConfirmDeleteModsDialogCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsDeleteModsDialogOpen);
        Assert.Single(saveService.SavesByInstanceId[instance.Id]);
        Assert.DoesNotContain(saveService.SavesByInstanceId[instance.Id], save => save.Name == "Alpha Base");
        Assert.Single(saveManagement.Saves);
    }

    [Fact]
    public async Task SaveManagementImportLocalSaveCommandDoesNothingWhenPickerIsCanceled()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        var saveService = new FakeSaveService();
        var filePickerService = new FakeFilePickerService();
        var viewModel = CreateViewModel([instance], filePickerService: filePickerService, saveService: saveService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        viewModel.SelectDetailsSectionCommand.Execute(viewModel.DetailSections.Single(section => section.Id == "saves"));

        await viewModel.Details.SaveManagement.ImportLocalSaveCommand.ExecuteAsync(null);

        Assert.Equal(0, saveService.ImportArchiveCallCount);
        Assert.False(viewModel.IsInvalidSaveImportDialogOpen);
    }

    [Fact]
    public async Task SaveManagementImportLocalSaveCommandImportsSelectedArchive()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        var statusService = new FakeStatusService();
        var saveService = new FakeSaveService();
        var archivePath = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "Cherry Grove.tar.gz");
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        await File.WriteAllTextAsync(archivePath, "fake archive");
        var filePickerService = new FakeFilePickerService
        {
            SaveArchivePath = archivePath
        };
        var viewModel = CreateViewModel([instance], statusService: statusService, filePickerService: filePickerService, saveService: saveService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        viewModel.SelectDetailsSectionCommand.Execute(viewModel.DetailSections.Single(section => section.Id == "saves"));

        await viewModel.Details.SaveManagement.ImportLocalSaveCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Details.SaveManagement.Saves);
        Assert.Equal("Cherry Grove", viewModel.Details.SaveManagement.Saves[0].Title);
        Assert.Equal(1, viewModel.Details.SaveManagement.InstalledSaveCount);
        Assert.Equal(Strings.Status_LocalSaveImported, statusService.LastMessage);
        Assert.False(viewModel.IsInvalidSaveImportDialogOpen);
    }

    [Fact]
    public async Task SaveManagementImportLocalSaveCommandShowsDialogForInvalidArchive()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        var saveService = new FakeSaveService
        {
            NextImportResult = LocalSaveImportResult.Failure(LocalSaveImportFailureReason.InvalidMinecraftSaveArchive)
        };
        var archivePath = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "bad-save.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        await File.WriteAllTextAsync(archivePath, "bad archive");
        var filePickerService = new FakeFilePickerService
        {
            SaveArchivePath = archivePath
        };
        var viewModel = CreateViewModel([instance], filePickerService: filePickerService, saveService: saveService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        viewModel.SelectDetailsSectionCommand.Execute(viewModel.DetailSections.Single(section => section.Id == "saves"));

        await viewModel.Details.SaveManagement.ImportLocalSaveCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsInvalidSaveImportDialogOpen);
        Assert.Equal(Strings.Dialog_InvalidSaveArchiveMessage, viewModel.InvalidSaveImportDialogMessage);

        viewModel.CloseInvalidSaveImportDialogCommand.Execute(null);

        Assert.False(viewModel.IsInvalidSaveImportDialogOpen);
        Assert.Equal(string.Empty, viewModel.InvalidSaveImportDialogMessage);
    }

    [Fact]
    public async Task SaveManagementImportLocalSaveCommandShowsDialogForUnsupportedArchive()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        var saveService = new FakeSaveService
        {
            NextImportResult = LocalSaveImportResult.Failure(LocalSaveImportFailureReason.UnsupportedArchive)
        };
        var archivePath = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "save.xyz");
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        await File.WriteAllTextAsync(archivePath, "bad archive");
        var filePickerService = new FakeFilePickerService
        {
            SaveArchivePath = archivePath
        };
        var viewModel = CreateViewModel([instance], filePickerService: filePickerService, saveService: saveService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        viewModel.SelectDetailsSectionCommand.Execute(viewModel.DetailSections.Single(section => section.Id == "saves"));

        await viewModel.Details.SaveManagement.ImportLocalSaveCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsInvalidSaveImportDialogOpen);
        Assert.Equal(Strings.Dialog_UnsupportedSaveArchiveMessage, viewModel.InvalidSaveImportDialogMessage);
    }

    [Fact]
    public async Task SaveManagementImportLocalSaveCommandReportsStatusForUnexpectedFailureWithoutDialog()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        var statusService = new FakeStatusService();
        var saveService = new FakeSaveService
        {
            NextImportResult = LocalSaveImportResult.Failure(LocalSaveImportFailureReason.UnexpectedError)
        };
        var archivePath = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "save.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        await File.WriteAllTextAsync(archivePath, "broken archive");
        var filePickerService = new FakeFilePickerService
        {
            SaveArchivePath = archivePath
        };
        var viewModel = CreateViewModel([instance], statusService: statusService, filePickerService: filePickerService, saveService: saveService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        viewModel.SelectDetailsSectionCommand.Execute(viewModel.DetailSections.Single(section => section.Id == "saves"));

        await viewModel.Details.SaveManagement.ImportLocalSaveCommand.ExecuteAsync(null);

        Assert.Equal(Strings.Status_LocalSaveImportFailed, statusService.LastMessage);
        Assert.False(viewModel.IsInvalidSaveImportDialogOpen);
    }

    [Fact]
    public async Task GameSettingsDragDropStateIsHiddenOutsideDetailsSections()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        var archivePath = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "save.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        await File.WriteAllTextAsync(archivePath, "fake archive");
        var floatingMessageService = new FakeFloatingMessageService();
        var viewModel = CreateViewModel([instance], floatingMessageService: floatingMessageService);

        await viewModel.EnsureInstancesLoadedAsync();

        var accepted = viewModel.UpdateImportDropState([archivePath]);

        Assert.False(accepted);
        Assert.Null(floatingMessageService.LastMessage);
    }

    [Fact]
    public async Task SaveManagementDragDropImportsSupportedArchives()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        var statusService = new FakeStatusService();
        var floatingMessageService = new FakeFloatingMessageService();
        var saveService = new FakeSaveService();
        var firstArchive = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "Alpha.zip");
        var secondArchive = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "Beta.tar.gz");
        Directory.CreateDirectory(Path.GetDirectoryName(firstArchive)!);
        Directory.CreateDirectory(Path.GetDirectoryName(secondArchive)!);
        await File.WriteAllTextAsync(firstArchive, "alpha");
        await File.WriteAllTextAsync(secondArchive, "beta");
        var viewModel = CreateViewModel([instance], statusService: statusService, saveService: saveService, floatingMessageService: floatingMessageService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        viewModel.SelectDetailsSectionCommand.Execute(viewModel.DetailSections.Single(section => section.Id == "saves"));

        var accepted = viewModel.UpdateImportDropState([firstArchive, secondArchive]);
        Assert.Equal(Strings.GameSettings_DropReleaseToImportMessage, floatingMessageService.LastMessage);
        await viewModel.HandleImportDropAsync([firstArchive, secondArchive]);

        Assert.True(accepted);
        Assert.Equal(2, saveService.ImportArchiveCallCount);
        Assert.Equal(string.Format(Strings.Status_LocalSavesImportedFormat, 2), statusService.LastMessage);
        Assert.Equal(2, viewModel.Details.SaveManagement.Saves.Count);
        Assert.Equal(string.Empty, floatingMessageService.LastMessage);
    }

    [Fact]
    public async Task SaveManagementDragDropRejectsFolders()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        var statusService = new FakeStatusService();
        var floatingMessageService = new FakeFloatingMessageService();
        var folderPath = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folderPath);
        var viewModel = CreateViewModel([instance], statusService: statusService, floatingMessageService: floatingMessageService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        viewModel.SelectDetailsSectionCommand.Execute(viewModel.DetailSections.Single(section => section.Id == "saves"));

        var accepted = viewModel.UpdateImportDropState([folderPath]);
        Assert.Equal(Strings.GameSettings_DropUnsupportedFileMessage, floatingMessageService.LastMessage);
        await viewModel.HandleImportDropAsync([folderPath]);

        Assert.False(accepted);
        Assert.Null(statusService.LastMessage);
        Assert.Equal(string.Empty, floatingMessageService.LastMessage);
    }

    [Fact]
    public async Task SaveManagementImportLocalSaveCommandRejectsUnsupportedExtensionBeforeService()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        var saveService = new FakeSaveService();
        var archivePath = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "save.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        await File.WriteAllTextAsync(archivePath, "not an archive");
        var filePickerService = new FakeFilePickerService
        {
            SaveArchivePath = archivePath
        };
        var viewModel = CreateViewModel([instance], filePickerService: filePickerService, saveService: saveService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        viewModel.SelectDetailsSectionCommand.Execute(viewModel.DetailSections.Single(section => section.Id == "saves"));

        await viewModel.Details.SaveManagement.ImportLocalSaveCommand.ExecuteAsync(null);

        Assert.Equal(0, saveService.ImportArchiveCallCount);
        Assert.True(viewModel.IsInvalidSaveImportDialogOpen);
        Assert.Equal(Strings.Dialog_UnsupportedSaveArchiveMessage, viewModel.InvalidSaveImportDialogMessage);
    }

    [Fact]
    public async Task ResourcePackManagementDetailsSectionUsesDedicatedViewModel()
    {
        var viewModel = CreateViewModel([CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla)]);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        var section = viewModel.DetailSections.Single(item => item.Id == "resource_packs");

        viewModel.SelectDetailsSectionCommand.Execute(section);

        Assert.True(section.IsSelected);
        Assert.IsType<InstanceResourcePackManagementSettingsViewModel>(viewModel.Details.CurrentSectionViewModel);
        Assert.Same(viewModel.Details.ResourcePackManagement, viewModel.Details.CurrentSectionViewModel);
        Assert.Equal(Strings.GameSettings_DetailResourcePacks, viewModel.Details.SectionTitle);
    }

    [Fact]
    public async Task ResourcePackManagementViewModelLoadsRealResourcePacksForSelectedInstance()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        var resourcePackService = new FakeResourcePackService();
        var firstCreatedAt = new DateTimeOffset(2026, 1, 3, 10, 20, 30, TimeSpan.Zero);
        var secondCreatedAt = new DateTimeOffset(2026, 1, 2, 8, 9, 10, TimeSpan.Zero);
        resourcePackService.ResourcePacksByInstanceId[instance.Id] =
        [
            CreateLocalResourcePack("Fresh Animations.zip", instance.InstanceDirectory, iconSource: @"C:\temp\pack.png", createdAt: firstCreatedAt),
            CreateLocalResourcePack("Bare Bones.zip", instance.InstanceDirectory, createdAt: secondCreatedAt)
        ];
        var viewModel = CreateViewModel([instance], resourcePackService: resourcePackService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        viewModel.SelectDetailsSectionCommand.Execute(viewModel.DetailSections.Single(section => section.Id == "resource_packs"));
        await TestAsync.WaitForAsync(() => viewModel.Details.ResourcePackManagement.ResourcePacks.Count == 2);

        var resourcePackManagement = viewModel.Details.ResourcePackManagement;
        Assert.Equal(2, resourcePackManagement.InstalledResourcePackCount);
        Assert.Equal(
            string.Format(Strings.GameSettings_ResourcePackManagementInstalledSummaryFormat, 2),
            resourcePackManagement.InstalledSummaryText);
        Assert.Equal(["Fresh Animations", "Bare Bones"], resourcePackManagement.ResourcePacks.Select(resourcePack => resourcePack.Title));
        Assert.Equal(string.Empty, resourcePackManagement.ResourcePacks[0].IconKey);
        Assert.Equal("main_menu_library", resourcePackManagement.ResourcePacks[1].IconKey);
        Assert.True(resourcePackManagement.ImportLocalResourcePackCommand.CanExecute(null));
        Assert.True(resourcePackManagement.HasResourcePacks);
        Assert.False(resourcePackManagement.CanShowResourcePackEmptyState);
        Assert.Same(resourcePackManagement.ResourcePacks[0], resourcePackManagement.SelectedResourcePack);
    }

    [Fact]
    public async Task ResourcePackManagementSectionReentryReusesVisibleItemViewModelsWithoutReloading()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        var resourcePackService = new FakeResourcePackService();
        resourcePackService.ResourcePacksByInstanceId[instance.Id] =
        [
            CreateLocalResourcePack("Fresh Animations.zip", instance.InstanceDirectory),
            CreateLocalResourcePack("Bare Bones.zip", instance.InstanceDirectory)
        ];
        var viewModel = CreateViewModel([instance], resourcePackService: resourcePackService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        var resourcePackManagement = OpenResourcePackManagementSection(viewModel);
        await TestAsync.WaitForAsync(() => resourcePackManagement.ResourcePacks.Count == 2);

        var initialCallCount = resourcePackService.GetResourcePacksCallCount;
        var firstItem = resourcePackManagement.ResourcePacks[0];
        var secondItem = resourcePackManagement.ResourcePacks[1];

        OpenShaderPackManagementSection(viewModel);
        OpenResourcePackManagementSection(viewModel);

        Assert.Equal(initialCallCount, resourcePackService.GetResourcePacksCallCount);
        Assert.Same(firstItem, resourcePackManagement.ResourcePacks[0]);
        Assert.Same(secondItem, resourcePackManagement.ResourcePacks[1]);
    }

    [Fact]
    public async Task ResourcePackManagementOpenFolderAndRevealUseResourcePackPaths()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        var folderService = new FakeInstanceFolderService();
        var resourcePackService = new FakeResourcePackService();
        resourcePackService.ResourcePacksByInstanceId[instance.Id] =
        [
            CreateLocalResourcePack("Fresh Animations.zip", instance.InstanceDirectory)
        ];
        var viewModel = CreateViewModel([instance], folderService: folderService, resourcePackService: resourcePackService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        viewModel.SelectDetailsSectionCommand.Execute(viewModel.DetailSections.Single(section => section.Id == "resource_packs"));
        await TestAsync.WaitForAsync(() => viewModel.Details.ResourcePackManagement.ResourcePacks.Count == 1);

        viewModel.Details.ResourcePackManagement.OpenResourcePackFolderCommand.Execute(null);
        viewModel.Details.ResourcePackManagement.OpenResourcePackLocationCommand.Execute(
            viewModel.Details.ResourcePackManagement.ResourcePacks[0]);

        Assert.Equal(Path.Combine(instance.InstanceDirectory, "resourcepacks"), folderService.LastOpenedPath);
        Assert.Equal(viewModel.Details.ResourcePackManagement.ResourcePacks[0].FullPath, folderService.LastRevealedFilePath);
    }

    [Fact]
    public async Task ResourcePackManagementImportLocalResourcePackCommandImportsSelectedArchive()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        var statusService = new FakeStatusService();
        var resourcePackService = new FakeResourcePackService();
        var archivePath = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "Fresh Animations.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        await File.WriteAllTextAsync(archivePath, "fake archive");
        var filePickerService = new FakeFilePickerService
        {
            ResourcePackArchivePath = archivePath
        };
        var viewModel = CreateViewModel([instance], statusService: statusService, filePickerService: filePickerService, resourcePackService: resourcePackService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        viewModel.SelectDetailsSectionCommand.Execute(viewModel.DetailSections.Single(section => section.Id == "resource_packs"));

        await viewModel.Details.ResourcePackManagement.ImportLocalResourcePackCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Details.ResourcePackManagement.ResourcePacks);
        Assert.Equal("Fresh Animations", viewModel.Details.ResourcePackManagement.ResourcePacks[0].Title);
        Assert.Equal(1, viewModel.Details.ResourcePackManagement.InstalledResourcePackCount);
        Assert.Equal(Strings.Status_LocalResourcePackImported, statusService.LastMessage);
        Assert.False(viewModel.IsInvalidSaveImportDialogOpen);
    }

    [Fact]
    public async Task ResourcePackManagementImportLocalResourcePackCommandRejectsUnsupportedExtensionBeforeService()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        var resourcePackService = new FakeResourcePackService();
        var archivePath = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "resourcepack.rar");
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        await File.WriteAllTextAsync(archivePath, "not a zip");
        var filePickerService = new FakeFilePickerService
        {
            ResourcePackArchivePath = archivePath
        };
        var viewModel = CreateViewModel([instance], filePickerService: filePickerService, resourcePackService: resourcePackService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        viewModel.SelectDetailsSectionCommand.Execute(viewModel.DetailSections.Single(section => section.Id == "resource_packs"));

        await viewModel.Details.ResourcePackManagement.ImportLocalResourcePackCommand.ExecuteAsync(null);

        Assert.Equal(0, resourcePackService.ImportArchiveCallCount);
        Assert.True(viewModel.IsInvalidSaveImportDialogOpen);
        Assert.Equal(Strings.Dialog_UnsupportedResourcePackArchiveMessage, viewModel.InvalidSaveImportDialogMessage);
        Assert.Equal(Strings.Dialog_InvalidResourcePackImportTitle, viewModel.InvalidSaveImportDialogTitle);
    }

    [Fact]
    public async Task ResourcePackManagementDragDropImportsSupportedArchives()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        var statusService = new FakeStatusService();
        var floatingMessageService = new FakeFloatingMessageService();
        var resourcePackService = new FakeResourcePackService();
        var firstArchive = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "Fresh.zip");
        var secondArchive = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "Bare Bones.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(firstArchive)!);
        Directory.CreateDirectory(Path.GetDirectoryName(secondArchive)!);
        await File.WriteAllTextAsync(firstArchive, "first");
        await File.WriteAllTextAsync(secondArchive, "second");
        var viewModel = CreateViewModel(
            [instance],
            statusService: statusService,
            resourcePackService: resourcePackService,
            floatingMessageService: floatingMessageService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        viewModel.SelectDetailsSectionCommand.Execute(viewModel.DetailSections.Single(section => section.Id == "resource_packs"));

        var accepted = viewModel.UpdateImportDropState([firstArchive, secondArchive]);
        Assert.Equal(Strings.GameSettings_DropReleaseToImportMessage, floatingMessageService.LastMessage);
        await viewModel.HandleImportDropAsync([firstArchive, secondArchive]);

        Assert.True(accepted);
        Assert.Equal(2, resourcePackService.ImportArchiveCallCount);
        Assert.Equal(string.Format(Strings.Status_LocalResourcePacksImportedFormat, 2), statusService.LastMessage);
        Assert.Equal(2, viewModel.Details.ResourcePackManagement.ResourcePacks.Count);
        Assert.Equal(string.Empty, floatingMessageService.LastMessage);
    }

    [Fact]
    public async Task ResourcePackManagementDeleteUsesConfirmationDialogBeforeRemovingResourcePacks()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        var statusService = new FakeStatusService();
        var resourcePackService = new FakeResourcePackService();
        resourcePackService.ResourcePacksByInstanceId[instance.Id] =
        [
            CreateLocalResourcePack("Fresh Animations.zip", instance.InstanceDirectory),
            CreateLocalResourcePack("Bare Bones.zip", instance.InstanceDirectory)
        ];
        var viewModel = CreateViewModel([instance], statusService: statusService, resourcePackService: resourcePackService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        viewModel.SelectDetailsSectionCommand.Execute(viewModel.DetailSections.Single(section => section.Id == "resource_packs"));
        await TestAsync.WaitForAsync(() => viewModel.Details.ResourcePackManagement.ResourcePacks.Count == 2);

        var resourcePackManagement = viewModel.Details.ResourcePackManagement;
        resourcePackManagement.ToggleMultiSelectModeCommand.Execute(null);
        resourcePackManagement.SelectAllResourcePacksCommand.Execute(null);
        resourcePackManagement.RequestDeleteSelectedResourcePacksCommand.Execute(null);

        Assert.True(viewModel.IsDeleteModsDialogOpen);
        Assert.Equal(Strings.Dialog_DeleteResourcePacksTitle, viewModel.DeleteModsDialogTitle);
        Assert.Equal(string.Format(Strings.Dialog_DeleteMultipleResourcePacksMessageFormat, 2), viewModel.DeleteModsDialogMessage);

        await viewModel.ConfirmDeleteModsDialogCommand.ExecuteAsync(null);
        await TestAsync.WaitForAsync(() => statusService.LastMessage is not null);

        Assert.Equal(string.Format(Strings.Status_SelectedResourcePacksDeletedFormat, 2), statusService.LastMessage);
        Assert.Empty(viewModel.Details.ResourcePackManagement.ResourcePacks);
    }

    [Fact]
    public async Task ShaderPackManagementDetailsSectionUsesDedicatedViewModel()
    {
        var viewModel = CreateViewModel([CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla)]);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        var section = viewModel.DetailSections.Single(item => item.Id == "shaders");

        viewModel.SelectDetailsSectionCommand.Execute(section);

        Assert.True(section.IsSelected);
        Assert.IsType<InstanceShaderPackManagementSettingsViewModel>(viewModel.Details.CurrentSectionViewModel);
        Assert.Same(viewModel.Details.ShaderPackManagement, viewModel.Details.CurrentSectionViewModel);
        Assert.Equal(Strings.GameSettings_DetailShaders, viewModel.Details.SectionTitle);
    }

    [Fact]
    public async Task ShaderPackManagementViewModelLoadsRealShaderPacksForSelectedInstance()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        var shaderPackService = new FakeShaderPackService();
        var firstCreatedAt = new DateTimeOffset(2026, 1, 3, 10, 20, 30, TimeSpan.Zero);
        var secondCreatedAt = new DateTimeOffset(2026, 1, 2, 8, 9, 10, TimeSpan.Zero);
        shaderPackService.ShaderPacksByInstanceId[instance.Id] =
        [
            CreateLocalShaderPack("Complementary.zip", instance.InstanceDirectory, createdAt: firstCreatedAt),
            CreateLocalShaderPack("BSL.zip", instance.InstanceDirectory, createdAt: secondCreatedAt)
        ];
        var viewModel = CreateViewModel([instance], shaderPackService: shaderPackService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        viewModel.SelectDetailsSectionCommand.Execute(viewModel.DetailSections.Single(section => section.Id == "shaders"));
        await TestAsync.WaitForAsync(() => viewModel.Details.ShaderPackManagement.ShaderPacks.Count == 2);

        var shaderPackManagement = viewModel.Details.ShaderPackManagement;
        Assert.Equal(2, shaderPackManagement.InstalledShaderPackCount);
        Assert.Equal(
            string.Format(Strings.GameSettings_ShaderPackManagementInstalledSummaryFormat, 2),
            shaderPackManagement.InstalledSummaryText);
        Assert.Equal(["Complementary", "BSL"], shaderPackManagement.ShaderPacks.Select(shaderPack => shaderPack.Title));
        Assert.Equal(["instance_setting_page/shader", "instance_setting_page/shader"], shaderPackManagement.ShaderPacks.Select(shaderPack => shaderPack.IconKey));
        Assert.True(shaderPackManagement.ImportLocalShaderPackCommand.CanExecute(null));
        Assert.True(shaderPackManagement.HasShaderPacks);
        Assert.False(shaderPackManagement.CanShowShaderPackEmptyState);
        Assert.Same(shaderPackManagement.ShaderPacks[0], shaderPackManagement.SelectedShaderPack);
    }

    [Fact]
    public async Task ShaderPackManagementSectionReentryReusesVisibleItemViewModelsWithoutReloading()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        var shaderPackService = new FakeShaderPackService();
        shaderPackService.ShaderPacksByInstanceId[instance.Id] =
        [
            CreateLocalShaderPack("Complementary.zip", instance.InstanceDirectory),
            CreateLocalShaderPack("BSL.zip", instance.InstanceDirectory)
        ];
        var viewModel = CreateViewModel([instance], shaderPackService: shaderPackService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        var shaderPackManagement = OpenShaderPackManagementSection(viewModel);
        await TestAsync.WaitForAsync(() => shaderPackManagement.ShaderPacks.Count == 2);

        var initialCallCount = shaderPackService.GetShaderPacksCallCount;
        var firstItem = shaderPackManagement.ShaderPacks[0];
        var secondItem = shaderPackManagement.ShaderPacks[1];

        OpenSaveManagementSection(viewModel);
        OpenShaderPackManagementSection(viewModel);

        Assert.Equal(initialCallCount, shaderPackService.GetShaderPacksCallCount);
        Assert.Same(firstItem, shaderPackManagement.ShaderPacks[0]);
        Assert.Same(secondItem, shaderPackManagement.ShaderPacks[1]);
    }

    [Fact]
    public async Task ShaderPackManagementOpenFolderAndRevealUseShaderPackPaths()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        var folderService = new FakeInstanceFolderService();
        var shaderPackService = new FakeShaderPackService();
        shaderPackService.ShaderPacksByInstanceId[instance.Id] =
        [
            CreateLocalShaderPack("Complementary.zip", instance.InstanceDirectory)
        ];
        var viewModel = CreateViewModel([instance], folderService: folderService, shaderPackService: shaderPackService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        viewModel.SelectDetailsSectionCommand.Execute(viewModel.DetailSections.Single(section => section.Id == "shaders"));
        await TestAsync.WaitForAsync(() => viewModel.Details.ShaderPackManagement.ShaderPacks.Count == 1);

        viewModel.Details.ShaderPackManagement.OpenShaderPackFolderCommand.Execute(null);
        viewModel.Details.ShaderPackManagement.OpenShaderPackLocationCommand.Execute(
            viewModel.Details.ShaderPackManagement.ShaderPacks[0]);

        Assert.Equal(Path.Combine(instance.InstanceDirectory, "shaderpacks"), folderService.LastOpenedPath);
        Assert.Equal(viewModel.Details.ShaderPackManagement.ShaderPacks[0].FullPath, folderService.LastRevealedFilePath);
    }

    [Fact]
    public async Task ShaderPackManagementImportLocalShaderPackCommandImportsSelectedArchive()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        var statusService = new FakeStatusService();
        var shaderPackService = new FakeShaderPackService();
        var archivePath = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "Complementary.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        await File.WriteAllTextAsync(archivePath, "fake archive");
        var filePickerService = new FakeFilePickerService
        {
            ShaderPackArchivePath = archivePath
        };
        var viewModel = CreateViewModel([instance], statusService: statusService, filePickerService: filePickerService, shaderPackService: shaderPackService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        viewModel.SelectDetailsSectionCommand.Execute(viewModel.DetailSections.Single(section => section.Id == "shaders"));

        await viewModel.Details.ShaderPackManagement.ImportLocalShaderPackCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Details.ShaderPackManagement.ShaderPacks);
        Assert.Equal("Complementary", viewModel.Details.ShaderPackManagement.ShaderPacks[0].Title);
        Assert.Equal(1, viewModel.Details.ShaderPackManagement.InstalledShaderPackCount);
        Assert.Equal(Strings.Status_LocalShaderPackImported, statusService.LastMessage);
        Assert.False(viewModel.IsInvalidSaveImportDialogOpen);
    }

    [Fact]
    public async Task ShaderPackManagementDeleteUsesConfirmationDialogBeforeRemovingShaderPacks()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        var statusService = new FakeStatusService();
        var shaderPackService = new FakeShaderPackService();
        shaderPackService.ShaderPacksByInstanceId[instance.Id] =
        [
            CreateLocalShaderPack("Complementary.zip", instance.InstanceDirectory),
            CreateLocalShaderPack("BSL.zip", instance.InstanceDirectory)
        ];
        var viewModel = CreateViewModel([instance], statusService: statusService, shaderPackService: shaderPackService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        viewModel.SelectDetailsSectionCommand.Execute(viewModel.DetailSections.Single(section => section.Id == "shaders"));
        await TestAsync.WaitForAsync(() => viewModel.Details.ShaderPackManagement.ShaderPacks.Count == 2);

        var shaderPackManagement = viewModel.Details.ShaderPackManagement;
        shaderPackManagement.ToggleMultiSelectModeCommand.Execute(null);
        shaderPackManagement.SelectAllShaderPacksCommand.Execute(null);
        shaderPackManagement.RequestDeleteSelectedShaderPacksCommand.Execute(null);

        Assert.True(viewModel.IsDeleteModsDialogOpen);
        Assert.Equal(Strings.Dialog_DeleteShaderPacksTitle, viewModel.DeleteModsDialogTitle);
        Assert.Equal(string.Format(Strings.Dialog_DeleteMultipleShaderPacksMessageFormat, 2), viewModel.DeleteModsDialogMessage);

        await viewModel.ConfirmDeleteModsDialogCommand.ExecuteAsync(null);
        await TestAsync.WaitForAsync(() => statusService.LastMessage is not null);

        Assert.Equal(string.Format(Strings.Status_SelectedShaderPacksDeletedFormat, 2), statusService.LastMessage);
        Assert.Empty(viewModel.Details.ShaderPackManagement.ShaderPacks);
    }

    [Fact]
    public async Task SelectingInstanceDoesNotPreloadLocalResourceSections()
    {
        var instance = CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric);
        var modService = new FakeModService();
        var saveService = new FakeSaveService();
        var resourcePackService = new FakeResourcePackService();
        var shaderPackService = new FakeShaderPackService();
        var viewModel = CreateViewModel(
            [instance],
            modService: modService,
            saveService: saveService,
            resourcePackService: resourcePackService,
            shaderPackService: shaderPackService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());

        Assert.Equal(0, saveService.GetSavesCallCount);
        Assert.Equal(0, resourcePackService.GetResourcePacksCallCount);
        Assert.Equal(0, shaderPackService.GetShaderPacksCallCount);
        Assert.IsType<InstanceGeneralSettingsViewModel>(viewModel.Details.CurrentSectionViewModel);
        Assert.False(viewModel.Details.ModManagement.HasLoadedMods);
        var initialModCallCount = modService.GetModsCallCount;

        var modManagement = OpenModManagementSection(viewModel);
        await TestAsync.WaitForAsync(() => modManagement.HasLoadedMods);
        Assert.Equal(initialModCallCount + 1, modService.GetModsCallCount);

        var saveManagement = OpenSaveManagementSection(viewModel);
        await TestAsync.WaitForAsync(() => saveManagement.HasLoadedSaves);
        Assert.Equal(1, saveService.GetSavesCallCount);

        var resourcePackManagement = OpenResourcePackManagementSection(viewModel);
        await TestAsync.WaitForAsync(() => resourcePackManagement.HasLoadedResourcePacks);
        Assert.Equal(1, resourcePackService.GetResourcePacksCallCount);

        var shaderPackManagement = OpenShaderPackManagementSection(viewModel);
        await TestAsync.WaitForAsync(() => shaderPackManagement.HasLoadedShaderPacks);
        Assert.Equal(1, shaderPackService.GetShaderPacksCallCount);
    }

    [Fact]
    public async Task ModManagementSectionSwitchesImmediatelyWhileModsAreStillLoading()
    {
        var instance = CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric);
        var modService = new FakeModService
        {
            GetModsBlocker = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        modService.ModsByInstanceId[instance.Id] =
        [
            CreateLocalMod("sodium.jar", true, instance.InstanceDirectory, loader: "fabric", modId: "sodium", version: "1.0.0")
        ];
        var viewModel = CreateViewModel([instance], modService: modService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());

        var modManagement = OpenModManagementSection(viewModel);
        await TestAsync.WaitForAsync(() => modService.GetModsCallCount == 1);

        Assert.Same(modManagement, viewModel.Details.CurrentSectionViewModel);
        Assert.True(modManagement.IsLoadingMods);
        Assert.False(modManagement.HasLoadedMods);
        Assert.True(modManagement.CanShowModLoadingState);

        var saveManagement = OpenSaveManagementSection(viewModel);
        Assert.Same(saveManagement, viewModel.Details.CurrentSectionViewModel);
        Assert.True(modManagement.IsLoadingMods);
        Assert.Empty(modManagement.Mods);

        modService.GetModsBlocker.SetResult(true);
        await TestAsync.WaitForAsync(() => modManagement.HasLoadedMods);

        Assert.Empty(modManagement.Mods);

        OpenModManagementSection(viewModel);
        await TestAsync.WaitForAsync(() => modManagement.Mods.Count == 1);

        Assert.False(modManagement.IsLoadingMods);
        Assert.Single(modManagement.Mods);
        Assert.Equal("sodium", modManagement.Mods[0].Title);
    }

    [Fact]
    public async Task ModManagementSectionReentryReusesVisibleItemViewModelsWithoutReloading()
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
        var modManagement = OpenModManagementSection(viewModel);
        await TestAsync.WaitForAsync(() => modManagement.Mods.Count == 2);

        var initialCallCount = modService.GetModsCallCount;
        var firstItem = modManagement.Mods[0];
        var secondItem = modManagement.Mods[1];

        OpenSaveManagementSection(viewModel);
        OpenModManagementSection(viewModel);

        Assert.Equal(initialCallCount, modService.GetModsCallCount);
        Assert.Same(firstItem, modManagement.Mods[0]);
        Assert.Same(secondItem, modManagement.Mods[1]);
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
        var modManagement = OpenModManagementSection(viewModel);
        await TestAsync.WaitForAsync(() => modManagement.Mods.Count == 2);

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
        Assert.All(modManagement.Mods, mod => Assert.False(mod.IsSelected));

        modManagement.SelectModCommand.Execute(modManagement.Mods[1]);

        Assert.Same(modManagement.Mods[1], modManagement.SelectedMod);
        Assert.All(modManagement.Mods, mod => Assert.False(mod.IsSelected));
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
        var modManagement = OpenModManagementSection(viewModel);
        await TestAsync.WaitForAsync(() => modManagement.Mods.Count == 2);

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
        Assert.All(modManagement.Mods, mod => Assert.False(mod.IsSelected));
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
        var modManagement = OpenModManagementSection(viewModel);
        await TestAsync.WaitForAsync(() => modManagement.Mods.Count == 3);

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
        var modManagement = OpenModManagementSection(viewModel);
        await TestAsync.WaitForAsync(() => modManagement.Mods.Count == 2);

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
        var modManagement = OpenModManagementSection(viewModel);
        await TestAsync.WaitForAsync(() => modManagement.Mods.Count == 2);

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
        var modManagement = OpenModManagementSection(viewModel);
        await TestAsync.WaitForAsync(() => modManagement.Mods.Count == 2);

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
        var modManagement = OpenModManagementSection(viewModel);
        await TestAsync.WaitForAsync(() => modManagement.Mods.Count == 2);

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
        var modManagement = OpenModManagementSection(viewModel);
        await TestAsync.WaitForAsync(() => modManagement.Mods.Count == 1);

        var mod = Assert.Single(modManagement.Mods);
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
        OpenModManagementSection(viewModel);

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
        var modManagement = OpenModManagementSection(viewModel);
        await TestAsync.WaitForAsync(() => modManagement.HasLoadedMods);

        await modManagement.ImportLocalModCommand.ExecuteAsync(null);

        Assert.Single(modManagement.Mods);
        Assert.Equal("sodium", modManagement.Mods[0].Title);
        Assert.Equal(1, modManagement.InstalledModCount);
        Assert.Equal(1, modManagement.EnabledModCount);
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
        var modManagement = OpenModManagementSection(viewModel);
        await TestAsync.WaitForAsync(() => modManagement.Mods.Count == 1);

        var firstImportTask = modManagement.ImportLocalModCommand.ExecuteAsync(null);
        await TestAsync.WaitForAsync(() => viewModel.IsReplaceModImportDialogOpen);

        Assert.True(viewModel.IsReplaceModImportDialogOpen);
        Assert.Equal(
            string.Format(Strings.Dialog_ReplaceModImportMessageFormat, "sodium.jar"),
            viewModel.ReplaceModImportDialogMessage);
        Assert.Single(modManagement.Mods);

        viewModel.CancelReplaceModImportDialogCommand.Execute(null);
        await firstImportTask;

        Assert.False(viewModel.IsReplaceModImportDialogOpen);
        Assert.Single(modManagement.Mods);

        var secondImportTask = modManagement.ImportLocalModCommand.ExecuteAsync(null);
        await TestAsync.WaitForAsync(() => viewModel.IsReplaceModImportDialogOpen);
        await viewModel.ConfirmReplaceModImportDialogCommand.ExecuteAsync(null);
        await secondImportTask;

        Assert.False(viewModel.IsReplaceModImportDialogOpen);
        Assert.Single(modManagement.Mods);
        Assert.Equal(Strings.Status_LocalModImported, statusService.LastMessage);
    }

    [Fact]
    public async Task ModManagementImportLocalModCommandRejectsNonJarBeforeService()
    {
        var instance = CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric);
        var statusService = new FakeStatusService();
        var modService = new FakeModService();
        var tempModPath = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "notes.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(tempModPath)!);
        await File.WriteAllTextAsync(tempModPath, "not a mod");
        var filePickerService = new FakeFilePickerService
        {
            ModFilePath = tempModPath
        };
        var viewModel = CreateViewModel([instance], statusService: statusService, filePickerService: filePickerService, modService: modService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());

        await viewModel.Details.ModManagement.ImportLocalModCommand.ExecuteAsync(null);

        Assert.Equal(0, modService.ImportCallCount);
        Assert.Equal(Strings.Status_LocalModImportFailed, statusService.LastMessage);
        Assert.Empty(viewModel.Details.ModManagement.Mods);
    }

    [Fact]
    public async Task ModManagementDragDropRejectsMixedFileTypes()
    {
        var instance = CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric);
        var statusService = new FakeStatusService();
        var floatingMessageService = new FakeFloatingMessageService();
        var modService = new FakeModService();
        var modPath = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "alpha.jar");
        var textPath = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "readme.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(modPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(textPath)!);
        await File.WriteAllTextAsync(modPath, "mod");
        await File.WriteAllTextAsync(textPath, "text");
        var viewModel = CreateViewModel([instance], statusService: statusService, modService: modService, floatingMessageService: floatingMessageService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        viewModel.SelectDetailsSectionCommand.Execute(viewModel.DetailSections.Single(section => section.Id == "mod_management"));

        var accepted = viewModel.UpdateImportDropState([modPath, textPath]);
        Assert.Equal(Strings.GameSettings_DropUnsupportedFileMessage, floatingMessageService.LastMessage);
        await viewModel.HandleImportDropAsync([modPath, textPath]);

        Assert.False(accepted);
        Assert.Equal(0, modService.ImportCallCount);
        Assert.Null(statusService.LastMessage);
        Assert.Equal(string.Empty, floatingMessageService.LastMessage);
    }

    [Fact]
    public async Task ModManagementDragDropContinuesBatchAfterConflictIsCanceled()
    {
        var instance = CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric);
        var statusService = new FakeStatusService();
        var modService = new FakeModService();
        modService.ModsByInstanceId[instance.Id] =
        [
            CreateLocalMod("sodium.jar", true, instance.InstanceDirectory, loader: "fabric", modId: "sodium", version: "1.0.0")
        ];
        var conflictingPath = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "sodium.jar");
        var newPath = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "lithium.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(conflictingPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
        await File.WriteAllTextAsync(conflictingPath, "replacement");
        await File.WriteAllTextAsync(newPath, "new mod");
        var viewModel = CreateViewModel([instance], statusService: statusService, modService: modService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        viewModel.SelectDetailsSectionCommand.Execute(viewModel.DetailSections.Single(section => section.Id == "mod_management"));

        var importTask = viewModel.HandleImportDropAsync([conflictingPath, newPath]);
        await TestAsync.WaitForAsync(() => viewModel.IsReplaceModImportDialogOpen);

        viewModel.CancelReplaceModImportDialogCommand.Execute(null);
        await importTask;

        Assert.False(viewModel.IsReplaceModImportDialogOpen);
        Assert.Equal(1, modService.ImportCallCount);
        Assert.Equal(Strings.Status_LocalModImported, statusService.LastMessage);
        Assert.Contains(viewModel.Details.ModManagement.Mods, mod => mod.Title == "lithium");
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
        var modManagement = OpenModManagementSection(viewModel);
        await TestAsync.WaitForAsync(() => modManagement.Mods.Count == 2);

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
        var modManagement = OpenModManagementSection(viewModel);
        await TestAsync.WaitForAsync(() => modManagement.Mods.Count == 1);

        modManagement.OpenModFileLocationCommand.Execute(modManagement.Mods[0]);

        Assert.Equal(modManagement.Mods[0].FullPath, folderService.LastRevealedFilePath);
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
        var modManagement = OpenModManagementSection(viewModel);
        await TestAsync.WaitForAsync(() => modManagement.Mods.Count == 1);

        var mod = modManagement.Mods[0];
        modManagement.RequestDeleteModCommand.Execute(mod);

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
            var modManagement = OpenModManagementSection(viewModel);
            await TestAsync.WaitForAsync(() => modManagement.HasLoadedMods);

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
                modManagement.Mods.Count == 1
                && modManagement.Mods[0].Title == "Sodium");

            var mod = Assert.Single(modManagement.Mods);
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
        var modManagement = OpenModManagementSection(viewModel);
        await TestAsync.WaitForAsync(() => modManagement.Mods.Count == 1);

        var mod = Assert.Single(modManagement.Mods);
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
        var modManagement = OpenModManagementSection(viewModel);
        await TestAsync.WaitForAsync(() => modManagement.HasLoadedMods);

        Assert.Empty(modManagement.Mods);
        Assert.True(modManagement.IsModManagementSupported);
        Assert.True(modManagement.CanShowModInfoSection);
        Assert.False(modManagement.HasMods);
        Assert.False(modManagement.HasInstalledMods);
        Assert.False(modManagement.CanShowModListSection);
        Assert.True(modManagement.CanShowNoModsEmptyState);
        Assert.False(modManagement.CanShowModEmptyState);
        Assert.False(modManagement.CanShowModUnavailableState);
        Assert.Equal(Strings.GameSettings_ModManagementEmptyMessage, modManagement.ModEmptyMessage);
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
        var modManagement = OpenModManagementSection(viewModel);

        Assert.False(modManagement.IsModManagementSupported);
        Assert.False(modManagement.CanShowModInfoSection);
        Assert.False(modManagement.CanShowModListSection);
        Assert.False(modManagement.CanShowNoModsEmptyState);
        Assert.False(modManagement.CanShowModEmptyState);
        Assert.True(modManagement.CanShowModUnavailableState);
        Assert.Equal(Strings.GameSettings_ModManagementUnavailableMessage, modManagement.ModUnavailableMessage);
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
        var modManagement = OpenModManagementSection(viewModel);
        await TestAsync.WaitForAsync(() => modManagement.Mods.Count == 2);

        modManagement.ModSearchQuery = "sodium";

        Assert.Single(modManagement.Mods);
        Assert.Equal("sodium-fabric-0.5.13", modManagement.Mods[0].Title);
        Assert.Equal(2, modManagement.InstalledModCount);
        Assert.Equal(1, modManagement.EnabledModCount);
        Assert.True(modManagement.CanShowModListSection);
        Assert.False(modManagement.CanShowNoModsEmptyState);
        Assert.False(modManagement.CanShowModEmptyState);

        modManagement.ModSearchQuery = "missing-mod";

        Assert.Empty(modManagement.Mods);
        Assert.True(modManagement.CanShowModListSection);
        Assert.False(modManagement.CanShowNoModsEmptyState);
        Assert.True(modManagement.CanShowModEmptyState);
        Assert.Equal(Strings.GameSettings_ModManagementSearchEmptyMessage, modManagement.ModEmptyMessage);
        Assert.Equal(2, modManagement.InstalledModCount);
        Assert.Equal(1, modManagement.EnabledModCount);
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
        OpenModManagementSection(viewModel);
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

    private static GameSettingsDetailSectionItem GetDetailSection(GameSettingsPageViewModel viewModel, string sectionId)
    {
        return viewModel.DetailSections.Single(section => section.Id == sectionId);
    }

    private static InstanceModManagementSettingsViewModel OpenModManagementSection(GameSettingsPageViewModel viewModel)
    {
        viewModel.SelectDetailsSectionCommand.Execute(GetDetailSection(viewModel, "mod_management"));
        return viewModel.Details.ModManagement;
    }

    private static InstanceSaveManagementSettingsViewModel OpenSaveManagementSection(GameSettingsPageViewModel viewModel)
    {
        viewModel.SelectDetailsSectionCommand.Execute(GetDetailSection(viewModel, "saves"));
        return viewModel.Details.SaveManagement;
    }

    private static InstanceResourcePackManagementSettingsViewModel OpenResourcePackManagementSection(GameSettingsPageViewModel viewModel)
    {
        viewModel.SelectDetailsSectionCommand.Execute(GetDetailSection(viewModel, "resource_packs"));
        return viewModel.Details.ResourcePackManagement;
    }

    private static InstanceShaderPackManagementSettingsViewModel OpenShaderPackManagementSection(GameSettingsPageViewModel viewModel)
    {
        viewModel.SelectDetailsSectionCommand.Execute(GetDetailSection(viewModel, "shaders"));
        return viewModel.Details.ShaderPackManagement;
    }

    private static GameSettingsPageViewModel CreateViewModel(
        IReadOnlyList<GameInstance> instances,
        IReadOnlyList<MinecraftVersionInfo>? versions = null,
        FakeStatusService? statusService = null,
        FakeInstanceFolderService? folderService = null,
        FakeJavaRuntimeDiscoveryService? javaRuntimeDiscoveryService = null,
        FakeFilePickerService? filePickerService = null,
        FakeFloatingMessageService? floatingMessageService = null,
        IModService? modService = null,
        ILocalSaveService? saveService = null,
        ILocalResourcePackService? resourcePackService = null,
        ILocalShaderPackService? shaderPackService = null)
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
            modService ?? new FakeModService(),
            saveService ?? new FakeSaveService(),
            resourcePackService ?? new FakeResourcePackService(),
            shaderPackService ?? new FakeShaderPackService());
    }

    private static GameSettingsPageViewModel CreateViewModel(
        IGameInstanceService instanceService,
        IGameVersionService gameVersionService,
        FakeStatusService statusService,
        FakeInstanceFolderService folderService,
        FakeJavaRuntimeDiscoveryService? javaRuntimeDiscoveryService = null,
        FakeFilePickerService? filePickerService = null,
        FakeFloatingMessageService? floatingMessageService = null,
        IModService? modService = null,
        ILocalSaveService? saveService = null,
        ILocalResourcePackService? resourcePackService = null,
        ILocalShaderPackService? shaderPackService = null)
    {
        var resolvedModService = modService ?? new FakeModService();
        var resolvedSaveService = saveService ?? new FakeSaveService();
        var resolvedResourcePackService = resourcePackService ?? new FakeResourcePackService();
        var resolvedShaderPackService = shaderPackService ?? new FakeShaderPackService();
        return new GameSettingsPageViewModel(
            instanceService,
            gameVersionService,
            statusService,
            folderService,
            new FakeSystemMemoryService(),
            resolvedModService,
            new LocalModsViewModel(resolvedModService, statusService),
            new LocalSavesViewModel(resolvedSaveService, statusService),
            new LocalResourcePacksViewModel(resolvedResourcePackService, statusService),
            new LocalShaderPacksViewModel(resolvedShaderPackService, statusService),
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

    private static LocalSave CreateLocalSave(
        string name,
        string instanceDirectory,
        string? iconSource = null,
        DateTimeOffset? createdAt = null)
    {
        var fullPath = Path.Combine(instanceDirectory, "saves", name);
        return new LocalSave
        {
            Name = name,
            DirectoryName = name,
            FullPath = fullPath,
            IconSource = iconSource,
            CreatedAt = createdAt ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };
    }

    private static LocalResourcePack CreateLocalResourcePack(
        string fileName,
        string instanceDirectory,
        string? iconSource = null,
        DateTimeOffset? createdAt = null)
    {
        var fullPath = Path.Combine(instanceDirectory, "resourcepacks", fileName);
        return new LocalResourcePack
        {
            Name = Path.GetFileNameWithoutExtension(fileName),
            FileName = fileName,
            FullPath = fullPath,
            IconSource = iconSource,
            CreatedAt = createdAt ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };
    }

    private static LocalShaderPack CreateLocalShaderPack(
        string fileName,
        string instanceDirectory,
        string? iconSource = null,
        DateTimeOffset? createdAt = null)
    {
        var fullPath = Path.Combine(instanceDirectory, "shaderpacks", fileName);
        return new LocalShaderPack
        {
            Name = Path.GetFileNameWithoutExtension(fileName),
            FileName = fileName,
            FullPath = fullPath,
            IconSource = iconSource,
            CreatedAt = createdAt ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
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
        public string? SaveArchivePath { get; init; }
        public string? ResourcePackArchivePath { get; init; }
        public string? ShaderPackArchivePath { get; init; }
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

        public string? PickSaveArchive()
        {
            return SaveArchivePath;
        }

        public string? PickResourcePackArchive()
        {
            return ResourcePackArchivePath;
        }

        public string? PickShaderPackArchive()
        {
            return ShaderPackArchivePath;
        }

        public string? PickLocalImportFile()
        {
            return null;
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

        public TaskCompletionSource<bool>? GetModsBlocker { get; init; }

        public int GetModsCallCount { get; private set; }

        public int ImportCallCount { get; private set; }

        public List<string> ImportedPaths { get; } = [];

        public async Task<IReadOnlyList<LocalMod>> GetModsAsync(GameInstance instance, CancellationToken cancellationToken = default)
        {
            GetModsCallCount++;
            if (GetModsException is not null)
                throw GetModsException;

            if (GetModsBlocker is not null)
                await GetModsBlocker.Task.WaitAsync(cancellationToken);

            return
                ModsByInstanceId.TryGetValue(instance.Id, out var mods)
                    ? (IReadOnlyList<LocalMod>)mods.Select(CloneLocalMod).ToArray()
                    : (IReadOnlyList<LocalMod>)[];
        }

        public Task<LocalMod> ImportAsync(
            GameInstance instance,
            string sourceJarPath,
            bool overwriteExisting = false,
            CancellationToken cancellationToken = default)
        {
            ImportCallCount++;
            ImportedPaths.Add(sourceJarPath);
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

    private sealed class FakeSaveService : ILocalSaveService
    {
        public Dictionary<string, List<LocalSave>> SavesByInstanceId { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Exception? GetSavesException { get; init; }

        public TaskCompletionSource<bool>? GetSavesBlocker { get; init; }

        public int GetSavesCallCount { get; private set; }

        public LocalSaveImportResult? NextImportResult { get; init; }

        public int ImportArchiveCallCount { get; private set; }

        public string? LastImportedArchivePath { get; private set; }

        public List<string> ImportedArchivePaths { get; } = [];

        public async Task<IReadOnlyList<LocalSave>> GetSavesAsync(GameInstance instance, CancellationToken cancellationToken = default)
        {
            GetSavesCallCount++;
            if (GetSavesException is not null)
                throw GetSavesException;

            if (GetSavesBlocker is not null)
                await GetSavesBlocker.Task.WaitAsync(cancellationToken);

            return
                SavesByInstanceId.TryGetValue(instance.Id, out var saves)
                    ? (IReadOnlyList<LocalSave>)saves.Select(CloneLocalSave).ToArray()
                    : (IReadOnlyList<LocalSave>)[];
        }

        public Task<LocalSaveImportResult> ImportFromArchiveAsync(
            GameInstance instance,
            string archivePath,
            CancellationToken cancellationToken = default)
        {
            ImportArchiveCallCount++;
            LastImportedArchivePath = archivePath;
            ImportedArchivePaths.Add(archivePath);

            if (NextImportResult is not null)
            {
                if (NextImportResult.IsSuccess && NextImportResult.ImportedSave is not null)
                    AddOrUpdateSave(instance.Id, NextImportResult.ImportedSave);

                return Task.FromResult(CloneImportResult(NextImportResult));
            }

            if (!File.Exists(archivePath))
                return Task.FromResult(LocalSaveImportResult.Failure(LocalSaveImportFailureReason.FileNotFound));

            var importedSaveName = ResolveUniqueSaveName(instance.Id, RemoveAllExtensions(Path.GetFileName(archivePath)));
            var importedSave = CreateLocalSave(importedSaveName, instance.InstanceDirectory);
            AddOrUpdateSave(instance.Id, importedSave);
            return Task.FromResult(LocalSaveImportResult.Success(CloneLocalSave(importedSave)));
        }

        public Task DeleteAsync(LocalSave save, CancellationToken cancellationToken = default)
        {
            foreach (var pair in SavesByInstanceId)
            {
                pair.Value.RemoveAll(candidate =>
                    string.Equals(candidate.FullPath, save.FullPath, StringComparison.OrdinalIgnoreCase));
            }

            return Task.CompletedTask;
        }

        public async Task DeleteAsync(IEnumerable<LocalSave> saves, CancellationToken cancellationToken = default)
        {
            foreach (var save in saves)
                await DeleteAsync(save, cancellationToken);
        }

        private static LocalSave CloneLocalSave(LocalSave save)
        {
            return new LocalSave
            {
                Name = save.Name,
                DirectoryName = save.DirectoryName,
                FullPath = save.FullPath,
                IconSource = save.IconSource,
                CreatedAt = save.CreatedAt
            };
        }

        private void AddOrUpdateSave(string instanceId, LocalSave save)
        {
            if (!SavesByInstanceId.TryGetValue(instanceId, out var saves))
            {
                saves = [];
                SavesByInstanceId[instanceId] = saves;
            }

            saves.RemoveAll(candidate => string.Equals(candidate.FullPath, save.FullPath, StringComparison.OrdinalIgnoreCase));
            saves.Add(CloneLocalSave(save));
        }

        private string ResolveUniqueSaveName(string instanceId, string baseName)
        {
            var normalizedBaseName = string.IsNullOrWhiteSpace(baseName) ? "Imported Save" : baseName;
            if (!SavesByInstanceId.TryGetValue(instanceId, out var saves))
                return normalizedBaseName;

            var candidate = normalizedBaseName;
            var index = 1;
            while (saves.Any(save => string.Equals(save.DirectoryName, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                candidate = $"{normalizedBaseName} ({index})";
                index++;
            }

            return candidate;
        }

        private static LocalSaveImportResult CloneImportResult(LocalSaveImportResult result)
        {
            return result.IsSuccess && result.ImportedSave is not null
                ? LocalSaveImportResult.Success(CloneLocalSave(result.ImportedSave))
                : LocalSaveImportResult.Failure(result.FailureReason);
        }

        private static string RemoveAllExtensions(string fileName)
        {
            var candidate = fileName;
            while (true)
            {
                var withoutExtension = Path.GetFileNameWithoutExtension(candidate);
                if (string.Equals(candidate, withoutExtension, StringComparison.Ordinal))
                    return withoutExtension;

                candidate = withoutExtension;
            }
        }
    }

    private sealed class FakeResourcePackService : ILocalResourcePackService
    {
        public Dictionary<string, List<LocalResourcePack>> ResourcePacksByInstanceId { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Exception? GetResourcePacksException { get; init; }

        public TaskCompletionSource<bool>? GetResourcePacksBlocker { get; init; }

        public int GetResourcePacksCallCount { get; private set; }

        public LocalResourcePackImportResult? NextImportResult { get; init; }

        public int ImportArchiveCallCount { get; private set; }

        public string? LastImportedArchivePath { get; private set; }

        public List<string> ImportedArchivePaths { get; } = [];

        public async Task<IReadOnlyList<LocalResourcePack>> GetResourcePacksAsync(
            GameInstance instance,
            CancellationToken cancellationToken = default)
        {
            GetResourcePacksCallCount++;
            if (GetResourcePacksException is not null)
                throw GetResourcePacksException;

            if (GetResourcePacksBlocker is not null)
                await GetResourcePacksBlocker.Task.WaitAsync(cancellationToken);

            return
                ResourcePacksByInstanceId.TryGetValue(instance.Id, out var resourcePacks)
                    ? (IReadOnlyList<LocalResourcePack>)resourcePacks.Select(CloneLocalResourcePack).ToArray()
                    : (IReadOnlyList<LocalResourcePack>)[];
        }

        public Task<LocalResourcePackImportResult> ImportAsync(
            GameInstance instance,
            string archivePath,
            CancellationToken cancellationToken = default)
        {
            ImportArchiveCallCount++;
            LastImportedArchivePath = archivePath;
            ImportedArchivePaths.Add(archivePath);

            if (NextImportResult is not null)
            {
                if (NextImportResult.IsSuccess && NextImportResult.ImportedResourcePack is not null)
                    AddOrUpdateResourcePack(instance.Id, NextImportResult.ImportedResourcePack);

                return Task.FromResult(CloneImportResult(NextImportResult));
            }

            if (!File.Exists(archivePath))
            {
                return Task.FromResult(
                    LocalResourcePackImportResult.Failure(LocalResourcePackImportFailureReason.FileNotFound));
            }

            var fileName = ResolveUniqueResourcePackFileName(instance.Id, Path.GetFileName(archivePath));
            var importedResourcePack = CreateLocalResourcePack(fileName, instance.InstanceDirectory);
            AddOrUpdateResourcePack(instance.Id, importedResourcePack);
            return Task.FromResult(LocalResourcePackImportResult.Success(CloneLocalResourcePack(importedResourcePack)));
        }

        public Task DeleteAsync(LocalResourcePack resourcePack, CancellationToken cancellationToken = default)
        {
            foreach (var pair in ResourcePacksByInstanceId)
            {
                pair.Value.RemoveAll(candidate =>
                    string.Equals(candidate.FullPath, resourcePack.FullPath, StringComparison.OrdinalIgnoreCase));
            }

            return Task.CompletedTask;
        }

        public async Task DeleteAsync(IEnumerable<LocalResourcePack> resourcePacks, CancellationToken cancellationToken = default)
        {
            foreach (var resourcePack in resourcePacks)
                await DeleteAsync(resourcePack, cancellationToken);
        }

        private static LocalResourcePack CloneLocalResourcePack(LocalResourcePack resourcePack)
        {
            return new LocalResourcePack
            {
                Name = resourcePack.Name,
                FileName = resourcePack.FileName,
                FullPath = resourcePack.FullPath,
                IconSource = resourcePack.IconSource,
                CreatedAt = resourcePack.CreatedAt
            };
        }

        private void AddOrUpdateResourcePack(string instanceId, LocalResourcePack resourcePack)
        {
            if (!ResourcePacksByInstanceId.TryGetValue(instanceId, out var resourcePacks))
            {
                resourcePacks = [];
                ResourcePacksByInstanceId[instanceId] = resourcePacks;
            }

            resourcePacks.RemoveAll(candidate =>
                string.Equals(candidate.FullPath, resourcePack.FullPath, StringComparison.OrdinalIgnoreCase));
            resourcePacks.Add(CloneLocalResourcePack(resourcePack));
        }

        private string ResolveUniqueResourcePackFileName(string instanceId, string baseFileName)
        {
            var normalizedBaseFileName = string.IsNullOrWhiteSpace(baseFileName) ? "Imported Resource Pack.zip" : baseFileName;
            if (!ResourcePacksByInstanceId.TryGetValue(instanceId, out var resourcePacks))
                return normalizedBaseFileName;

            var baseName = Path.GetFileNameWithoutExtension(normalizedBaseFileName);
            var extension = Path.GetExtension(normalizedBaseFileName);
            var candidate = normalizedBaseFileName;
            var index = 1;
            while (resourcePacks.Any(resourcePack => string.Equals(resourcePack.FileName, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                candidate = $"{baseName} ({index}){extension}";
                index++;
            }

            return candidate;
        }

        private static LocalResourcePackImportResult CloneImportResult(LocalResourcePackImportResult result)
        {
            return result.IsSuccess && result.ImportedResourcePack is not null
                ? LocalResourcePackImportResult.Success(CloneLocalResourcePack(result.ImportedResourcePack))
                : LocalResourcePackImportResult.Failure(result.FailureReason);
        }
    }

    private sealed class FakeShaderPackService : ILocalShaderPackService
    {
        public Dictionary<string, List<LocalShaderPack>> ShaderPacksByInstanceId { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Exception? GetShaderPacksException { get; init; }

        public TaskCompletionSource<bool>? GetShaderPacksBlocker { get; init; }

        public int GetShaderPacksCallCount { get; private set; }

        public LocalShaderPackImportResult? NextImportResult { get; init; }

        public int ImportArchiveCallCount { get; private set; }

        public string? LastImportedArchivePath { get; private set; }

        public List<string> ImportedArchivePaths { get; } = [];

        public async Task<IReadOnlyList<LocalShaderPack>> GetShaderPacksAsync(
            GameInstance instance,
            CancellationToken cancellationToken = default)
        {
            GetShaderPacksCallCount++;
            if (GetShaderPacksException is not null)
                throw GetShaderPacksException;

            if (GetShaderPacksBlocker is not null)
                await GetShaderPacksBlocker.Task.WaitAsync(cancellationToken);

            return
                ShaderPacksByInstanceId.TryGetValue(instance.Id, out var shaderPacks)
                    ? (IReadOnlyList<LocalShaderPack>)shaderPacks.Select(CloneLocalShaderPack).ToArray()
                    : (IReadOnlyList<LocalShaderPack>)[];
        }

        public Task<LocalShaderPackImportResult> ImportAsync(
            GameInstance instance,
            string archivePath,
            CancellationToken cancellationToken = default)
        {
            ImportArchiveCallCount++;
            LastImportedArchivePath = archivePath;
            ImportedArchivePaths.Add(archivePath);

            if (NextImportResult is not null)
            {
                if (NextImportResult.IsSuccess && NextImportResult.ImportedShaderPack is not null)
                    AddOrUpdateShaderPack(instance.Id, NextImportResult.ImportedShaderPack);

                return Task.FromResult(CloneImportResult(NextImportResult));
            }

            if (!File.Exists(archivePath))
            {
                return Task.FromResult(
                    LocalShaderPackImportResult.Failure(LocalShaderPackImportFailureReason.FileNotFound));
            }

            var fileName = ResolveUniqueShaderPackFileName(instance.Id, Path.GetFileName(archivePath));
            var importedShaderPack = CreateLocalShaderPack(fileName, instance.InstanceDirectory);
            AddOrUpdateShaderPack(instance.Id, importedShaderPack);
            return Task.FromResult(LocalShaderPackImportResult.Success(CloneLocalShaderPack(importedShaderPack)));
        }

        public Task DeleteAsync(LocalShaderPack shaderPack, CancellationToken cancellationToken = default)
        {
            foreach (var pair in ShaderPacksByInstanceId)
            {
                pair.Value.RemoveAll(candidate =>
                    string.Equals(candidate.FullPath, shaderPack.FullPath, StringComparison.OrdinalIgnoreCase));
            }

            return Task.CompletedTask;
        }

        public async Task DeleteAsync(IEnumerable<LocalShaderPack> shaderPacks, CancellationToken cancellationToken = default)
        {
            foreach (var shaderPack in shaderPacks)
                await DeleteAsync(shaderPack, cancellationToken);
        }

        private static LocalShaderPack CloneLocalShaderPack(LocalShaderPack shaderPack)
        {
            return new LocalShaderPack
            {
                Name = shaderPack.Name,
                FileName = shaderPack.FileName,
                FullPath = shaderPack.FullPath,
                IconSource = shaderPack.IconSource,
                CreatedAt = shaderPack.CreatedAt
            };
        }

        private void AddOrUpdateShaderPack(string instanceId, LocalShaderPack shaderPack)
        {
            if (!ShaderPacksByInstanceId.TryGetValue(instanceId, out var shaderPacks))
            {
                shaderPacks = [];
                ShaderPacksByInstanceId[instanceId] = shaderPacks;
            }

            shaderPacks.RemoveAll(candidate =>
                string.Equals(candidate.FullPath, shaderPack.FullPath, StringComparison.OrdinalIgnoreCase));
            shaderPacks.Add(CloneLocalShaderPack(shaderPack));
        }

        private string ResolveUniqueShaderPackFileName(string instanceId, string baseFileName)
        {
            var normalizedBaseFileName = string.IsNullOrWhiteSpace(baseFileName) ? "Imported Shader Pack.zip" : baseFileName;
            if (!ShaderPacksByInstanceId.TryGetValue(instanceId, out var shaderPacks))
                return normalizedBaseFileName;

            var baseName = Path.GetFileNameWithoutExtension(normalizedBaseFileName);
            var extension = Path.GetExtension(normalizedBaseFileName);
            var candidate = normalizedBaseFileName;
            var index = 1;
            while (shaderPacks.Any(shaderPack => string.Equals(shaderPack.FileName, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                candidate = $"{baseName} ({index}){extension}";
                index++;
            }

            return candidate;
        }

        private static LocalShaderPackImportResult CloneImportResult(LocalShaderPackImportResult result)
        {
            return result.IsSuccess && result.ImportedShaderPack is not null
                ? LocalShaderPackImportResult.Success(CloneLocalShaderPack(result.ImportedShaderPack))
                : LocalShaderPackImportResult.Failure(result.FailureReason);
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
            int downloadSpeedLimitMbPerSecond = 0,
            bool installFabricApi = true)
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
