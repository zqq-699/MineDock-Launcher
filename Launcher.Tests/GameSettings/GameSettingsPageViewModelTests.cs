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
    public async Task GameSettingsPageReturnsToListWhenDetailsInstanceDisappearsDuringRefresh()
    {
        var instance = CreateInstance("Release World", "1.21.4", LoaderKind.Vanilla);
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(instance);
        var viewModel = CreateViewModel(instanceService, new FakeGameVersionService([]), new FakeStatusService(), new FakeInstanceFolderService());

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.ShowInstanceDetails(instance);
        instanceService.CreatedInstances.Clear();

        await viewModel.RefreshInstancesForPageActivationAsync();

        Assert.False(viewModel.IsDetailsStep);
        Assert.True(viewModel.IsListStep);
        Assert.Null(viewModel.SelectedInstance);
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
        viewModel.InstancesChanged += _ => syncRequested++;

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
                Strings.GameSettings_DetailResourcePacks,
                Strings.GameSettings_DetailShaders,
                Strings.GameSettings_DetailBackup,
                Strings.GameSettings_DetailExport
            ],
            viewModel.DetailSections.Select(section => section.Title));
    }

    [Fact]
    public async Task ModManagementOnlineInstallRaisesRequestForSelectedModdedInstance()
    {
        var instance = CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric);
        var viewModel = CreateViewModel([instance]);
        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        GameInstance? requestedInstance = null;
        viewModel.OnlineModInstallRequested += instance => requestedInstance = instance;

        viewModel.Details.ModManagement.InstallOnlineModCommand.Execute(null);

        Assert.Same(instance, requestedInstance);
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
        Assert.Same(saveManagement.VisibleSaves, saveManagement.Saves);
        Assert.Equal(4, saveManagement.VisibleSaveListItems.Count);
        Assert.IsType<SaveManagementInfoPanelItem>(saveManagement.VisibleSaveListItems[0]);
        Assert.IsType<SaveManagementListSectionItem>(saveManagement.VisibleSaveListItems[1]);
        Assert.Same(saveManagement.Saves[0], saveManagement.VisibleSaveListItems[2]);
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
        Assert.Single(saveManagement.VisibleSaveListItems);
        Assert.IsType<SaveManagementInfoPanelItem>(saveManagement.VisibleSaveListItems[0]);
        Assert.False(saveManagement.IsMultiSelectMode);
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
        Assert.Same(resourcePackManagement.VisibleResourcePacks, resourcePackManagement.ResourcePacks);
        Assert.Equal(4, resourcePackManagement.VisibleResourcePackListItems.Count);
        Assert.IsType<ResourcePackManagementInfoPanelItem>(resourcePackManagement.VisibleResourcePackListItems[0]);
        Assert.IsType<ResourcePackManagementListSectionItem>(resourcePackManagement.VisibleResourcePackListItems[1]);
        Assert.Same(resourcePackManagement.ResourcePacks[0], resourcePackManagement.VisibleResourcePackListItems[2]);
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
        Assert.Single(viewModel.Details.ResourcePackManagement.VisibleResourcePackListItems);
        Assert.IsType<ResourcePackManagementInfoPanelItem>(viewModel.Details.ResourcePackManagement.VisibleResourcePackListItems[0]);
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
        Assert.Same(shaderPackManagement.VisibleShaderPacks, shaderPackManagement.ShaderPacks);
        Assert.Equal(4, shaderPackManagement.VisibleShaderPackListItems.Count);
        Assert.IsType<ShaderPackManagementInfoPanelItem>(shaderPackManagement.VisibleShaderPackListItems[0]);
        Assert.IsType<ShaderPackManagementListSectionItem>(shaderPackManagement.VisibleShaderPackListItems[1]);
        Assert.Same(shaderPackManagement.ShaderPacks[0], shaderPackManagement.VisibleShaderPackListItems[2]);
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
        Assert.Single(viewModel.Details.ShaderPackManagement.VisibleShaderPackListItems);
        Assert.IsType<ShaderPackManagementInfoPanelItem>(viewModel.Details.ShaderPackManagement.VisibleShaderPackListItems[0]);
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
            ["sodium-fabric-0.5.13.jar", "lithium-fabric-0.14.7.jar.disabled"],
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
    public async Task BackupDetailsSectionDefaultsToNoDirectoryAndDisablesOpenCommand()
    {
        var viewModel = CreateViewModel([CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla)]);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        OpenBackupSection(viewModel);

        Assert.Equal(Strings.GameSettings_BackupDirectoryNotSelected, viewModel.Details.Backup.BackupDirectoryText);
        Assert.Equal(
            string.Format(Strings.GameSettings_BackupInfoSummaryFormat, 0),
            viewModel.Details.Backup.BackupInfoText);
        Assert.False(viewModel.Details.Backup.OpenBackupFolderCommand.CanExecute(null));
        Assert.False(viewModel.Details.Backup.CreateBackupNowCommand.CanExecute(null));
        Assert.Single(viewModel.Details.Backup.VisibleBackupListItems);
        Assert.IsType<BackupManagementInfoPanelItem>(viewModel.Details.Backup.VisibleBackupListItems[0]);
    }

    [Fact]
    public async Task ChangeBackupDirectoryCommandSavesDirectoryAndRefreshesCount()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        var targetDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "backups");
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(instance);
        var statusService = new FakeStatusService();
        var filePickerService = new FakeFilePickerService { FolderPath = targetDirectory };
        var backupService = new FakeInstanceBackupService();
        backupService.Backups.AddRange(
        [
            CreateBackupRecord("Alpha", "alpha.zip"),
            CreateBackupRecord("Beta", "beta.zip")
        ]);
        var viewModel = CreateViewModel(
            instanceService,
            new FakeGameVersionService([]),
            statusService,
            new FakeInstanceFolderService(),
            filePickerService: filePickerService,
            backupService: backupService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        OpenBackupSection(viewModel);

        await viewModel.Details.Backup.ChangeBackupDirectoryCommand.ExecuteAsync(null);

        var normalizedDirectory = Path.GetFullPath(targetDirectory);
        Assert.Equal(normalizedDirectory, instance.BackupDirectory);
        Assert.Equal(normalizedDirectory, viewModel.Details.Backup.BackupDirectoryText);
        Assert.Equal(normalizedDirectory, instanceService.LastSavedInstance?.BackupDirectory);
        Assert.Equal(Strings.FilePicker_BackupDirectoryTitle, filePickerService.LastFolderPickerTitle);
        Assert.Null(filePickerService.LastFolderPickerInitialDirectory);
        Assert.Equal(Strings.Status_BackupDirectoryChanged, statusService.LastMessage);
        Assert.Equal(
            string.Format(Strings.GameSettings_BackupInfoSummaryFormat, 2),
            viewModel.Details.Backup.BackupInfoText);
        Assert.Equal(4, viewModel.Details.Backup.VisibleBackupListItems.Count);
        Assert.IsType<BackupManagementInfoPanelItem>(viewModel.Details.Backup.VisibleBackupListItems[0]);
        Assert.IsType<BackupManagementListSectionItem>(viewModel.Details.Backup.VisibleBackupListItems[1]);
        Assert.Same(viewModel.Details.Backup.VisibleBackups[0], viewModel.Details.Backup.VisibleBackupListItems[2]);
        Assert.True(viewModel.Details.Backup.OpenBackupFolderCommand.CanExecute(null));
        Assert.True(viewModel.Details.Backup.CreateBackupNowCommand.CanExecute(null));
    }

    [Fact]
    public async Task ChangeBackupDirectoryCommandDoesNothingWhenPickerIsCanceled()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(instance);
        var statusService = new FakeStatusService();
        var viewModel = CreateViewModel(
            instanceService,
            new FakeGameVersionService([]),
            statusService,
            new FakeInstanceFolderService());

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        OpenBackupSection(viewModel);

        await viewModel.Details.Backup.ChangeBackupDirectoryCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, instance.BackupDirectory);
        Assert.Equal(0, instanceService.SaveCallCount);
        Assert.Null(statusService.LastMessage);
    }

    [Fact]
    public async Task OpenBackupFolderCommandReportsFailureWhenFolderCannotOpen()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        instance.BackupDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "backups");
        var statusService = new FakeStatusService();
        var folderService = new FakeInstanceFolderService { OpenResult = false };
        var viewModel = CreateViewModel([instance], statusService: statusService, folderService: folderService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        OpenBackupSection(viewModel);

        await viewModel.Details.Backup.OpenBackupFolderCommand.ExecuteAsync(null);

        Assert.Equal(Path.GetFullPath(instance.BackupDirectory), folderService.LastOpenedPath);
        Assert.Equal(Strings.Status_OpenBackupDirectoryFailed, statusService.LastMessage);
    }

    [Fact]
    public async Task OpenBackupLocationCommandRevealsBackupZip()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        instance.BackupDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "backups");
        var backup = CreateBackupRecord("Nightly", "nightly.zip");
        backup.FullPath = Path.Combine(instance.BackupDirectory, backup.FileName);
        var backupService = new FakeInstanceBackupService();
        backupService.Backups.Add(backup);
        var folderService = new FakeInstanceFolderService();
        var viewModel = CreateViewModel([instance], folderService: folderService, backupService: backupService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        OpenBackupSection(viewModel);

        viewModel.Details.Backup.OpenBackupLocationCommand.Execute(viewModel.Details.Backup.VisibleBackups.Single());

        Assert.Equal(backup.FullPath, folderService.LastRevealedFilePath);
    }

    [Fact]
    public async Task ConfirmDeleteBackupCommandDeletesBackupAndRefreshesList()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        instance.BackupDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "backups");
        var backup = CreateBackupRecord("Nightly", "nightly.zip");
        backup.FullPath = Path.Combine(instance.BackupDirectory, backup.FileName);
        var backupService = new FakeInstanceBackupService();
        backupService.Backups.Add(backup);
        var statusService = new FakeStatusService();
        var viewModel = CreateViewModel([instance], statusService: statusService, backupService: backupService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        OpenBackupSection(viewModel);

        viewModel.Details.Backup.RequestDeleteBackupCommand.Execute(viewModel.Details.Backup.VisibleBackups.Single());
        await viewModel.Details.Backup.ConfirmDeleteBackupCommand.ExecuteAsync(null);

        Assert.False(viewModel.Details.Backup.IsDeleteBackupDialogOpen);
        Assert.Equal(1, backupService.DeleteBackupCallCount);
        Assert.Equal(instance.BackupDirectory, backupService.LastDeletedBackupDirectory);
        Assert.Equal(backup.FullPath, backupService.LastDeletedBackupFullPath);
        Assert.Empty(viewModel.Details.Backup.VisibleBackups);
        Assert.Single(viewModel.Details.Backup.VisibleBackupListItems);
        Assert.IsType<BackupManagementInfoPanelItem>(viewModel.Details.Backup.VisibleBackupListItems[0]);
        Assert.Equal(
            string.Format(Strings.GameSettings_BackupInfoSummaryFormat, 0),
            viewModel.Details.Backup.BackupInfoText);
        Assert.Equal(Strings.Status_BackupDeleted, statusService.LastMessage);
    }

    [Fact]
    public async Task ConfirmDeleteBackupCommandReportsFailureWithoutRemovingBackup()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        instance.BackupDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "backups");
        var backupService = new FakeInstanceBackupService
        {
            DeleteException = new IOException("delete failed")
        };
        backupService.Backups.Add(CreateBackupRecord("Nightly", "nightly.zip"));
        var statusService = new FakeStatusService();
        var viewModel = CreateViewModel([instance], statusService: statusService, backupService: backupService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        OpenBackupSection(viewModel);

        viewModel.Details.Backup.RequestDeleteBackupCommand.Execute(viewModel.Details.Backup.VisibleBackups.Single());
        await viewModel.Details.Backup.ConfirmDeleteBackupCommand.ExecuteAsync(null);

        Assert.False(viewModel.Details.Backup.IsDeleteBackupDialogOpen);
        Assert.Equal(1, backupService.DeleteBackupCallCount);
        Assert.Equal(["Nightly"], viewModel.Details.Backup.VisibleBackups.Select(backup => backup.Title));
        Assert.Equal(Strings.Status_BackupDeleteFailed, statusService.LastMessage);
    }

    [Fact]
    public async Task RequestDeleteSelectedBackupsCommandOpensConfirmationAndDeletesSelectedBackups()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        instance.BackupDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "backups");
        var backupService = new FakeInstanceBackupService();
        backupService.Backups.AddRange(
        [
            CreateBackupRecord("Nightly One", "nightly-one.zip"),
            CreateBackupRecord("Nightly Two", "nightly-two.zip"),
            CreateBackupRecord("Release", "release.zip")
        ]);
        var statusService = new FakeStatusService();
        var viewModel = CreateViewModel([instance], statusService: statusService, backupService: backupService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        OpenBackupSection(viewModel);

        viewModel.TopSearchQuery = "nightly";
        viewModel.Details.Backup.ToggleMultiSelectModeCommand.Execute(null);
        viewModel.Details.Backup.SelectAllBackupsCommand.Execute(null);
        viewModel.Details.Backup.RequestDeleteSelectedBackupsCommand.Execute(null);

        Assert.True(viewModel.Details.Backup.IsDeleteBackupDialogOpen);
        Assert.Equal(string.Format(Strings.Dialog_DeleteMultipleBackupsMessageFormat, 2), viewModel.Details.Backup.DeleteBackupDialogMessage);
        Assert.Equal(0, backupService.DeleteBackupCallCount);

        await viewModel.Details.Backup.ConfirmDeleteBackupCommand.ExecuteAsync(null);

        Assert.False(viewModel.Details.Backup.IsDeleteBackupDialogOpen);
        Assert.False(viewModel.Details.Backup.IsMultiSelectMode);
        Assert.Equal(2, backupService.DeleteBackupCallCount);
        Assert.Equal(["Release"], backupService.Backups.Select(backup => backup.Name));
        Assert.Equal(string.Format(Strings.Status_SelectedBackupsDeletedFormat, 2), statusService.LastMessage);
    }

    [Fact]
    public async Task RequestRestoreBackupCommandOpensConfirmationDialogOnly()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        instance.BackupDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "backups");
        var backupService = new FakeInstanceBackupService();
        backupService.Backups.Add(CreateBackupRecord("Nightly", "nightly.zip"));
        var viewModel = CreateViewModel([instance], backupService: backupService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        OpenBackupSection(viewModel);

        viewModel.Details.Backup.RequestRestoreBackupCommand.Execute(viewModel.Details.Backup.VisibleBackups.Single());

        Assert.True(viewModel.Details.Backup.IsRestoreBackupDialogOpen);
        Assert.Contains("Nightly", viewModel.Details.Backup.RestoreBackupDialogMessage);
        Assert.Contains("自动备份", viewModel.Details.Backup.RestoreBackupDialogMessage);
        Assert.Equal(0, backupService.CreateBackupCallCount);
        Assert.Equal(0, backupService.RestoreBackupCallCount);
    }

    [Fact]
    public async Task ConfirmRestoreBackupCommandCreatesProtectionBackupThenRestores()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        instance.BackupDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "backups");
        var backup = CreateBackupRecord("Nightly", "nightly.zip");
        backup.FullPath = Path.Combine(instance.BackupDirectory, backup.FileName);
        var backupService = new FakeInstanceBackupService();
        backupService.Backups.Add(backup);
        var floatingMessageService = new FakeFloatingMessageService();
        var viewModel = CreateViewModel(
            [instance],
            backupService: backupService,
            floatingMessageService: floatingMessageService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        OpenBackupSection(viewModel);

        viewModel.Details.Backup.RequestRestoreBackupCommand.Execute(viewModel.Details.Backup.VisibleBackups.Single());
        await viewModel.Details.Backup.ConfirmRestoreBackupCommand.ExecuteAsync(null);

        Assert.False(viewModel.Details.Backup.IsRestoreBackupDialogOpen);
        Assert.False(viewModel.Details.Backup.IsRestoringBackup);
        Assert.Equal(1, backupService.CreateBackupCallCount);
        Assert.StartsWith("恢复前备份 ", backupService.LastCreatedBackupName);
        Assert.Equal(1, backupService.RestoreBackupCallCount);
        Assert.Same(instance, backupService.LastRestoredBackupInstance);
        Assert.Equal(instance.BackupDirectory, backupService.LastRestoredBackupDirectory);
        Assert.Equal(backup.FullPath, backupService.LastRestoredBackupFullPath);
        Assert.Equal(
            [
                $"create:{backupService.LastCreatedBackupName}",
                $"restore:{backup.FullPath}"
            ],
            backupService.Operations);
        Assert.Equal(Strings.Status_BackupRestored, floatingMessageService.LastMessage);
        Assert.Equal(2, viewModel.Details.Backup.BackupCount);
    }

    [Fact]
    public async Task ConfirmRestoreBackupCommandReportsFailureWhenRestoreFails()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        instance.BackupDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "backups");
        var backupService = new FakeInstanceBackupService
        {
            RestoreException = new IOException("restore failed")
        };
        backupService.Backups.Add(CreateBackupRecord("Nightly", "nightly.zip"));
        var statusService = new FakeStatusService();
        var viewModel = CreateViewModel([instance], statusService: statusService, backupService: backupService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        OpenBackupSection(viewModel);

        viewModel.Details.Backup.RequestRestoreBackupCommand.Execute(viewModel.Details.Backup.VisibleBackups.Single());
        await viewModel.Details.Backup.ConfirmRestoreBackupCommand.ExecuteAsync(null);

        Assert.False(viewModel.Details.Backup.IsRestoringBackup);
        Assert.Equal(1, backupService.CreateBackupCallCount);
        Assert.Equal(1, backupService.RestoreBackupCallCount);
        Assert.True(viewModel.Details.Backup.RequestRestoreBackupCommand.CanExecute(viewModel.Details.Backup.VisibleBackups.First()));
        Assert.Equal(Strings.Status_BackupRestoreFailed, statusService.LastMessage);
    }

    [Fact]
    public async Task ConfirmCreateBackupCreatesBackupAndRefreshesList()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        instance.BackupDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "backups");
        var backupService = new FakeInstanceBackupService();
        var floatingMessageService = new FakeFloatingMessageService();
        var viewModel = CreateViewModel(
            [instance],
            backupService: backupService,
            floatingMessageService: floatingMessageService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        OpenBackupSection(viewModel);

        viewModel.Details.Backup.CreateBackupNowCommand.Execute(null);
        viewModel.Details.Backup.NewBackupName = "Nightly";
        await viewModel.Details.Backup.ConfirmCreateBackupDialogCommand.ExecuteAsync(null);

        Assert.False(viewModel.Details.Backup.IsCreateBackupDialogOpen);
        Assert.False(viewModel.Details.Backup.IsCreatingBackup);
        Assert.Equal(1, backupService.CreateBackupCallCount);
        Assert.Same(instance, backupService.LastCreatedBackupInstance);
        Assert.Equal(instance.BackupDirectory, backupService.LastCreatedBackupDirectory);
        Assert.Equal("Nightly", backupService.LastCreatedBackupName);
        Assert.Equal(
            string.Format(Strings.GameSettings_BackupInfoSummaryFormat, 1),
            viewModel.Details.Backup.BackupInfoText);
        Assert.Equal("Nightly", viewModel.Details.Backup.VisibleBackups.Single().Title);
        Assert.Equal("Nightly.zip · 1 MB", viewModel.Details.Backup.VisibleBackups.Single().Subtitle);
        Assert.Equal(Strings.Status_BackupCreated, floatingMessageService.LastMessage);
    }

    [Fact]
    public async Task ConfirmCreateBackupFailureOpensFailureDialogWithSummary()
    {
        var instance = CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla);
        instance.BackupDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "backups");
        var backupService = new FakeInstanceBackupService
        {
            CreateException = new InstanceBackupException(
                InstanceBackupFailureReason.BackupDirectoryInsideInstance,
                "Backup directory cannot be inside the instance directory.")
        };
        var viewModel = CreateViewModel([instance], backupService: backupService);

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        OpenBackupSection(viewModel);

        viewModel.Details.Backup.CreateBackupNowCommand.Execute(null);
        viewModel.Details.Backup.NewBackupName = "Bad";
        await viewModel.Details.Backup.ConfirmCreateBackupDialogCommand.ExecuteAsync(null);

        Assert.False(viewModel.Details.Backup.IsCreatingBackup);
        Assert.True(viewModel.Details.Backup.IsBackupFailureDialogOpen);
        Assert.Contains(Strings.BackupFailure_BackupDirectoryInsideInstance, viewModel.Details.Backup.BackupFailureDialogMessage);
        Assert.Contains(nameof(InstanceBackupException), viewModel.Details.Backup.BackupFailureDialogMessage);
    }

    [Fact]
    public async Task ConfirmEditInstanceDialogRenamesSelectedInstanceAndRaisesSyncEvent()
    {
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla));
        var statusService = new FakeStatusService();
        var viewModel = CreateViewModel(instanceService, new FakeGameVersionService([]), statusService, new FakeInstanceFolderService());
        var syncRequested = 0;
        GameSettingsInstancesChangedEventArgs? changeArgs = null;
        viewModel.InstancesChanged += args =>
        {
            syncRequested++;
            changeArgs = args;
        };

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
        Assert.Equal(GameSettingsInstancesChangedKind.Updated, changeArgs?.Kind);
        Assert.Equal(viewModel.SelectedInstance?.Instance.Id, changeArgs?.UpdatedInstance?.Id);
        Assert.Equal(viewModel.SelectedInstance?.Instance.Id, instanceService.LastRenamedInstanceId);
        Assert.Equal("Renamed World", instanceService.LastRenamedName);
        Assert.Equal("/Assets/Icons/block/diamond_block.png", instanceService.LastRenamedIconSource);
        Assert.Equal(string.Format(Strings.Status_InstanceRenamedFormat, "Renamed World"), statusService.LastMessage);
        Assert.False(viewModel.EditDialog.IsEditInstanceDialogOpen);
        Assert.True(viewModel.EditDialog.IsEditInstanceSuccessful);
    }

    [Theory]
    [InlineData("..")]
    [InlineData(".dfg")]
    [InlineData("name.")]
    public async Task ConfirmEditInstanceDialogRejectsUnsafeInstanceName(string unsafeName)
    {
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla));
        var viewModel = CreateViewModel(instanceService, new FakeGameVersionService([]), new FakeStatusService(), new FakeInstanceFolderService());

        await viewModel.EnsureInstancesLoadedAsync();
        viewModel.SelectInstanceCommand.Execute(viewModel.VisibleInstances.Single());
        viewModel.Details.RequestEditInstanceCommand.Execute(null);
        viewModel.EditDialog.InstanceName = unsafeName;

        await viewModel.ConfirmEditInstanceDialogCommand.ExecuteAsync(null);

        Assert.True(viewModel.EditDialog.IsEditInstanceDialogOpen);
        Assert.True(viewModel.EditDialog.IsEditInstanceInputStep);
        Assert.True(viewModel.EditDialog.IsInstanceNameInvalid);
        Assert.Null(instanceService.LastRenamedInstanceId);
        Assert.Equal("Vanilla World", viewModel.SelectedInstance?.Name);
    }

    [Fact]
    public void ExportCanExportRequiresFieldsAndSelectedInstance()
    {
        var instance = CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric);
        var viewModel = CreateViewModel([instance], modpackExportService: new FakeModpackExportService());
        viewModel.ShowInstanceDetails(instance, "export");
        var export = viewModel.Details.Export;

        Assert.False(export.CanExport);
        Assert.True(export.IsExportModpackNameEmpty);

        export.ExportModpackName = "Pack";

        Assert.True(export.CanExport);
        Assert.False(export.IsExportModpackNameEmpty);

        export.SelectedExportTypeOption = export.ExportTypeOptions.Single(option => option.Id == "modrinth");

        Assert.True(export.CanExport);
    }

    [Fact]
    public void ExportDisabledModsToggleDependsOnExportModsToggle()
    {
        var instance = CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric);
        var viewModel = CreateViewModel([instance], modpackExportService: new FakeModpackExportService());
        viewModel.ShowInstanceDetails(instance, "export");
        var export = viewModel.Details.Export;

        Assert.True(export.PackMods);
        Assert.True(export.CanPackDisabledMods);
        Assert.False(export.PackDisabledMods);

        export.PackDisabledMods = true;
        export.PackMods = false;

        Assert.False(export.PackMods);
        Assert.False(export.CanPackDisabledMods);
        Assert.False(export.PackDisabledMods);

        export.PackMods = true;

        Assert.True(export.CanPackDisabledMods);
        Assert.False(export.PackDisabledMods);
    }

    [Fact]
    public async Task ExportCommandPicksArchiveAndCallsExportService()
    {
        var instance = CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric);
        var outputPath = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "pack.zip");
        var filePickerService = new FakeFilePickerService { ModpackExportArchivePath = outputPath };
        var floatingMessageService = new FakeFloatingMessageService();
        var statusService = new FakeStatusService();
        var exportService = new FakeModpackExportService
        {
            Result = new ModpackExportResult(true, OutputArchivePath: outputPath)
        };
        var viewModel = CreateViewModel(
            [instance],
            statusService: statusService,
            filePickerService: filePickerService,
            floatingMessageService: floatingMessageService,
            modpackExportService: exportService);
        viewModel.ShowInstanceDetails(instance, "export");
        var export = viewModel.Details.Export;
        export.ExportModpackName = "Pack";
        export.PackDisabledMods = true;

        await export.ExportCommand.ExecuteAsync(null);

        Assert.Equal("Pack.zip", filePickerService.LastModpackExportArchiveDefaultFileName);
        Assert.Single(exportService.Requests);
        Assert.Equal(instance.Id, exportService.Requests[0].Instance.Id);
        Assert.Equal(outputPath, exportService.Requests[0].OutputArchivePath);
        Assert.Equal("Pack", exportService.Requests[0].Name);
        Assert.Equal(string.Empty, exportService.Requests[0].Author);
        Assert.Equal("1.0.0", exportService.Requests[0].Version);
        Assert.True(exportService.Requests[0].IncludeDisabledMods);
        Assert.Equal(Strings.Status_ModpackExported, floatingMessageService.LastMessage);
        Assert.Contains(outputPath, statusService.LastMessage);
    }

    [Fact]
    public async Task ExportCommandUsesModrinthKindAndExtensionWhenSelected()
    {
        var instance = CreateInstance("Fabric Pack", "1.20.1", LoaderKind.Fabric);
        var outputPath = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "pack.mrpack");
        var filePickerService = new FakeFilePickerService { ModpackExportArchivePath = outputPath };
        var exportService = new FakeModpackExportService
        {
            Result = new ModpackExportResult(true, OutputArchivePath: outputPath)
        };
        var viewModel = CreateViewModel(
            [instance],
            filePickerService: filePickerService,
            modpackExportService: exportService);
        viewModel.ShowInstanceDetails(instance, "export");
        var export = viewModel.Details.Export;
        export.ExportModpackName = "Pack";
        export.SelectedExportTypeOption = export.ExportTypeOptions.Single(option => option.Id == "modrinth");

        await export.ExportCommand.ExecuteAsync(null);

        Assert.Equal("Pack.mrpack", filePickerService.LastModpackExportArchiveDefaultFileName);
        Assert.Equal(ModpackExportKind.Modrinth, filePickerService.LastModpackExportArchiveKind);
        Assert.Single(exportService.Requests);
        Assert.Equal(ModpackExportKind.Modrinth, exportService.Requests[0].Kind);
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

    private static InstanceBackupSettingsViewModel OpenBackupSection(GameSettingsPageViewModel viewModel)
    {
        viewModel.SelectDetailsSectionCommand.Execute(GetDetailSection(viewModel, "backup"));
        return viewModel.Details.Backup;
    }

    private static void ExecuteResourceManagementOpenFolderCommand(
        GameSettingsPageViewModel viewModel,
        string sectionId)
    {
        switch (sectionId)
        {
            case "mod_management":
                viewModel.Details.ModManagement.OpenModFolderCommand.Execute(null);
                break;
            case "saves":
                viewModel.Details.SaveManagement.OpenSaveFolderCommand.Execute(null);
                break;
            case "resource_packs":
                viewModel.Details.ResourcePackManagement.OpenResourcePackFolderCommand.Execute(null);
                break;
            case "shaders":
                viewModel.Details.ShaderPackManagement.OpenShaderPackFolderCommand.Execute(null);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(sectionId), sectionId, null);
        }
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(
            fieldName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field not found: {fieldName}");
        return (T)field.GetValue(instance)!;
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
        ILocalModIconEnrichmentService? modIconEnrichmentService = null,
        ILocalSaveService? saveService = null,
        ILocalResourcePackService? resourcePackService = null,
        ILocalShaderPackService? shaderPackService = null,
        IInstanceBackupService? backupService = null,
        IModpackExportService? modpackExportService = null)
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
            modIconEnrichmentService,
            saveService ?? new FakeSaveService(),
            resourcePackService ?? new FakeResourcePackService(),
            shaderPackService ?? new FakeShaderPackService(),
            backupService ?? new FakeInstanceBackupService(),
            modpackExportService);
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
        ILocalModIconEnrichmentService? modIconEnrichmentService = null,
        ILocalSaveService? saveService = null,
        ILocalResourcePackService? resourcePackService = null,
        ILocalShaderPackService? shaderPackService = null,
        IInstanceBackupService? backupService = null,
        IModpackExportService? modpackExportService = null)
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
            backupService ?? new FakeInstanceBackupService(),
            new LocalModsViewModel(resolvedModService, statusService, iconEnrichmentService: modIconEnrichmentService),
            new LocalSavesViewModel(resolvedSaveService, statusService),
            new LocalResourcePacksViewModel(resolvedResourcePackService, statusService),
            new LocalShaderPacksViewModel(resolvedShaderPackService, statusService),
            javaRuntimeDiscoveryService ?? new FakeJavaRuntimeDiscoveryService(),
            filePickerService ?? new FakeFilePickerService(),
            floatingMessageService ?? new FakeFloatingMessageService(),
            modpackExportService: modpackExportService);
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

    private static InstanceBackupRecord CreateBackupRecord(string name, string fileName)
    {
        return new InstanceBackupRecord
        {
            Name = name,
            FileName = fileName,
            FullPath = Path.Combine(Path.GetTempPath(), "launcher-tests", fileName),
            SizeBytes = 1024 * 1024,
            CreatedAt = DateTimeOffset.UtcNow
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
        public bool OpenResult { get; init; } = true;
        public bool RevealResult { get; init; } = true;

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
            return OpenResult;
        }

        public bool TryRevealFile(string filePath)
        {
            LastRevealedFilePath = filePath;
            return RevealResult;
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
        public string? ModpackExportArchivePath { get; init; }
        public string? FolderPath { get; init; }
        public string? LastFolderPickerTitle { get; private set; }
        public string? LastFolderPickerInitialDirectory { get; private set; }
        public string? LastModpackExportArchiveDefaultFileName { get; private set; }
        public ModpackExportKind? LastModpackExportArchiveKind { get; private set; }

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

        public string? PickModpackExportArchive(string defaultFileName, ModpackExportKind kind)
        {
            LastModpackExportArchiveDefaultFileName = defaultFileName;
            LastModpackExportArchiveKind = kind;
            return ModpackExportArchivePath;
        }

        public string? PickLocalImportFile()
        {
            return null;
        }

        public string? PickFolder(string title, string? initialDirectory = null)
        {
            LastFolderPickerTitle = title;
            LastFolderPickerInitialDirectory = initialDirectory;
            return FolderPath;
        }
    }

    private sealed class FakeInstanceBackupService : IInstanceBackupService
    {
        public List<InstanceBackupRecord> Backups { get; } = [];
        public List<string> Operations { get; } = [];
        public Exception? CreateException { get; init; }
        public Exception? DeleteException { get; init; }
        public Exception? RestoreException { get; init; }
        public string? LastEnsuredDirectory { get; private set; }
        public string? LastCountedDirectory { get; private set; }
        public string? LastGetBackupsDirectory { get; private set; }
        public GameInstance? LastCreatedBackupInstance { get; private set; }
        public string? LastCreatedBackupDirectory { get; private set; }
        public string? LastCreatedBackupName { get; private set; }
        public string? LastDeletedBackupDirectory { get; private set; }
        public string? LastDeletedBackupFullPath { get; private set; }
        public GameInstance? LastRestoredBackupInstance { get; private set; }
        public string? LastRestoredBackupDirectory { get; private set; }
        public string? LastRestoredBackupFullPath { get; private set; }
        public int CreateBackupCallCount { get; private set; }
        public int DeleteBackupCallCount { get; private set; }
        public int RestoreBackupCallCount { get; private set; }

        public Task<string> EnsureBackupDirectoryAsync(string backupDirectory, CancellationToken cancellationToken = default)
        {
            LastEnsuredDirectory = Path.GetFullPath(backupDirectory);
            return Task.FromResult(LastEnsuredDirectory);
        }

        public Task<int> CountBackupEntriesAsync(string backupDirectory, CancellationToken cancellationToken = default)
        {
            LastCountedDirectory = backupDirectory;
            return Task.FromResult(Backups.Count);
        }

        public Task<IReadOnlyList<InstanceBackupRecord>> GetBackupsAsync(
            string backupDirectory,
            CancellationToken cancellationToken = default)
        {
            LastGetBackupsDirectory = backupDirectory;
            return Task.FromResult<IReadOnlyList<InstanceBackupRecord>>(Backups.Select(CloneBackup).ToArray());
        }

        public Task<InstanceBackupRecord> CreateBackupAsync(
            GameInstance instance,
            string backupDirectory,
            string backupName,
            CancellationToken cancellationToken = default)
        {
            CreateBackupCallCount++;
            LastCreatedBackupInstance = instance;
            LastCreatedBackupDirectory = backupDirectory;
            LastCreatedBackupName = backupName;
            Operations.Add($"create:{backupName}");

            if (CreateException is not null)
                throw CreateException;

            var record = new InstanceBackupRecord
            {
                Name = backupName,
                FileName = $"{backupName}.zip",
                FullPath = Path.Combine(backupDirectory, $"{backupName}.zip"),
                SizeBytes = 1024 * 1024,
                CreatedAt = DateTimeOffset.UtcNow
            };
            Backups.Add(CloneBackup(record));
            return Task.FromResult(CloneBackup(record));
        }

        public Task DeleteBackupAsync(
            string backupDirectory,
            string backupFullPath,
            CancellationToken cancellationToken = default)
        {
            DeleteBackupCallCount++;
            LastDeletedBackupDirectory = backupDirectory;
            LastDeletedBackupFullPath = backupFullPath;

            if (DeleteException is not null)
                throw DeleteException;

            Backups.RemoveAll(backup => string.Equals(backup.FullPath, backupFullPath, StringComparison.OrdinalIgnoreCase));
            return Task.CompletedTask;
        }

        public Task RestoreBackupAsync(
            GameInstance instance,
            string backupDirectory,
            string backupFullPath,
            CancellationToken cancellationToken = default)
        {
            RestoreBackupCallCount++;
            LastRestoredBackupInstance = instance;
            LastRestoredBackupDirectory = backupDirectory;
            LastRestoredBackupFullPath = backupFullPath;
            Operations.Add($"restore:{backupFullPath}");

            if (RestoreException is not null)
                throw RestoreException;

            return Task.CompletedTask;
        }

        private static InstanceBackupRecord CloneBackup(InstanceBackupRecord backup)
        {
            return new InstanceBackupRecord
            {
                Name = backup.Name,
                FileName = backup.FileName,
                FullPath = backup.FullPath,
                SizeBytes = backup.SizeBytes,
                CreatedAt = backup.CreatedAt
            };
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

    private sealed class FakeModpackExportService : IModpackExportService
    {
        public List<ModpackExportRequest> Requests { get; } = [];

        public ModpackExportResult Result { get; init; } = new(true, OutputArchivePath: "export.zip");

        public Task<ModpackExportResult> ExportAsync(
            ModpackExportRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(Result);
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

        public int SetEnabledCallCount { get; private set; }

        public List<string> ImportedPaths { get; } = [];

        public HashSet<string> SetEnabledFailures { get; } = new(StringComparer.OrdinalIgnoreCase);

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
            SetEnabledCallCount++;
            if (SetEnabledFailures.Contains(mod.FullPath))
                throw new InvalidOperationException($"Failed to change mod enabled state: {mod.FullPath}");

            foreach (var pair in ModsByInstanceId)
            {
                var index = pair.Value.FindIndex(candidate =>
                    string.Equals(candidate.FullPath, mod.FullPath, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                    continue;

                var updated = CloneLocalMod(pair.Value[index]);
                updated.IsEnabled = enabled;
                updated.FullPath = GetPathForEnabledState(updated.FullPath, enabled);
                updated.FileName = Path.GetFileName(updated.FullPath);
                pair.Value[index] = updated;
                return Task.CompletedTask;
            }

            throw new InvalidOperationException($"Mod not found: {mod.FullPath}");
        }

        private static string GetPathForEnabledState(string path, bool enabled)
        {
            return enabled
                ? path.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)
                    ? path[..^".disabled".Length]
                    : path
                : path.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)
                    ? path
                    : path + ".disabled";
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

    private sealed class FakeLocalModIconEnrichmentService : ILocalModIconEnrichmentService
    {
        public Dictionary<string, string> CachedIcons { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> ResolvedIcons { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> ProgressIcons { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public TaskCompletionSource<bool>? ResolveBlocker { get; init; }

        public int ResolveCachedCallCount { get; private set; }

        public int ResolveCallCount { get; private set; }

        public IReadOnlyList<LocalMod> LastCachedRequestedMods { get; private set; } = [];

        public IReadOnlyList<LocalMod> LastRequestedMods { get; private set; } = [];

        public Task<IReadOnlyDictionary<string, string>> ResolveCachedIconSourcesAsync(
            IReadOnlyList<LocalMod> mods,
            CancellationToken cancellationToken = default)
        {
            ResolveCachedCallCount++;
            LastCachedRequestedMods = mods.Select(CloneLocalMod).ToArray();
            return Task.FromResult(FilterIcons(CachedIcons, mods));
        }

        public async Task<IReadOnlyDictionary<string, string>> ResolveMissingIconSourcesAsync(
            IReadOnlyList<LocalMod> mods,
            CancellationToken cancellationToken = default,
            IProgress<IReadOnlyDictionary<string, string>>? progress = null)
        {
            ResolveCallCount++;
            LastRequestedMods = mods.Select(CloneLocalMod).ToArray();
            var progressIcons = FilterIcons(ProgressIcons, mods);
            if (progressIcons.Count > 0)
                progress?.Report(progressIcons);

            if (ResolveBlocker is not null)
                await ResolveBlocker.Task.WaitAsync(cancellationToken);

            return FilterIcons(ResolvedIcons, mods);
        }

        private static IReadOnlyDictionary<string, string> FilterIcons(
            IReadOnlyDictionary<string, string> icons,
            IReadOnlyList<LocalMod> mods)
        {
            return new Dictionary<string, string>(
                icons.Where(pair => mods.Any(mod =>
                    string.Equals(mod.FullPath, pair.Key, StringComparison.OrdinalIgnoreCase))),
                StringComparer.OrdinalIgnoreCase);
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

        public Task<IReadOnlyList<GameInstance>> GetStoredInstancesAsync(
            LauncherSettings settings,
            CancellationToken cancellationToken = default)
        {
            throw exception;
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
            bool installFabricApi = true,
            string? fabricApiVersionId = null,
            string? quiltStandardLibraryVersionId = null)
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
