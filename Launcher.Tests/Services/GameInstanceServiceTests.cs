/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Text.Json;
using System.Text.Json.Nodes;
using Launcher.Application;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Launcher.Infrastructure.Modpacks;
using Launcher.Infrastructure.Persistence;

namespace Launcher.Tests.Services;

public sealed class GameInstanceServiceTests : TestTempDirectory
{
    [Fact]
    public async Task CreateInstanceCreatesIsolatedDirectories()
    {
        var (settings, repository, service, provider) = CreateService();

        var instance = await service.CreateInstanceAsync(
            "1.20.1", LoaderKind.Vanilla, null, "Test Instance", null);

        Assert.Equal("Test Instance", provider.LastIsolatedVersionName);
        Assert.Equal(Path.Combine(settings.MinecraftDirectory, "versions", "Test Instance"), instance.InstanceDirectory);
        Assert.True(Directory.Exists(Path.Combine(instance.InstanceDirectory, "mods")));
        Assert.True(Directory.Exists(Path.Combine(instance.InstanceDirectory, "config")));
        Assert.Single(await repository.GetAllAsync());
    }

    [Fact]
    public async Task CreateInstanceRejectsPathLikeName()
    {
        var (settings, repository, service, provider) = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateInstanceAsync("1.20.1", LoaderKind.Vanilla, null, @"..\Pack", null));

