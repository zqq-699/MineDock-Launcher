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

using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Download;
using Launcher.Application.Services;
using Launcher.Domain.Models;

using System.IO;

namespace Launcher.Tests.Download;

public sealed class DownloadPageViewModelTests
{

    [Fact]
    public async Task DownloadPageShowsOnlyReleaseVersionsForReleaseCategory()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.21.4", "Release", false),
            new MinecraftVersionInfo("24w45a", "Snapshot", false),
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var viewModel = CreateDownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();

        Assert.True(viewModel.HasVisibleVersions);
        Assert.Equal(["1.21.4", "1.20.1"], viewModel.VisibleVersions.Select(version => version.Name));
        Assert.False(viewModel.HasVersionEmptyMessage);
    }

    [Fact]
    public void DownloadPageLocalImportCancelClosesDialogAndClearsSelection()
    {
        var tempFilePath = CreateTempModpackFile(".mrpack");

        try
        {
            var viewModel = CreateDownloadPageViewModel(new FakeGameVersionService([]));

            viewModel.LocalImportDialog.Open();
            Assert.True(viewModel.LocalImportDialog.ApplyDroppedFiles([tempFilePath]));

            viewModel.LocalImportDialog.CancelCommand.Execute(null);

            Assert.False(viewModel.LocalImportDialog.IsOpen);
            Assert.False(viewModel.LocalImportDialog.HasSelectedFile);
            Assert.Empty(viewModel.LocalImportDialog.SelectedFilePath);
            Assert.Empty(viewModel.LocalImportDialog.SelectedFileName);
        }
        finally
        {
            File.Delete(tempFilePath);
        }
    }

    [Fact]
    public async Task DownloadPageLocalImportConfirmImportsSelectedModpackAndRaisesInstanceInstalled()
    {
        var tempFilePath = CreateTempModpackFile(".mrpack");

        try
        {
            var tasksPage = new DownloadTasksPageViewModel();
            var importedInstance = new GameInstance
            {
                Id = "imported",
                Name = "Imported Pack",
                VersionName = "Imported Pack",
                MinecraftVersion = "1.20.1",
                Loader = LoaderKind.Fabric,
                LoaderVersion = "0.16.10",
                InstanceDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"))
            };
            var localModpackImportService = new FakeLocalModpackImportService
            {
                RecognitionResultToReturn = ModpackRecognitionResult.Success(),
                ResultToReturn = ModpackImportResult.Success(importedInstance)
            };
            var floatingMessageService = new FakeFloatingMessageService();
            var viewModel = CreateDownloadPageViewModel(
                new FakeGameVersionService([]),
                tasksPage: tasksPage,
                localModpackImportService: localModpackImportService,
                floatingMessageService: floatingMessageService);
            GameInstance? installedInstance = null;
            viewModel.InstanceInstalled += (_, instance) => installedInstance = instance;

            viewModel.LocalImportDialog.Open();
            Assert.True(viewModel.LocalImportDialog.ApplyDroppedFiles([tempFilePath]));
            Assert.True(viewModel.LocalImportDialog.ConfirmImportCommand.CanExecute(null));

            await viewModel.LocalImportDialog.ConfirmImportCommand.ExecuteAsync(null);

            Assert.False(viewModel.LocalImportDialog.IsOpen);
            Assert.False(viewModel.LocalImportDialog.HasSelectedFile);
            Assert.Equal(Path.GetFullPath(tempFilePath), localModpackImportService.LastArchivePath);
            Assert.Same(importedInstance, installedInstance);
            Assert.Equal(Strings.Status_ModpackInstalling, floatingMessageService.LastMessage);
            var task = Assert.Single(tasksPage.Tasks);
            Assert.Equal(DownloadTaskState.Completed, task.State);
            Assert.Equal(string.Format(Strings.Status_ModpackImportedFormat, importedInstance.Name), task.StatusMessage);
        }
        finally
        {
            File.Delete(tempFilePath);
        }
    }

    [Fact]
    public async Task DownloadPageLocalImportShowsManualDownloadsDialogWhenSomeFilesNeedManualRetry()
    {
        var tempFilePath = CreateTempModpackFile(".zip");
        var instanceDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(instanceDirectory);
        File.WriteAllText(Path.Combine(instanceDirectory, ModpackManualDownloads.FileName), "stub");

        try
        {
            var tasksPage = new DownloadTasksPageViewModel();
            var instanceFolderService = new FakeInstanceFolderService();
            var importedInstance = new GameInstance
            {
                Id = "imported",
                Name = "Imported Pack",
                VersionName = "Imported Pack",
                MinecraftVersion = "1.20.1",
                Loader = LoaderKind.Forge,
                LoaderVersion = "47.4.20",
                InstanceDirectory = instanceDirectory
            };
            var localModpackImportService = new FakeLocalModpackImportService
            {
                RecognitionResultToReturn = ModpackRecognitionResult.Success(),
                ResultToReturn = ModpackImportResult.Success(
                    importedInstance,
                    [
                        new ManualModpackDownload
                        {
                            ProjectId = 348025,
                            FileId = 4436467,
                            FileName = "SRParasites-1.12.2v1.9.11.jar",
                            DisplayName = "SRP v 1.9.11",
                            SuggestedUrl = "https://edge.forgecdn.net/files/4436/467/SRParasites-1.12.2v1.9.11.jar",
                            FailureSummary = "http_403"
                        }
                    ])
            };
            var viewModel = CreateDownloadPageViewModel(
                new FakeGameVersionService([]),
                tasksPage: tasksPage,
                localModpackImportService: localModpackImportService,
                instanceFolderService: instanceFolderService);

            viewModel.LocalImportDialog.Open();
            Assert.True(viewModel.LocalImportDialog.ApplyDroppedFiles([tempFilePath]));

            await viewModel.LocalImportDialog.ConfirmImportCommand.ExecuteAsync(null);

            var task = Assert.Single(tasksPage.Tasks);
            Assert.Equal(DownloadTaskState.Completed, task.State);
            Assert.Equal(
                string.Format(Strings.Status_ModpackImportedWithManualDownloadsFormat, importedInstance.Name),
                task.StatusMessage);
            Assert.True(viewModel.ModpackManualDownloadsDialog.IsOpen);
            Assert.Single(viewModel.ModpackManualDownloadsDialog.Files);

            viewModel.ModpackManualDownloadsDialog.OpenFileCommand.Execute(null);
            Assert.Equal(Path.Combine(instanceDirectory, ModpackManualDownloads.FileName), instanceFolderService.LastRevealedFilePath);

            viewModel.ModpackManualDownloadsDialog.CloseCommand.Execute(null);
            Assert.False(viewModel.ModpackManualDownloadsDialog.IsOpen);
        }
        finally
        {
            File.Delete(tempFilePath);
            Directory.Delete(instanceDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadPageLoadsVersionsUsingPrimedDownloadSourcePreference()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.21.4", "Release", false)
        ]);
        var viewModel = CreateDownloadPageViewModel(service);
        viewModel.PrimeFromSettings(new LauncherSettings
        {
            DownloadSourcePreference = DownloadSourcePreference.BmclApi
        });

        await viewModel.EnsureVersionsLoadedAsync();

        Assert.Equal(DownloadSourcePreference.BmclApi, service.LastDownloadSourcePreference);
    }

    [Fact]
    public async Task DownloadPageShowsOnlySnapshotVersionsForSnapshotCategory()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.21.4", "Release", false),
            new MinecraftVersionInfo("24w45a", "Snapshot", false, new DateTimeOffset(2024, 10, 30, 0, 0, 0, TimeSpan.Zero)),
            new MinecraftVersionInfo("24w44a", "Snapshot", false, new DateTimeOffset(2024, 11, 06, 0, 0, 0, TimeSpan.Zero))
        ]);
        var viewModel = CreateDownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();
        viewModel.SelectVersionCategoryCommand.Execute(viewModel.VersionCategories.Single(category => category.Id == "snapshot"));

        Assert.True(viewModel.HasVisibleVersions);
        Assert.Equal(["24w44a", "24w45a"], viewModel.VisibleVersions.Select(version => version.Name));
        Assert.All(viewModel.VisibleVersions, version =>
        {
            Assert.True(version.IsSnapshot);
            Assert.Equal("\u5feb\u7167\u7248", version.TypeLabel);
            Assert.Equal("/Assets/Icons/block/dirt_block.png", version.IconSource);
        });
    }

    [Fact]
    public async Task DownloadPageCannotEnterInstanceOptionsWithoutSelectedVersion()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var viewModel = CreateDownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();

        Assert.Equal(DownloadPageStep.VersionList, viewModel.CurrentStep);
        Assert.True(viewModel.IsVersionListStep);
        Assert.False(viewModel.GoToInstanceOptionsCommand.CanExecute(null));
    }

    [Fact]
    public async Task DownloadPageEntersInstanceOptionsWithSelectedReleaseDefaults()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var viewModel = CreateDownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();
        var version = viewModel.VisibleVersions.Single();
        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(version);

        Assert.Equal(DownloadPageStep.InstanceOptions, viewModel.CurrentStep);
        Assert.True(viewModel.IsInstanceOptionsStep);
        Assert.Equal("1.20.1", viewModel.InstanceName);
        Assert.Equal("1.20.1", viewModel.PageTitle);
        Assert.Equal("/Assets/Icons/block/grass_block.png", viewModel.PageTitleIconSource);
        Assert.Equal([LoaderKind.Vanilla, LoaderKind.Fabric, LoaderKind.Forge, LoaderKind.NeoForge, LoaderKind.Quilt], viewModel.LoaderOptions.Select(option => option.Kind));
        Assert.Equal(LoaderKind.Vanilla, viewModel.SelectedLoaderOption?.Kind);
        Assert.True(viewModel.LoaderOptions.Single(option => option.Kind == LoaderKind.Vanilla).IsSelected);
        Assert.Equal("/Assets/Icons/block/grass_block.png", viewModel.LoaderOptions.Single(option => option.Kind == LoaderKind.Vanilla).IconSource);
        Assert.Equal("/Assets/Icons/block/fabric.png", viewModel.LoaderOptions.Single(option => option.Kind == LoaderKind.Fabric).IconSource);
        Assert.Equal("/Assets/Icons/block/Anvil.png", viewModel.LoaderOptions.Single(option => option.Kind == LoaderKind.Forge).IconSource);
        Assert.Equal("/Assets/Icons/block/neo_logo.png", viewModel.LoaderOptions.Single(option => option.Kind == LoaderKind.NeoForge).IconSource);
        Assert.Equal("/Assets/Icons/block/quilt_x16.png", viewModel.LoaderOptions.Single(option => option.Kind == LoaderKind.Quilt).IconSource);
    }

    [Fact]
    public async Task DownloadPageLoadsLoaderVersionsUsingDownloadSourcePreference()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var fabricProvider = new FakeLoaderProvider
        {
            Kind = LoaderKind.Fabric,
            LoaderVersions = [new LoaderVersionInfo("0.16.10")]
        };
        var viewModel = CreateDownloadPageViewModel(
            service,
            loaderProviders: CreateLoaderProviders(fabricProvider));
        viewModel.PrimeFromSettings(new LauncherSettings
        {
            DownloadSourcePreference = DownloadSourcePreference.BmclApi,
            DownloadSpeedLimitMbPerSecond = 16
        });

        await viewModel.EnsureVersionsLoadedAsync();
        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(viewModel.VisibleVersions.Single());
        viewModel.SelectLoaderOptionCommand.Execute(viewModel.LoaderOptions.Single(option => option.Kind == LoaderKind.Fabric));
        await TestAsync.WaitForAsync(() => viewModel.HasLoaderVersions);

        Assert.Equal(DownloadSourcePreference.BmclApi, fabricProvider.LastDownloadSourcePreference);
        Assert.Equal(16, fabricProvider.LastDownloadSpeedLimitMbPerSecond);
    }

    [Fact]
    public async Task DownloadPageInstallCommandRequiresSelectedVanillaVersion()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var viewModel = CreateDownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();

        Assert.False(viewModel.InstallCommand.CanExecute(null));

        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(viewModel.VisibleVersions.Single());

        Assert.True(viewModel.InstallCommand.CanExecute(null));

        viewModel.IsInstalling = true;
        Assert.True(viewModel.InstallCommand.CanExecute(null));

        viewModel.IsInstalling = false;
        var fabric = viewModel.LoaderOptions.Single(option => option.Kind == LoaderKind.Fabric);
        viewModel.SelectLoaderOptionCommand.Execute(fabric);
        await TestAsync.WaitForAsync(() => viewModel.HasLoaderVersions);

        Assert.False(viewModel.InstallCommand.CanExecute(null));
        Assert.False(viewModel.HasInstallStatus);
    }

    [Fact]
    public async Task DownloadPageFabricInstallPassesSelectedLoaderVersion()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.2", "Release", false)
        ]);
        var instanceService = new FakeGameInstanceService();
        var viewModel = CreateDownloadPageViewModel(service, instanceService);

        await viewModel.EnsureVersionsLoadedAsync();
        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(viewModel.VisibleVersions.Single());
        viewModel.SelectLoaderOptionCommand.Execute(viewModel.LoaderOptions.Single(option => option.Kind == LoaderKind.Fabric));
        await TestAsync.WaitForAsync(() => viewModel.HasLoaderVersions);
        viewModel.SelectedLoaderVersion = viewModel.LoaderVersions.Single(version => version.Version == "0.16.10");
        await viewModel.InstallCommand.ExecuteAsync(null);

        Assert.Equal("1.20.2", instanceService.LastMinecraftVersion);
        Assert.Equal(LoaderKind.Fabric, instanceService.LastLoader);
        Assert.Equal("0.16.10", instanceService.LastLoaderVersion);
        Assert.Equal("1.20.2-fabric-0.16.10", instanceService.LastName);
        Assert.Null(instanceService.LastFabricApiVersionId);
    }

    [Fact]
    public async Task DownloadPageFabricLoadsApiVersionsAndSelectsLatest()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.2", "Release", false)
        ]);
        var modrinthService = new FakeModrinthService
        {
            FabricApiVersions =
            [
                new ModrinthVersionInfo
                {
                    VersionId = "fabric-api-new",
                    Name = "Fabric API 0.92.2",
                    VersionNumber = "0.92.2+1.20.2",
                    IsStable = true
                },
                new ModrinthVersionInfo
                {
                    VersionId = "fabric-api-old",
                    Name = "Fabric API 0.91.0",
                    VersionNumber = "0.91.0+1.20.2",
                    IsStable = false
                }
            ]
        };
        var viewModel = CreateDownloadPageViewModel(service, modrinthService: modrinthService);

        await viewModel.EnsureVersionsLoadedAsync();
        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(viewModel.VisibleVersions.Single());
        viewModel.SelectLoaderOptionCommand.Execute(viewModel.LoaderOptions.Single(option => option.Kind == LoaderKind.Fabric));

        await TestAsync.WaitForAsync(() => viewModel.SelectedAddonLibraryVersion?.VersionId == "fabric-api-new");

        Assert.True(viewModel.ShouldShowAddonLibrarySelector);
        Assert.Equal(Strings.Download_FabricApiLabel, viewModel.AddonLibraryLabelText);
        Assert.Equal("1.20.2", modrinthService.LastFabricApiMinecraftVersion);
        Assert.Equal(Strings.Download_AddonLibraryNone, viewModel.AddonLibraryVersions[0].Title);
        Assert.False(viewModel.AddonLibraryVersions[0].IsInstallable);
        Assert.Equal("fabric-api-new", viewModel.AddonLibraryVersions[1].VersionId);
        Assert.True(viewModel.AddonLibraryVersions[1].IsLatest);
        Assert.Equal("fabric-api-new", viewModel.SelectedAddonLibraryVersion?.VersionId);
    }

    [Fact]
    public async Task DownloadPageFabricInstallPassesSelectedApiVersion()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.2", "Release", false)
        ]);
        var instanceService = new FakeGameInstanceService();
        var modrinthService = new FakeModrinthService
        {
            FabricApiVersions =
            [
                new ModrinthVersionInfo
                {
                    VersionId = "fabric-api-new",
                    Name = "Fabric API 0.92.2",
                    VersionNumber = "0.92.2+1.20.2",
                    IsStable = true
                }
            ]
        };
        var viewModel = CreateDownloadPageViewModel(service, instanceService, modrinthService: modrinthService);

        await viewModel.EnsureVersionsLoadedAsync();
        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(viewModel.VisibleVersions.Single());
        viewModel.SelectLoaderOptionCommand.Execute(viewModel.LoaderOptions.Single(option => option.Kind == LoaderKind.Fabric));
        await TestAsync.WaitForAsync(() => viewModel.SelectedAddonLibraryVersion?.VersionId == "fabric-api-new");
        await TestAsync.WaitForAsync(() => viewModel.HasLoaderVersions);
        viewModel.SelectedLoaderVersion = viewModel.LoaderVersions.Single(version => version.Version == "0.16.10");

        await viewModel.InstallCommand.ExecuteAsync(null);

        Assert.True(instanceService.LastInstallFabricApi);
        Assert.Equal("fabric-api-new", instanceService.LastFabricApiVersionId);
    }

    [Fact]
    public async Task DownloadPageForgeInstallPassesSelectedLoaderVersion()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var instanceService = new FakeGameInstanceService();
        var viewModel = CreateDownloadPageViewModel(service, instanceService);

        await viewModel.EnsureVersionsLoadedAsync();
        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(viewModel.VisibleVersions.Single());
        viewModel.SelectLoaderOptionCommand.Execute(viewModel.LoaderOptions.Single(option => option.Kind == LoaderKind.Forge));
        await TestAsync.WaitForAsync(() => viewModel.HasLoaderVersions);
        viewModel.SelectedLoaderVersion = viewModel.LoaderVersions.Single(version => version.Version == "47.4.20");
        await viewModel.InstallCommand.ExecuteAsync(null);

        Assert.Equal("1.20.1", instanceService.LastMinecraftVersion);
        Assert.Equal(LoaderKind.Forge, instanceService.LastLoader);
        Assert.Equal("47.4.20", instanceService.LastLoaderVersion);
        Assert.Equal("1.20.1-forge-47.4.20", instanceService.LastName);
    }

    [Fact]
    public async Task DownloadPageQuiltInstallPassesSelectedLoaderVersion()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.2", "Release", false)
        ]);
        var instanceService = new FakeGameInstanceService();
        var viewModel = CreateDownloadPageViewModel(service, instanceService);

        await viewModel.EnsureVersionsLoadedAsync();
        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(viewModel.VisibleVersions.Single());
        viewModel.SelectLoaderOptionCommand.Execute(viewModel.LoaderOptions.Single(option => option.Kind == LoaderKind.Quilt));
        await TestAsync.WaitForAsync(() => viewModel.HasLoaderVersions);
        viewModel.SelectedLoaderVersion = viewModel.LoaderVersions.Single(version => version.Version == "0.29.2");
        await viewModel.InstallCommand.ExecuteAsync(null);

        Assert.Equal("1.20.2", instanceService.LastMinecraftVersion);
        Assert.Equal(LoaderKind.Quilt, instanceService.LastLoader);
        Assert.Equal("0.29.2", instanceService.LastLoaderVersion);
        Assert.Equal("1.20.2-quilt-0.29.2", instanceService.LastName);
    }

    [Fact]
    public async Task DownloadPageQuiltInstallPassesSelectedLibraryVersion()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.2", "Release", false)
        ]);
        var instanceService = new FakeGameInstanceService();
        var modrinthService = new FakeModrinthService
        {
            QuiltStandardLibraryVersions =
            [
                new ModrinthVersionInfo
                {
                    VersionId = "qsl-new",
                    Name = "QFAPI / QSL 8.0.0",
                    VersionNumber = "8.0.0",
                    IsStable = true
                }
            ]
        };
        var viewModel = CreateDownloadPageViewModel(service, instanceService, modrinthService: modrinthService);

        await viewModel.EnsureVersionsLoadedAsync();
        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(viewModel.VisibleVersions.Single());
        viewModel.SelectLoaderOptionCommand.Execute(viewModel.LoaderOptions.Single(option => option.Kind == LoaderKind.Quilt));
        await TestAsync.WaitForAsync(() => viewModel.SelectedQuiltLibraryVersion?.VersionId == "qsl-new");
        await TestAsync.WaitForAsync(() => viewModel.HasLoaderVersions);
        viewModel.SelectedLoaderVersion = viewModel.LoaderVersions.Single(version => version.Version == "0.29.2");

        await viewModel.InstallCommand.ExecuteAsync(null);

        Assert.Equal("qsl-new", instanceService.LastQuiltStandardLibraryVersionId);
    }

    [Fact]
    public async Task DownloadPageInstallCreatesVanillaInstance()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var instanceService = new FakeGameInstanceService();
        var tasksPage = new DownloadTasksPageViewModel();
        var viewModel = CreateDownloadPageViewModel(service, instanceService, tasksPage);
        GameInstance? installedInstance = null;
        viewModel.InstanceInstalled += (_, instance) => installedInstance = instance;

        await viewModel.EnsureVersionsLoadedAsync();
        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(viewModel.VisibleVersions.Single());
        viewModel.InstanceName = "My Vanilla";
        await viewModel.InstallCommand.ExecuteAsync(null);

        Assert.Equal("1.20.1", instanceService.LastMinecraftVersion);
        Assert.Equal(LoaderKind.Vanilla, instanceService.LastLoader);
        Assert.Null(instanceService.LastLoaderVersion);
        Assert.Equal("My Vanilla", instanceService.LastName);
        Assert.False(viewModel.IsInstalling);
        Assert.False(viewModel.HasInstallError);
        Assert.Equal(100, viewModel.InstallProgressPercent);
        Assert.Same(instanceService.CreatedInstances.Single(), installedInstance);
        Assert.Contains("\u5df2\u5b89\u88c5", viewModel.InstallStatusMessage);

        var task = Assert.Single(tasksPage.Tasks);
        Assert.True(tasksPage.HasTasks);
        Assert.Equal(DownloadTaskState.Completed, task.State);
        Assert.Equal(100, task.ProgressPercent);
        Assert.Contains("1.20.1", task.Title);
        Assert.Equal(DownloadPageStep.VersionList, viewModel.CurrentStep);
    }

    [Fact]
    public async Task DownloadPageInstallPassesDownloadSourcePreferenceToInstanceService()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var instanceService = new FakeGameInstanceService();
        var viewModel = CreateDownloadPageViewModel(service, instanceService);
        viewModel.PrimeFromSettings(new LauncherSettings
        {
            DownloadSourcePreference = DownloadSourcePreference.Official
        });

        await viewModel.EnsureVersionsLoadedAsync();
        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(viewModel.VisibleVersions.Single());
        await viewModel.InstallCommand.ExecuteAsync(null);

        Assert.Equal(DownloadSourcePreference.Official, instanceService.LastDownloadSourcePreference);
    }

    [Fact]
    public async Task DownloadPageInstallPassesDownloadSpeedLimitToInstanceService()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var instanceService = new FakeGameInstanceService();
        var viewModel = CreateDownloadPageViewModel(service, instanceService);
        viewModel.PrimeFromSettings(new LauncherSettings
        {
            DownloadSpeedLimitMbPerSecond = 64
        });

        await viewModel.EnsureVersionsLoadedAsync();
        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(viewModel.VisibleVersions.Single());
        await viewModel.InstallCommand.ExecuteAsync(null);

        Assert.Equal(64, instanceService.LastDownloadSpeedLimitMbPerSecond);
    }

    [Fact]
    public async Task DownloadPageCancelInstallTaskStopsInstallAndRemovesTask()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var allowCreate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var instanceService = new FakeGameInstanceService { WaitBeforeCreate = allowCreate.Task };
        var tasksPage = new DownloadTasksPageViewModel();
        var viewModel = CreateDownloadPageViewModel(service, instanceService, tasksPage);

        await viewModel.EnsureVersionsLoadedAsync();
        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(viewModel.VisibleVersions.Single());

        var installTask = viewModel.InstallCommand.ExecuteAsync(null);
        await instanceService.CreateStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var taskItem = Assert.Single(tasksPage.Tasks);

        tasksPage.CancelTaskCommand.Execute(taskItem);
        await installTask;

        Assert.True(taskItem.IsCancellationRequested);
        Assert.Empty(tasksPage.Tasks);
        Assert.Empty(instanceService.CreatedInstances);
        Assert.False(viewModel.IsInstalling);
        Assert.False(viewModel.HasInstallError);
        Assert.Empty(viewModel.InstallStatusMessage);
    }

    [Fact]
    public async Task DownloadPageAllowsConcurrentInstalls()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false),
            new MinecraftVersionInfo("1.20.2", "Release", false)
        ]);
        var allowCreate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var instanceService = new FakeGameInstanceService { WaitBeforeCreate = allowCreate.Task };
        var tasksPage = new DownloadTasksPageViewModel();
        var viewModel = CreateDownloadPageViewModel(service, instanceService, tasksPage);

        await viewModel.EnsureVersionsLoadedAsync();

        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(viewModel.VisibleVersions[0]);
        viewModel.InstanceName = "First Install";
        var firstInstall = viewModel.InstallCommand.ExecuteAsync(null);
        await TestAsync.WaitForAsync(() => instanceService.CreateCallCount == 1);

        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(viewModel.VisibleVersions[1]);
        viewModel.InstanceName = "Second Install";

        Assert.True(viewModel.InstallCommand.CanExecute(null));

        var secondInstall = viewModel.InstallCommand.ExecuteAsync(null);
        await TestAsync.WaitForAsync(() => instanceService.CreateCallCount == 2);

        Assert.True(viewModel.IsInstalling);
        Assert.Equal(2, tasksPage.Tasks.Count);
        Assert.All(tasksPage.Tasks, task => Assert.Equal(DownloadTaskState.Running, task.State));

        allowCreate.SetResult(true);
        await Task.WhenAll(firstInstall, secondInstall);

        Assert.False(viewModel.IsInstalling);
        Assert.Equal(2, instanceService.CreatedInstances.Count);
        Assert.Contains(instanceService.CreatedInstances, instance => instance.Name == "First Install");
        Assert.Contains(instanceService.CreatedInstances, instance => instance.Name == "Second Install");
        Assert.All(tasksPage.Tasks, task => Assert.Equal(DownloadTaskState.Completed, task.State));
    }

    [Fact]
    public async Task DownloadPageInstallShowsErrorWithoutCrashingWhenCreateFails()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var expected = new InvalidOperationException("network down");
        var instanceService = new FakeGameInstanceService { CreateException = expected };
        var viewModel = CreateDownloadPageViewModel(service, instanceService);

        await viewModel.EnsureVersionsLoadedAsync();
        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(viewModel.VisibleVersions.Single());

        await viewModel.InstallCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsInstalling);
        Assert.True(viewModel.HasInstallError);
        Assert.Equal(Strings.Status_InstallFailed, viewModel.InstallError);
    }


    private static DownloadPageViewModel CreateDownloadPageViewModel(
        IGameVersionService gameVersionService,
        IGameInstanceService? instanceService = null,
        DownloadTasksPageViewModel? tasksPage = null,
        IEnumerable<ILoaderProvider>? loaderProviders = null,
        IFloatingMessageService? floatingMessageService = null,
        IInstanceFolderService? instanceFolderService = null,
        IFilePickerService? filePickerService = null,
        ILocalModpackImportService? localModpackImportService = null,
        IModrinthService? modrinthService = null)
    {
        return new DownloadPageViewModel(
            gameVersionService,
            instanceService ?? new FakeGameInstanceService(),
            tasksPage ?? new DownloadTasksPageViewModel(),
            loaderProviders ?? CreateLoaderProviders(),
            ImmediateUiDispatcher.Instance,
            floatingMessageService ?? new FakeFloatingMessageService(),
            instanceFolderService ?? new FakeInstanceFolderService(),
            filePickerService ?? new FakeFilePickerService(),
            localModpackImportService ?? new FakeLocalModpackImportService(),
            modrinthService);
    }

    private static string CreateTempModpackFile(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), "launcher-tests", $"{Guid.NewGuid():N}{extension}");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "stub");
        return path;
    }

    private static GameInstance CreateImportedInstance(string name)
    {
        return new GameInstance
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            VersionName = name,
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Fabric,
            LoaderVersion = "0.16.10",
            InstanceDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"))
        };
    }

    private static IEnumerable<ILoaderProvider> CreateLoaderProviders(
        FakeLoaderProvider? fabricProvider = null,
        FakeLoaderProvider? forgeProvider = null,
        FakeLoaderProvider? neoForgeProvider = null,
        FakeLoaderProvider? quiltProvider = null)
    {
        return
        [
            fabricProvider ?? new FakeLoaderProvider
            {
                Kind = LoaderKind.Fabric,
                LoaderVersions =
                [
                    new LoaderVersionInfo("0.16.10"),
                    new LoaderVersionInfo("0.16.9", false)
                ]
            },
            forgeProvider ?? new FakeLoaderProvider
            {
                Kind = LoaderKind.Forge,
                LoaderVersions =
                [
                    new LoaderVersionInfo("47.4.20"),
                    new LoaderVersionInfo("47.4.10")
                ]
            },
            neoForgeProvider ?? new FakeLoaderProvider
            {
                Kind = LoaderKind.NeoForge,
                LoaderVersions =
                [
                    new LoaderVersionInfo("20.4.237"),
                    new LoaderVersionInfo("20.4.236-beta", false)
                ]
            },
            quiltProvider ?? new FakeLoaderProvider
            {
                Kind = LoaderKind.Quilt,
                LoaderVersions =
                [
                    new LoaderVersionInfo("0.29.2"),
                    new LoaderVersionInfo("0.29.2-beta.5", false)
                ]
            }
        ];
    }

    private sealed class FakeModrinthService : IModrinthService
    {
        public IReadOnlyList<ModrinthVersionInfo> FabricApiVersions { get; init; } = [];
        public Exception? GetFabricApiVersionsException { get; init; }
        public int GetFabricApiVersionsCallCount { get; private set; }
        public string? LastFabricApiMinecraftVersion { get; private set; }
        public IReadOnlyList<ModrinthVersionInfo> QuiltStandardLibraryVersions { get; init; } = [];
        public Exception? GetQuiltStandardLibraryVersionsException { get; init; }
        public int GetQuiltStandardLibraryVersionsCallCount { get; private set; }
        public string? LastQuiltStandardLibraryMinecraftVersion { get; private set; }

        public Task<IReadOnlyList<ModrinthProject>> SearchModsAsync(
            string query,
            string minecraftVersion,
            LoaderKind loader,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ModrinthProject>>([]);
        }

        public Task<IReadOnlyList<ModrinthVersionInfo>> GetFabricApiVersionsAsync(
            string minecraftVersion,
            CancellationToken cancellationToken = default)
        {
            GetFabricApiVersionsCallCount++;
            LastFabricApiMinecraftVersion = minecraftVersion;

            if (GetFabricApiVersionsException is not null)
                return Task.FromException<IReadOnlyList<ModrinthVersionInfo>>(GetFabricApiVersionsException);

            return Task.FromResult(FabricApiVersions);
        }

        public Task<IReadOnlyList<ModrinthVersionInfo>> GetQuiltStandardLibraryVersionsAsync(
            string minecraftVersion,
            CancellationToken cancellationToken = default)
        {
            GetQuiltStandardLibraryVersionsCallCount++;
            LastQuiltStandardLibraryMinecraftVersion = minecraftVersion;

            if (GetQuiltStandardLibraryVersionsException is not null)
                return Task.FromException<IReadOnlyList<ModrinthVersionInfo>>(GetQuiltStandardLibraryVersionsException);

            return Task.FromResult(QuiltStandardLibraryVersions);
        }

        public Task<string> InstallLatestCompatibleAsync(
            ModrinthProject project,
            GameInstance instance,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<string> InstallFabricApiAsync(
            GameInstance instance,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<string> InstallFabricApiAsync(
            GameInstance instance,
            string versionId,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<string> InstallQuiltStandardLibraryAsync(
            GameInstance instance,
            string versionId,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
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

    private sealed class FakeFilePickerService : IFilePickerService
    {
        public string? LocalImportFilePath { get; init; }

        public string? PickMinecraftSkin()
        {
            return null;
        }

        public string? PickJavaExecutable()
        {
            return null;
        }

        public string? PickModFile()
        {
            return null;
        }

        public string? PickSaveArchive()
        {
            return null;
        }

        public string? PickResourcePackArchive()
        {
            return null;
        }

        public string? PickShaderPackArchive()
        {
            return null;
        }

        public string? PickModpackExportArchive(string defaultFileName, ModpackExportKind kind)
        {
            return null;
        }

        public string? PickLocalImportFile()
        {
            return LocalImportFilePath;
        }

        public string? PickFolder(string title, string? initialDirectory = null)
        {
            return null;
        }
    }

    private sealed class FakeLocalModpackImportService : ILocalModpackImportService
    {
        private int importCallCount;

        public ModpackRecognitionResult RecognitionResultToReturn { get; init; } =
            ModpackRecognitionResult.Success();

        public ModpackImportResult ResultToReturn { get; init; } =
            ModpackImportResult.Failure(ModpackImportFailureReason.UnsupportedArchive);

        public Exception? ExceptionToThrow { get; init; }

        public Task? WaitBeforeRecognition { get; init; }

        public IReadOnlyList<Task> WaitBeforeImportByCall { get; init; } = [];

        public IReadOnlyList<LauncherProgress> ProgressReportsToEmit { get; init; } = [];

        public IReadOnlyList<IReadOnlyList<LauncherProgress>> ProgressReportsToEmitByImport { get; init; } = [];

        public string? LastArchivePath { get; private set; }

        public CancellationToken LastImportCancellationToken { get; private set; }

        public int RecognizeCallCount { get; private set; }

        public int ImportCallCount => Volatile.Read(ref importCallCount);

        public List<string> ImportedArchivePaths { get; } = [];

        public TaskCompletionSource<bool> RecognitionStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ModpackRecognitionResult> RecognizeArchiveAsync(
            string archivePath,
            CancellationToken cancellationToken = default)
        {
            RecognizeCallCount++;
            LastArchivePath = archivePath;
            RecognitionStarted.TrySetResult(true);
            if (WaitBeforeRecognition is not null)
                await WaitBeforeRecognition.WaitAsync(cancellationToken);

            return RecognitionResultToReturn;
        }

        public async Task<ModpackImportResult> ImportFromArchiveAsync(
            string archivePath,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default,
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            LastArchivePath = archivePath;
            LastImportCancellationToken = cancellationToken;
            ImportedArchivePaths.Add(archivePath);
            var importIndex = Interlocked.Increment(ref importCallCount) - 1;
            var progressReports = importIndex < ProgressReportsToEmitByImport.Count
                ? ProgressReportsToEmitByImport[importIndex]
                : ProgressReportsToEmit;
            foreach (var progressUpdate in progressReports)
                progress?.Report(progressUpdate);

            if (importIndex < WaitBeforeImportByCall.Count)
                await WaitBeforeImportByCall[importIndex].WaitAsync(cancellationToken);

            if (ExceptionToThrow is not null)
                throw ExceptionToThrow;

            return ResultToReturn;
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
}


