/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Text.Json;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Modpacks;
using Launcher.Infrastructure.Persistence;
using Launcher.Tests.Fakes;

namespace Launcher.Tests.Infrastructure.Persistence;

public sealed class InstanceInstallTransactionTests : TestTempDirectory
{
    [Fact]
    public async Task CommitMovesPreparedLogicalVersionIntoFinalDirectory()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var service = new InstanceInstallTransactionService(
            logger: null,
            guidFactory: () => Guid.ParseExact("a83f21c4000000000000000000000000", "N"));
        await using var transaction = await service.BeginAsync(
            minecraftDirectory, "Test", "instance", "game", true);
        var versionsDirectory = Path.Combine(minecraftDirectory, "versions");
        Assert.False(File.Exists(Path.Combine(versionsDirectory, ".bhl-install-transaction.lock")));
        Assert.False(File.Exists(Path.Combine(versionsDirectory, ".bhl-version-mutation.lock")));
        Assert.StartsWith(
            Path.Combine(AppContext.BaseDirectory, "BHL", "locks", "versions"),
            CrossProcessVersionLock.GetInstallCoordinationPath(minecraftDirectory),
            StringComparison.OrdinalIgnoreCase);
        Directory.CreateDirectory(Path.Combine(transaction.PendingDirectory, "mods"));
        await File.WriteAllTextAsync(
            Path.Combine(transaction.PendingDirectory, "Test.json"),
            """{"id":"Test","jar":"Test"}""");
        await File.WriteAllTextAsync(Path.Combine(transaction.PendingDirectory, "Test.jar"), "jar");
        var instance = CreateInstance("instance", "Test", transaction.PendingDirectory);

        await transaction.CommitAsync(instance);