        Assert.Null(provider.LastIsolatedVersionName);
        Assert.Empty(await repository.GetAllAsync());
        Assert.False(Directory.Exists(Path.Combine(settings.MinecraftDirectory, "Pack")));
    }

    [Fact]
    public async Task CreateCancellationRemovesNewVersionDirectory()
    {
        var wait = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeLoaderProvider
        {
            WriteJsonBeforeWaiting = true,
            PartialVersionName = "1.20.1",
            WaitBeforeInstall = wait.Task
        };
        var (settings, repository, service, _) = CreateService(provider);
        using var cancellation = new CancellationTokenSource();

        var create = service.CreateInstanceAsync(
            "1.20.1", LoaderKind.Vanilla, null, "Custom Name", null,
            cancellationToken: cancellation.Token);
        await provider.InstallStarted.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => create);
        Assert.False(Directory.Exists(Path.Combine(settings.MinecraftDirectory, "versions", "1.20.1")));
        Assert.Empty(await repository.GetAllAsync());
    }

    [Fact]
    public async Task DiscoveryRecognizesLoaderMetadata()
    {
        var (settings, _, service, _) = CreateService();
        await CreateVersionAsync(settings.MinecraftDirectory, "fabric", "snapshot", "1.21.4", "net.fabricmc:fabric-loader:0.16.9");
        await CreateVersionAsync(settings.MinecraftDirectory, "forge", "release", "1.20.1", "net.minecraftforge:forge:1.20.1-47.2.0");
        await CreateVersionAsync(settings.MinecraftDirectory, "neoforge", "release", "1.20.4", "net.neoforged:neoforge:20.4.237");
        await CreateVersionAsync(settings.MinecraftDirectory, "quilt", "release", "1.20.1", "org.quiltmc:quilt-loader:0.26.0");

        var instances = (await service.GetInstancesAsync()).ToDictionary(instance => instance.VersionName);

        Assert.Equal(LoaderKind.Fabric, instances["fabric"].Loader);
        Assert.Equal("0.16.9", instances["fabric"].LoaderVersion);
        Assert.Equal("snapshot", instances["fabric"].VersionType);
        Assert.Equal(LoaderKind.Forge, instances["forge"].Loader);
        Assert.Equal(LoaderKind.NeoForge, instances["neoforge"].Loader);
        Assert.Equal(LoaderKind.Quilt, instances["quilt"].Loader);
    }

    [Fact]
    public async Task DiscoveryDoesNotCreateOptionalInstanceContentDirectories()
    {
        var (settings, _, service, _) = CreateService();
        await CreateVersionAsync(settings.MinecraftDirectory, "discovered", "release", "1.21", null);
        var instanceDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "discovered");

        var instances = await service.GetInstancesAsync();

        Assert.Contains(instances, instance => instance.VersionName == "discovered");
        foreach (var directoryName in new[] { "mods", "config", "saves", "resourcepacks", "shaderpacks" })
            Assert.False(Directory.Exists(Path.Combine(instanceDirectory, directoryName)));
    }

    [Fact]
    public async Task DeleteDefaultInstanceFallsBackToRemainingInstance()
    {
        var (settings, repository, service, _) = CreateService(defaultInstanceId: "first");
        await CreateVersionAsync(settings.MinecraftDirectory, "first");
        await CreateVersionAsync(settings.MinecraftDirectory, "second");
        await repository.SaveAllAsync([CreateStoredInstance("first"), CreateStoredInstance("second")]);

        Assert.True(await service.DeleteInstanceAsync("first"));
        Assert.Equal("second", (await new TestSettingsService(settings).LoadAsync()).DefaultInstanceId);
        Assert.Single(await repository.GetAllAsync());
        Assert.Empty(Directory.GetDirectories(Path.Combine(settings.MinecraftDirectory, "versions"), ".bhl-delete-pending-*"));
    }

    [Fact]
    public async Task DeleteInstanceDoesNotRewriteRemainingInstanceSettings()
    {
        var (settings, repository, service, _) = CreateService(defaultInstanceId: "first");
        await CreateVersionAsync(settings.MinecraftDirectory, "first");
        await CreateVersionAsync(settings.MinecraftDirectory, "second");
        await repository.SaveAllAsync([CreateStoredInstance("first"), CreateStoredInstance("second")]);

        var secondSettingsPath = Path.Combine(
            settings.MinecraftDirectory,
            "versions",
            "second",
            LauncherApplicationIdentity.StorageDirectoryName,
            "instance-settings.json");
        var expectedContents = await File.ReadAllTextAsync(secondSettingsPath);
        var expectedLastWriteTime = DateTime.UtcNow.AddMinutes(-5);
        File.SetLastWriteTimeUtc(secondSettingsPath, expectedLastWriteTime);
        expectedLastWriteTime = File.GetLastWriteTimeUtc(secondSettingsPath);

        Assert.True(await service.DeleteInstanceAsync("first"));

        Assert.Equal(expectedContents, await File.ReadAllTextAsync(secondSettingsPath));
        Assert.Equal(expectedLastWriteTime, File.GetLastWriteTimeUtc(secondSettingsPath));
    }

    [Fact]
    public async Task CreateInstanceDoesNotExposeFinalDirectoryBeforeCommit()
    {
        var releaseInstall = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeLoaderProvider { WaitBeforeInstall = releaseInstall.Task };
        var (settings, repository, service, _) = CreateService(provider);

        var creation = service.CreateInstanceAsync(
            "1.20.1", LoaderKind.Vanilla, null, "Test", null);
        await provider.InstallStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var versionsDirectory = Path.Combine(settings.MinecraftDirectory, "versions");
        Assert.False(Directory.Exists(Path.Combine(versionsDirectory, "Test")));
        Assert.Single(Directory.GetDirectories(versionsDirectory, ".bhl-install-pending-Test-*"));
        Assert.Empty(await repository.DiscoverInstalledVersionsAsync(settings.MinecraftDirectory));

        releaseInstall.SetResult(true);
        var instance = await creation;
        Assert.Equal(Path.Combine(versionsDirectory, "Test"), instance.InstanceDirectory);
        Assert.True(File.Exists(Path.Combine(instance.InstanceDirectory, "Test.json")));
    }

    [Fact]
    public async Task CreateInstancePreservesSettingsChangedWhileInstallIsRunning()
    {
        var settingsService = new JsonSettingsService(TempRoot);
        var initialSettings = await settingsService.LoadAsync();
        initialSettings.MinecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        initialSettings.Theme = "Dark";
        initialSettings.DownloadSourcePreference = DownloadSourcePreference.Auto;
        initialSettings.DefaultMemoryMb = 4096;
        await settingsService.SaveAsync(initialSettings);

        var releaseInstall = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeLoaderProvider { WaitBeforeInstall = releaseInstall.Task };
        var repository = new JsonGameInstanceRepository(settingsService);
        var transactionService = new InstanceInstallTransactionService();
        var service = new GameInstanceService(
            settingsService,
            repository,
            [provider],
            modpackGameInstaller: new ModpackGameInstaller([provider]),
            installTransactionService: transactionService);

        var creation = service.CreateInstanceAsync(
            "1.20.1", LoaderKind.Vanilla, null, "Test", null);
        await provider.InstallStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var changedSettings = await settingsService.LoadAsync();
        changedSettings.Theme = "Light";
        changedSettings.DownloadSourcePreference = DownloadSourcePreference.BmclApi;
        changedSettings.DefaultMemoryMb = 8192;
        await settingsService.SaveAsync(changedSettings);

        releaseInstall.SetResult(true);
        var instance = await creation;
        var persistedSettings = await settingsService.LoadAsync();

        Assert.Equal("Light", persistedSettings.Theme);
        Assert.Equal(DownloadSourcePreference.BmclApi, persistedSettings.DownloadSourcePreference);
        Assert.Equal(8192, persistedSettings.DefaultMemoryMb);
        Assert.Equal(instance.Id, persistedSettings.DefaultInstanceId);
    }

    [Fact]
    public async Task CreateInstanceDoesNotReplaceDefaultSelectedWhileInstallIsRunning()
    {
        var settingsService = new JsonSettingsService(TempRoot);
        var initialSettings = await settingsService.LoadAsync();
        initialSettings.MinecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        await settingsService.SaveAsync(initialSettings);

        var releaseInstall = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeLoaderProvider { WaitBeforeInstall = releaseInstall.Task };
        var repository = new JsonGameInstanceRepository(settingsService);
        var transactionService = new InstanceInstallTransactionService();
        var service = new GameInstanceService(
            settingsService,
            repository,
            [provider],
            modpackGameInstaller: new ModpackGameInstaller([provider]),
            installTransactionService: transactionService);

        var creation = service.CreateInstanceAsync(
            "1.20.1", LoaderKind.Vanilla, null, "Test", null);
        await provider.InstallStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var changedSettings = await settingsService.LoadAsync();
        changedSettings.DefaultInstanceId = "user-selected-instance";
        await settingsService.SaveAsync(changedSettings);

        releaseInstall.SetResult(true);
        await creation;

        Assert.Equal("user-selected-instance", (await settingsService.LoadAsync()).DefaultInstanceId);
    }

    [Fact]
    public async Task CreateInstanceDoesNotPublishOldDirectoryInstanceIntoNewDirectorySettings()
    {
        var settingsService = new JsonSettingsService(TempRoot);
        var initialSettings = await settingsService.LoadAsync();
        var installMinecraftDirectory = Path.Combine(TempRoot, "first", ".minecraft");
        var newMinecraftDirectory = Path.Combine(TempRoot, "second", ".minecraft");
        initialSettings.MinecraftDirectory = installMinecraftDirectory;
        await settingsService.SaveAsync(initialSettings);

        var releaseInstall = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeLoaderProvider { WaitBeforeInstall = releaseInstall.Task };
        var repository = new JsonGameInstanceRepository(settingsService);
        var transactionService = new InstanceInstallTransactionService();
        var service = new GameInstanceService(
            settingsService,
            repository,
            [provider],
            modpackGameInstaller: new ModpackGameInstaller([provider]),
            installTransactionService: transactionService);

        var creation = service.CreateInstanceAsync(
            "1.20.1", LoaderKind.Vanilla, null, "Test", null);
        await provider.InstallStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var changedSettings = await settingsService.LoadAsync();
        changedSettings.MinecraftDirectory = newMinecraftDirectory;
        await settingsService.SaveAsync(changedSettings);
        releaseInstall.SetResult(true);

        var instance = await creation;
        var persistedSettings = await settingsService.LoadAsync();

        Assert.Equal(newMinecraftDirectory, persistedSettings.MinecraftDirectory);
        Assert.Null(persistedSettings.DefaultInstanceId);
        Assert.StartsWith(Path.GetFullPath(installMinecraftDirectory), Path.GetFullPath(instance.InstanceDirectory));
        Assert.True(File.Exists(Path.Combine(instance.InstanceDirectory, ".bhl-install-pending.json")));
    }

    [Fact]
    public async Task DeleteInstancePreservesOriginalStateWhenStagingMoveFails()
    {
        var settings = CreateSettings("first");
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(
            settingsService,
            logger: null,
            moveDirectoryAsync: null,
            deletionGuidFactory: () => Guid.ParseExact("a83f21c4000000000000000000000000", "N"),
            stageDeletionMove: (_, _) => throw new IOException("source is locked"),
            deleteStagedDirectory: null);
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);
        await CreateVersionAsync(settings.MinecraftDirectory, "first");
        await repository.SaveAllAsync([CreateStoredInstance("first")]);

        await Assert.ThrowsAsync<IOException>(() => service.DeleteInstanceAsync("first"));

        Assert.True(Directory.Exists(Path.Combine(settings.MinecraftDirectory, "versions", "first")));
        Assert.Single(await repository.GetAllAsync());
        Assert.Equal("first", (await settingsService.LoadAsync()).DefaultInstanceId);
    }

    [Fact]
    public async Task DeleteInstanceRemainsCommittedWhenPhysicalCleanupFails()
    {
        var settings = CreateSettings("first");
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(
            settingsService,
            logger: null,
            moveDirectoryAsync: null,
            deletionGuidFactory: () => Guid.ParseExact("a83f21c4000000000000000000000000", "N"),
            stageDeletionMove: Directory.Move,
            deleteStagedDirectory: (_, _) => throw new IOException("directory is still in use"),
            recycleStagedDirectory: _ => throw new IOException("recycle bin unavailable"));
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);
        await CreateVersionAsync(settings.MinecraftDirectory, "first");
        await repository.SaveAllAsync([CreateStoredInstance("first")]);

        Assert.True(await service.DeleteInstanceAsync("first"));

        var versionsDirectory = Path.Combine(settings.MinecraftDirectory, "versions");
        Assert.False(Directory.Exists(Path.Combine(versionsDirectory, "first")));
        var pendingDirectory = Path.Combine(versionsDirectory, ".bhl-delete-pending-first-a83f21c4");
        Assert.True(Directory.Exists(pendingDirectory));
        Assert.True(File.Exists(Path.Combine(pendingDirectory, ".bhl-delete-pending-.json")));
        Assert.Empty(await repository.GetAllAsync());
        Assert.Empty(await service.GetInstancesAsync());
        Assert.Equal(string.Empty, (await settingsService.LoadAsync()).DefaultInstanceId);
    }

    [Fact]
    public async Task DeleteInstanceRejectsConcurrentDuplicateRequest()
    {
        var settings = CreateSettings();
        var settingsService = new TestSettingsService(settings);
        var moveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var allowMove = new ManualResetEventSlim();
        var moveCount = 0;
        var repository = new JsonGameInstanceRepository(
            settingsService,
            logger: null,
            moveDirectoryAsync: null,
            deletionGuidFactory: () => Guid.NewGuid(),
            stageDeletionMove: (source, destination) =>
            {
                Interlocked.Increment(ref moveCount);
                moveStarted.TrySetResult();
                if (!allowMove.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("test did not release staged deletion move");
                Directory.Move(source, destination);
            },
            deleteStagedDirectory: null,
            recycleStagedDirectory: path => Directory.Delete(path, recursive: true));
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);
        await CreateVersionAsync(settings.MinecraftDirectory, "first");
        await repository.SaveAllAsync([CreateStoredInstance("first")]);

        var firstDelete = service.DeleteInstanceAsync("first");
        await moveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var duplicateResult = await service.DeleteInstanceAsync("first");
        allowMove.Set();

        Assert.False(duplicateResult);
        Assert.True(await firstDelete);
        Assert.Equal(1, moveCount);
    }

    [Fact]
    public async Task SaveInstanceAsyncUsesIdentitySafeSingleInstanceUpdate()
    {
        var (settings, repository, service, _) = CreateService();
        await CreateVersionAsync(settings.MinecraftDirectory, "first");
        await repository.SaveAllAsync([CreateStoredInstance("first")]);
        var instance = Assert.Single(await repository.GetAllAsync());
        instance.Description = "updated";

        await service.SaveInstanceAsync(instance);

        Assert.Equal("updated", Assert.Single(await repository.GetAllAsync()).Description);
    }

    [Fact]
    public async Task RenameInstanceAsyncWithIconOnlyUsesIdentitySafeSingleInstanceUpdate()
    {
        var (settings, repository, service, _) = CreateService();
        await CreateVersionAsync(settings.MinecraftDirectory, "first");
        await repository.SaveAllAsync([CreateStoredInstance("first")]);

        var renamed = await service.RenameInstanceAsync("first", "first", "icon.png");

        Assert.Equal("icon.png", renamed.IconSource);
        Assert.True(Directory.Exists(Path.Combine(settings.MinecraftDirectory, "versions", "first")));
        var stored = Assert.Single(await repository.GetAllAsync());
        Assert.Equal("first", stored.Id);
        Assert.Equal("icon.png", stored.IconSource);
    }

    [Fact]
    public async Task StartupCleanupServiceDeletesPendingInstanceDirectories()
    {
        var settings = CreateSettings();
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(
            settingsService,
            logger: null,
            moveDirectoryAsync: null,
            deletionGuidFactory: null,
            stageDeletionMove: null,
            deleteStagedDirectory: null,
            recycleStagedDirectory: path => Directory.Delete(path, recursive: true));
        var pendingDirectory = Path.Combine(
            settings.MinecraftDirectory,
            "versions",
            ".bhl-delete-pending-Test-a83f21c4");
        Directory.CreateDirectory(pendingDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(pendingDirectory, ".bhl-delete-pending-.json"),
            """{"schemaVersion":1,"transactionId":"a83f21c4000000000000000000000000","versionName":"Test","createdAtUtc":"2026-07-13T00:00:00Z"}""");
        await File.WriteAllTextAsync(Path.Combine(pendingDirectory, "Test.json"), """{"id":"Test"}""");
        var cleanupService = new InstanceDeletionCleanupService(settingsService, repository);

        await cleanupService.CleanupPendingAsync();

        Assert.False(Directory.Exists(pendingDirectory));
    }

    [Fact]
    public async Task RenameRemainsCommittedWhenPrimaryJarRenameFails()
    {
        var (settings, repository, service, _) = CreateService();
        await CreateVersionAsync(settings.MinecraftDirectory, "Old Pack", writeJar: true);
        var oldDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "Old Pack");
        await File.WriteAllTextAsync(Path.Combine(oldDirectory, "Renamed Pack.jar"), "conflict");
        await repository.SaveAllAsync([
            new GameInstance
            {
                Id = "old",
                Name = "Old Pack",
                MinecraftVersion = "1.20.1",
                VersionName = "Old Pack",
                InstanceDirectory = oldDirectory
            }
        ]);

        await Assert.ThrowsAsync<IOException>(() => service.RenameInstanceAsync("old", "Renamed Pack", null));

        Assert.False(Directory.Exists(oldDirectory));
        Assert.False(Directory.Exists(Path.Combine(settings.MinecraftDirectory, "versions", "Renamed Pack")));
        var pendingDirectory = Assert.Single(Directory.GetDirectories(
            Path.Combine(settings.MinecraftDirectory, "versions"),
            ".bhl-rename-pending-Old Pack-*"));
        Assert.True(File.Exists(Path.Combine(pendingDirectory, ".bhl-rename-pending.json")));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(pendingDirectory, "Old Pack.json")));
        Assert.Equal("Old Pack", json.RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public async Task RenameStagingFailureKeepsInstanceVisibleWhenMarkerDeleteFails()
    {
        var settings = CreateSettings();
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(
            settingsService,
            logger: null,
            moveDirectoryAsync: (_, _, _) => throw new IOException("source is locked"),
            deletionGuidFactory: null,
            stageDeletionMove: null,
            deleteStagedDirectory: null,
            recycleStagedDirectory: null,
            renameGuidFactory: () => Guid.ParseExact("a83f21c4000000000000000000000000", "N"),
            deleteRenameMarker: _ => throw new IOException("marker is locked"));
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);
        await CreateVersionAsync(settings.MinecraftDirectory, "Old Pack");
        var oldDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "Old Pack");
        await repository.SaveAllAsync([new GameInstance
        {
            Id = "old",
            Name = "Old Pack",
            MinecraftVersion = "1.20.1",
            VersionName = "Old Pack",
            InstanceDirectory = oldDirectory
        }]);

        await Assert.ThrowsAsync<IOException>(() => service.RenameInstanceAsync("old", "New Pack", null));

        Assert.True(Directory.Exists(oldDirectory));
        Assert.False(File.Exists(Path.Combine(oldDirectory, ".bhl-rename-pending.json")));
        Assert.True(File.Exists(Path.Combine(oldDirectory, ".bhl-rename-aborted.json")));
        Assert.Single(await repository.GetAllAsync());
    }

    private (LauncherSettings Settings, JsonGameInstanceRepository Repository, GameInstanceService Service, FakeLoaderProvider Provider)
        CreateService(FakeLoaderProvider? provider = null, string? defaultInstanceId = null)
    {
        var settings = CreateSettings(defaultInstanceId);
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(
            settingsService,
            logger: null,
            moveDirectoryAsync: null,
            deletionGuidFactory: null,
            stageDeletionMove: null,
            deleteStagedDirectory: null,
            recycleStagedDirectory: path => Directory.Delete(path, recursive: true));
        provider ??= new FakeLoaderProvider();
        var installer = new ModpackGameInstaller([provider]);
        var transactionService = new InstanceInstallTransactionService();
        return (
            settings,
            repository,
            new GameInstanceService(
                settingsService,
                repository,
                [provider],
                modpackGameInstaller: installer,
                installTransactionService: transactionService),
            provider);
    }

    private LauncherSettings CreateSettings(string? defaultInstanceId = null) => new()
    {
        DataDirectory = TempRoot,
        MinecraftDirectory = Path.Combine(TempRoot, ".minecraft"),
        DefaultInstanceId = defaultInstanceId ?? string.Empty
    };

    private static async Task CreateVersionAsync(
        string minecraftDirectory,
        string versionName,
        string? type = null,
        string? inheritsFrom = null,
        string? library = null,
        bool writeJar = false)
    {
        var directory = Path.Combine(minecraftDirectory, "versions", versionName);
        Directory.CreateDirectory(directory);
        var json = new JsonObject { ["id"] = versionName, ["jar"] = versionName };
        if (type is not null) json["type"] = type;
        if (inheritsFrom is not null) json["inheritsFrom"] = inheritsFrom;
        if (library is not null)
            json["libraries"] = new JsonArray(new JsonObject { ["name"] = library });
        await File.WriteAllTextAsync(Path.Combine(directory, $"{versionName}.json"), json.ToJsonString());
        if (writeJar)
            await File.WriteAllTextAsync(Path.Combine(directory, $"{versionName}.jar"), "fake jar");
    }

    private static GameInstance CreateStoredInstance(string name) => new()
    {
        Id = name,
        Name = name,
        MinecraftVersion = "1.20.1",
        VersionName = name,
        InstanceDirectory = string.Empty
    };
}