        Assert.True(transaction.IsCommitted);
        Assert.False(Directory.Exists(transaction.PendingDirectory));
        Assert.True(File.Exists(Path.Combine(transaction.FinalDirectory, "Test.json")));
        Assert.True(File.Exists(Path.Combine(transaction.FinalDirectory, "Test.jar")));
        Assert.True(Directory.Exists(Path.Combine(transaction.FinalDirectory, "mods")));
        Assert.True(File.Exists(Path.Combine(transaction.FinalDirectory, ".bhl-install-pending.json")));
        await transaction.CompleteLogicalCommitAsync();
        Assert.False(File.Exists(Path.Combine(transaction.FinalDirectory, ".bhl-install-pending.json")));
        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(transaction.FinalDirectory, "BHL", "instance-settings.json")));
        Assert.Equal("Test", settings.RootElement.GetProperty("VersionName").GetString());
        Assert.Equal(transaction.FinalDirectory, settings.RootElement.GetProperty("InstanceDirectory").GetString());
    }

    [Fact]
    public async Task CommitFailsWithoutOverwritingOrMergingWhenFinalDirectoryAppearsDuringInstall()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var service = new InstanceInstallTransactionService();
        await using var transaction = await service.BeginAsync(
            minecraftDirectory, "Test", "instance", "game", false);
        await File.WriteAllTextAsync(
            Path.Combine(transaction.PendingDirectory, "Test.json"),
            """{"id":"Test"}""");
        await File.WriteAllTextAsync(Path.Combine(transaction.PendingDirectory, "downloaded.txt"), "pending");
        Directory.CreateDirectory(transaction.FinalDirectory);
        await File.WriteAllTextAsync(Path.Combine(transaction.FinalDirectory, "user-created.txt"), "keep");
        var instance = CreateInstance("instance", "Test", transaction.PendingDirectory);

        await Assert.ThrowsAnyAsync<IOException>(() => transaction.CommitAsync(instance));

        Assert.False(transaction.IsCommitted);
        Assert.True(Directory.Exists(transaction.PendingDirectory));
        Assert.Equal("keep", await File.ReadAllTextAsync(
            Path.Combine(transaction.FinalDirectory, "user-created.txt")));
        Assert.False(File.Exists(Path.Combine(transaction.FinalDirectory, "downloaded.txt")));
    }

    [Fact]
    public async Task LoaderUsesLogicalIdentityButCopiesToExplicitPhysicalDirectory()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var transactionService = new InstanceInstallTransactionService();
        await using var transaction = await transactionService.BeginAsync(
            minecraftDirectory, "Test", "instance", "game", false);
        var provider = new FakeLoaderProvider();
        var installer = new ModpackGameInstaller([provider]);
        var target = new LoaderInstallTarget(minecraftDirectory, "Test", transaction.PendingDirectory);

        var versionName = await installer.InstallInstanceAsync(
            "1.20.1", LoaderKind.Vanilla, null, target, progress: null);

        Assert.Equal("Test", versionName);
        Assert.Equal("Test", provider.LastIsolatedVersionName);
        Assert.NotEqual(Path.GetFullPath(minecraftDirectory), Path.GetFullPath(provider.LastGameDirectory!));
        Assert.False(Directory.Exists(Path.Combine(minecraftDirectory, "versions", "Test")));
        Assert.True(File.Exists(Path.Combine(transaction.PendingDirectory, "Test.json")));
        Assert.True(File.Exists(Path.Combine(transaction.PendingDirectory, "Test.jar")));
    }

    [Fact]
    public async Task LoaderSandboxIsNotSeededWithExistingSharedRuntimeTrees()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        Directory.CreateDirectory(Path.Combine(minecraftDirectory, "libraries", "unrelated"));
        Directory.CreateDirectory(Path.Combine(minecraftDirectory, "assets", "objects", "aa"));
        await File.WriteAllTextAsync(Path.Combine(minecraftDirectory, "libraries", "unrelated", "library.jar"), "library");
        await File.WriteAllTextAsync(Path.Combine(minecraftDirectory, "assets", "objects", "aa", "asset"), "asset");
        var transactionService = new InstanceInstallTransactionService();
        await using var transaction = await transactionService.BeginAsync(
            minecraftDirectory, "Test", "instance", "game", false);
        var provider = new FakeLoaderProvider();
        var installer = new ModpackGameInstaller([provider]);

        await installer.InstallInstanceAsync(
            "1.20.1",
            LoaderKind.Vanilla,
            null,
            new LoaderInstallTarget(minecraftDirectory, "Test", transaction.PendingDirectory),
            progress: null);

        Assert.False(provider.SawLibrariesDirectoryDuringInstall);
        Assert.False(provider.SawAssetObjectsDirectoryDuringInstall);
        Assert.False(Directory.Exists(Path.Combine(minecraftDirectory, "versions", "Test")));
    }

    [Fact]
    public async Task ConcurrentInstallersProceedWhileCleanupSkipsTheirActivePendingDirectories()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var settingsService = new TestSettingsService(new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = minecraftDirectory
        });
        var first = new InstanceInstallTransactionService();
        var second = new InstanceInstallTransactionService();
        await using var transaction = await first.BeginAsync(
            minecraftDirectory, "Test", "instance", "game", false);
        await using var otherTransaction = await second.BeginAsync(
            minecraftDirectory, "Other", "other", "game", false);
        var cleanup = new InstanceInstallCleanupService(settingsService);
        await cleanup.CleanupPendingAsync();

        Assert.True(Directory.Exists(transaction.PendingDirectory));
        Assert.True(Directory.Exists(otherTransaction.PendingDirectory));
    }

    [Fact]
    public async Task ModpackStagingSuffixesSameNameWhileFirstImportOrDownloadIsActive()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        var transactionService = new InstanceInstallTransactionService();
        var instanceService = new GameInstanceService(
            settingsService,
            repository,
            Array.Empty<ILoaderProvider>(),
            installTransactionService: transactionService);
        var staging = new ModpackInstanceStagingService(
            settingsService,
            repository,
            instanceService,
            transactionService);
        var prepared = new PreparedModpack
        {
            PackageKind = ModpackPackageKind.Modrinth,
            SourceArchivePath = Path.Combine(TempRoot, "pack.mrpack"),
            WorkingDirectory = Path.Combine(TempRoot, "work"),
            PackageName = "Same Pack",
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Fabric,
            LoaderVersion = "0.16.10"
        };

        var first = await staging.StageAsync(prepared, "Same Pack");
        var second = await staging.StageAsync(prepared, "Same Pack");

        Assert.Equal("Same Pack", first.ResolvedInstanceName);
        Assert.Equal("Same Pack (1)", second.ResolvedInstanceName);
        Assert.Contains(".bhl-install-pending-Same Pack-", first.InstanceDirectory);
        Assert.Contains(".bhl-install-pending-Same Pack (1)-", second.InstanceDirectory);

        await staging.CleanupFailedImportAsync(first, null);
        await staging.CleanupFailedImportAsync(second, null);
    }

    [Fact]
    public async Task ActiveInstallationDoesNotBlockDeletingAnotherInstance()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        var oldDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "Old");
        Directory.CreateDirectory(oldDirectory);
        await File.WriteAllTextAsync(Path.Combine(oldDirectory, "Old.json"), """{"id":"Old"}""");
        var installService = new InstanceInstallTransactionService();
        await using var transaction = await installService.BeginAsync(
            settings.MinecraftDirectory, "Downloading", "downloading", "game", false);

        var stagedDelete = await repository.StageVersionForDeletionAsync(settings.MinecraftDirectory, "Old");

        Assert.False(Directory.Exists(oldDirectory));
        Assert.True(Directory.Exists(stagedDelete));
        Assert.True(Directory.Exists(transaction.PendingDirectory));
    }

    [Fact]
    public async Task RenameCannotClaimLogicalNameReservedByActiveInstallation()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        var oldDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "Old");
        Directory.CreateDirectory(oldDirectory);
        await File.WriteAllTextAsync(Path.Combine(oldDirectory, "Old.json"), """{"id":"Old"}""");
        var oldInstance = CreateInstance("old", "Old", oldDirectory);
        await repository.SaveAllAsync([oldInstance]);
        var installService = new InstanceInstallTransactionService();
        var instanceService = new GameInstanceService(
            settingsService,
            repository,
            Array.Empty<ILoaderProvider>(),
            installTransactionService: installService);
        await using var transaction = await installService.BeginAsync(
            settings.MinecraftDirectory, "Test", "installing", "game", false);

        await Assert.ThrowsAsync<DuplicateGameInstanceNameException>(() => instanceService.RenameInstanceAsync(
            oldInstance.Id,
            "Test",
            newIconSource: null));

        Assert.True(Directory.Exists(oldDirectory));
        Assert.False(Directory.Exists(Path.Combine(settings.MinecraftDirectory, "versions", "Test")));
    }

    [Fact]
    public async Task StartupCleanupDeletesUnlockedPendingDirectoryWithValidMetadata()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        var pending = Path.Combine(
            settings.MinecraftDirectory,
            "versions",
            ".bhl-install-pending-Test-a83f21c4");
        Directory.CreateDirectory(Path.Combine(pending, "BHL"));
        var versionsDirectory = Path.GetDirectoryName(pending)!;
        await File.WriteAllTextAsync(Path.Combine(versionsDirectory, ".bhl-install-transaction.lock"), string.Empty);
        await File.WriteAllTextAsync(Path.Combine(versionsDirectory, ".bhl-version-mutation.lock"), string.Empty);
        await File.WriteAllTextAsync(Path.Combine(pending, "Test.json"), """{"id":"Test","type":"release"}""");
        await File.WriteAllTextAsync(
            Path.Combine(pending, ".bhl-install-pending.json"),
            """{"schemaVersion":1,"transactionId":"a83f21c4000000000000000000000000","instanceId":"test","logicalVersionName":"Test","installKind":"modpack","initializeDefaultIfEmpty":false,"createdAtUtc":"2026-07-13T00:00:00Z"}""");
        await File.WriteAllTextAsync(
            Path.Combine(pending, "BHL", "instance-settings.json"),
            """{"id":"test","name":"Test","versionName":"Test"}""");

        Assert.Empty(await repository.DiscoverInstalledVersionsAsync(settings.MinecraftDirectory));
        Assert.Empty(await repository.GetAllAsync());

        await new InstanceInstallCleanupService(settingsService).CleanupPendingAsync();

        Assert.False(Directory.Exists(pending));
        Assert.False(File.Exists(Path.Combine(versionsDirectory, ".bhl-install-transaction.lock")));
        Assert.False(File.Exists(Path.Combine(versionsDirectory, ".bhl-version-mutation.lock")));
    }

    [Fact]
    public async Task StartupCleanupPreservesPrefixedInstallDirectoryWithoutValidMarker()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var directory = Path.Combine(
            settings.MinecraftDirectory,
            "versions",
            ".bhl-install-pending-UserData-a83f21c4");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "keep.txt"), "keep");

        await new InstanceInstallCleanupService(new TestSettingsService(settings)).CleanupPendingAsync();

        Assert.True(Directory.Exists(directory));
        Assert.True(File.Exists(Path.Combine(directory, "keep.txt")));
    }

    [Fact]
    public async Task BeginFailsWithoutCreatingFinalDirectoryAfterThreeGuidConflicts()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var versionsDirectory = Path.Combine(minecraftDirectory, "versions");
        Directory.CreateDirectory(versionsDirectory);
        foreach (var suffix in new[] { "a83f21c4", "b94e32d5", "c05f43e6" })
            Directory.CreateDirectory(Path.Combine(versionsDirectory, $".bhl-install-pending-Test-{suffix}"));
        var guids = new Queue<Guid>(
        [
            Guid.ParseExact("a83f21c4000000000000000000000000", "N"),
            Guid.ParseExact("b94e32d5000000000000000000000000", "N"),
            Guid.ParseExact("c05f43e6000000000000000000000000", "N")
        ]);
        var service = new InstanceInstallTransactionService(null, () => guids.Dequeue());

        await Assert.ThrowsAsync<IOException>(() => service.BeginAsync(
            minecraftDirectory, "Test", "instance", "game", false));

        Assert.False(Directory.Exists(Path.Combine(versionsDirectory, "Test")));
    }

    [Fact]
    public async Task CleanupReconcilesCommittedMarkerWithoutDeletingFinalDirectory()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var finalDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "Test");
        Directory.CreateDirectory(finalDirectory);
        await File.WriteAllTextAsync(Path.Combine(finalDirectory, "Test.json"), """{"id":"Test"}""");
        await File.WriteAllTextAsync(
            Path.Combine(finalDirectory, ".bhl-install-pending.json"),
            """
            {
              "schemaVersion": 1,
              "transactionId": "a83f21c4000000000000000000000000",
              "instanceId": "instance",
              "logicalVersionName": "Test",
              "installKind": "game",
              "initializeDefaultIfEmpty": true,
              "createdAtUtc": "2026-07-13T00:00:00Z"
            }
            """);

        await new InstanceInstallCleanupService(settingsService).CleanupPendingAsync();

        Assert.True(Directory.Exists(finalDirectory));
        Assert.True(File.Exists(Path.Combine(finalDirectory, "Test.json")));
        Assert.False(File.Exists(Path.Combine(finalDirectory, ".bhl-install-pending.json")));
        Assert.Equal("instance", (await settingsService.LoadAsync()).DefaultInstanceId);
    }

    [Fact]
    public async Task CleanupReloadsLatestSettingsBeforeInitializingDefaultInstance()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var initialSettings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = minecraftDirectory,
            Theme = "Dark"
        };
        var latestSettings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = minecraftDirectory,
            Theme = "Light",
            DownloadSourcePreference = DownloadSourcePreference.BmclApi,
            DefaultMemoryMb = 8192
        };
        var settingsService = new SequencedSettingsService(initialSettings, latestSettings);
        var finalDirectory = Path.Combine(minecraftDirectory, "versions", "Test");
        Directory.CreateDirectory(finalDirectory);
        await File.WriteAllTextAsync(Path.Combine(finalDirectory, "Test.json"), """{"id":"Test"}""");
        await File.WriteAllTextAsync(
            Path.Combine(finalDirectory, ".bhl-install-pending.json"),
            """
            {
              "schemaVersion": 1,
              "transactionId": "a83f21c4000000000000000000000000",
              "instanceId": "instance",
              "logicalVersionName": "Test",
              "installKind": "game",
              "initializeDefaultIfEmpty": true,
              "createdAtUtc": "2026-07-13T00:00:00Z"
            }
            """);

        await new InstanceInstallCleanupService(settingsService).CleanupPendingAsync();

        Assert.NotNull(settingsService.SavedSettings);
        Assert.Equal("Light", settingsService.SavedSettings.Theme);
        Assert.Equal(DownloadSourcePreference.BmclApi, settingsService.SavedSettings.DownloadSourcePreference);
        Assert.Equal(8192, settingsService.SavedSettings.DefaultMemoryMb);
        Assert.Equal("instance", settingsService.SavedSettings.DefaultInstanceId);
        Assert.False(File.Exists(Path.Combine(finalDirectory, ".bhl-install-pending.json")));
    }

    [Fact]
    public async Task CleanupPreservesCommittedMarkerWhenLatestSettingsSaveFails()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new FailingSaveSettingsService(settings);
        var finalDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "Test");
        Directory.CreateDirectory(finalDirectory);
        await File.WriteAllTextAsync(Path.Combine(finalDirectory, "Test.json"), """{"id":"Test"}""");
        var markerPath = Path.Combine(finalDirectory, ".bhl-install-pending.json");
        await File.WriteAllTextAsync(
            markerPath,
            """
            {
              "schemaVersion": 1,
              "transactionId": "a83f21c4000000000000000000000000",
              "instanceId": "instance",
              "logicalVersionName": "Test",
              "installKind": "game",
              "initializeDefaultIfEmpty": true,
              "createdAtUtc": "2026-07-13T00:00:00Z"
            }
            """);

        await new InstanceInstallCleanupService(settingsService).CleanupPendingAsync();

        Assert.True(Directory.Exists(finalDirectory));
        Assert.True(File.Exists(markerPath));
    }

    private static GameInstance CreateInstance(string id, string name, string directory) => new()
    {
        Id = id,
        Name = name,
        MinecraftVersion = "1.20.1",
        VersionName = name,
        InstanceDirectory = directory
    };

    private sealed class SequencedSettingsService(
        LauncherSettings initialSettings,
        LauncherSettings latestSettings) : ISettingsService
    {
        private int loadCount;

        public LauncherSettings? SavedSettings { get; private set; }

        public Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default)
        {
            var settings = Interlocked.Increment(ref loadCount) == 1
                ? initialSettings
                : latestSettings;
            return Task.FromResult(settings);
        }

        public Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default)
        {
            SavedSettings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingSaveSettingsService(LauncherSettings settings) : ISettingsService
    {
        public Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(settings);

        public Task SaveAsync(LauncherSettings value, CancellationToken cancellationToken = default) =>
            Task.FromException(new IOException("settings are locked"));
    }
}
