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

using Launcher.Application;
using System.Text.Json;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Persistence;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Persistence;

public sealed class JsonGameInstanceRepositoryTests : TestTempDirectory
{
    private static readonly string InstanceMetadataDirectoryName = LauncherApplicationIdentity.StorageDirectoryName;

    [Fact]
    public async Task DiscoverInstalledVersionsResolvesInheritedForgeMetadata()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var versionsDirectory = Path.Combine(minecraftDirectory, "versions");
        var vanillaDirectory = Path.Combine(versionsDirectory, "1.20.1");
        var forgeDirectory = Path.Combine(versionsDirectory, "forge-profile");
        Directory.CreateDirectory(vanillaDirectory);
        Directory.CreateDirectory(forgeDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(vanillaDirectory, "1.20.1.json"),
            """{"id":"1.20.1","type":"release","minecraftVersion":"1.20.1"}""");
        await File.WriteAllTextAsync(
            Path.Combine(forgeDirectory, "forge-profile.json"),
            """{"id":"forge-profile","inheritsFrom":"1.20.1","libraries":[{"name":"net.minecraftforge:forge:1.20.1-47.2.0"}]}""");
        var repository = new JsonGameInstanceRepository(new TestSettingsService(new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = minecraftDirectory
        }));

        var versions = await repository.DiscoverInstalledVersionsAsync(minecraftDirectory);

        var forge = Assert.Single(versions, version => version.VersionName == "forge-profile");
        Assert.Equal("1.20.1", forge.MinecraftVersion);
        Assert.Equal(LoaderKind.Forge, forge.Loader);
        Assert.Equal("47.2.0", forge.LoaderVersion);
    }

    [Fact]
    public async Task SaveAllAsyncWritesInstanceSettingsIntoVersionLauncherDirectory()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        var versionDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "demo-pack");
        repository.CreateInstanceDirectories(versionDirectory);

        await repository.SaveAllAsync(
        [
            new GameInstance
            {
                Id = "demo-pack",
                Name = "Demo Pack",
                MinecraftVersion = "1.20.1",
                VersionName = "demo-pack",
                Description = "stored beside the instance",
                InstanceDirectory = versionDirectory,
                BackupDirectory = Path.Combine(TempRoot, "backups", "demo-pack"),
                MemorySettingsMode = MemorySettingsMode.Auto,
                MemoryMb = 5120,
                JavaSettingsMode = LaunchSettingsMode.PerInstance,
                JavaSelectionMode = JavaSelectionMode.Manual,
                SelectedJavaExecutablePath = @"C:\Java\jdk-21\bin\java.exe"
            }
        ]);

        var settingsPath = Path.Combine(versionDirectory, InstanceMetadataDirectoryName, "instance-settings.json");
        Assert.True(File.Exists(settingsPath));
        Assert.False(File.Exists(Path.Combine(settings.DataDirectory, "instances.json")));

        var savedJson = await File.ReadAllTextAsync(settingsPath);
        using var document = JsonDocument.Parse(savedJson);
        Assert.Equal("Demo Pack", document.RootElement.GetProperty("Name").GetString());
        Assert.Equal("stored beside the instance", document.RootElement.GetProperty("Description").GetString());
        Assert.Equal(Path.Combine(TempRoot, "backups", "demo-pack"), document.RootElement.GetProperty("BackupDirectory").GetString());
        Assert.Equal((int)MemorySettingsMode.Auto, document.RootElement.GetProperty("MemorySettingsMode").GetInt32());
        Assert.Equal(5120, document.RootElement.GetProperty("MemoryMb").GetInt32());
        Assert.Equal((int)LaunchSettingsMode.PerInstance, document.RootElement.GetProperty("JavaSettingsMode").GetInt32());
        Assert.Equal((int)JavaSelectionMode.Manual, document.RootElement.GetProperty("JavaSelectionMode").GetInt32());
        Assert.Equal(@"C:\Java\jdk-21\bin\java.exe", document.RootElement.GetProperty("SelectedJavaExecutablePath").GetString());
    }

    [Fact]
    public async Task GetAllAsyncReadsInstanceSettingsFromVersionLauncherDirectory()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        var versionDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "demo-pack");
        repository.CreateInstanceDirectories(versionDirectory);
        var settingsPath = Path.Combine(versionDirectory, InstanceMetadataDirectoryName, "instance-settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);

        var storedInstance = new GameInstance
        {
            Id = "demo-pack",
            Name = "Demo Pack",
            MinecraftVersion = "1.20.1",
            VersionName = "demo-pack",
            Description = "loaded from instance folder",
            InstanceDirectory = "stale-path",
            BackupDirectory = Path.Combine(TempRoot, "backups", "demo-pack")
        };

        await using (var stream = File.Create(settingsPath))
        {
            await JsonSerializer.SerializeAsync(stream, storedInstance, new JsonSerializerOptions { WriteIndented = true });
        }

        var loaded = Assert.Single(await repository.GetAllAsync());
        Assert.Equal("Demo Pack", loaded.Name);
        Assert.Equal("loaded from instance folder", loaded.Description);
        Assert.Equal(versionDirectory, loaded.InstanceDirectory);
        Assert.Equal(Path.Combine(TempRoot, "backups", "demo-pack"), loaded.BackupDirectory);
    }

    [Fact]
    public async Task GetAllAsyncWithMinecraftDirectoryReadsWithoutLoadingSettingsDirectory()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, "wrong-minecraft")
        };
        var actualMinecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        var versionDirectory = Path.Combine(actualMinecraftDirectory, "versions", "demo-pack");
        repository.CreateInstanceDirectories(versionDirectory);
        var settingsPath = Path.Combine(versionDirectory, InstanceMetadataDirectoryName, "instance-settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);

        await using (var stream = File.Create(settingsPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                new GameInstance
                {
                    Id = "demo-pack",
                    Name = "Demo Pack",
                    MinecraftVersion = "1.20.1",
                    VersionName = "demo-pack",
                    InstanceDirectory = "stale-path"
                },
                new JsonSerializerOptions { WriteIndented = true });
        }

        var loaded = Assert.Single(await repository.GetAllAsync(actualMinecraftDirectory));

        Assert.Equal("demo-pack", loaded.Id);
        Assert.Equal(versionDirectory, loaded.InstanceDirectory);
    }

    [Fact]
    public async Task GetAllAsyncDefaultsMissingJavaSettingsToUseGlobalAutomatic()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        var versionDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "legacy-pack");
        repository.CreateInstanceDirectories(versionDirectory);
        var settingsPath = Path.Combine(versionDirectory, InstanceMetadataDirectoryName, "instance-settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);

        await File.WriteAllTextAsync(
            settingsPath,
            """
            {
              "Id": "legacy-pack",
              "Name": "Legacy Pack",
              "MinecraftVersion": "1.20.1",
              "VersionName": "legacy-pack"
            }
            """);

        var loaded = Assert.Single(await repository.GetAllAsync());

        Assert.Equal(LaunchSettingsMode.UseGlobal, loaded.JavaSettingsMode);
        Assert.Equal(JavaSelectionMode.Auto, loaded.JavaSelectionMode);
        Assert.Null(loaded.SelectedJavaExecutablePath);
    }

    [Fact]
    public async Task GetAllAsyncDefaultsMissingMemorySettingsToManual()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        var versionDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "legacy-memory-pack");
        repository.CreateInstanceDirectories(versionDirectory);
        var settingsPath = Path.Combine(versionDirectory, InstanceMetadataDirectoryName, "instance-settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);

        await File.WriteAllTextAsync(
            settingsPath,
            """
            {
              "Id": "legacy-memory-pack",
              "Name": "Legacy Memory Pack",
              "MinecraftVersion": "1.20.1",
              "VersionName": "legacy-memory-pack"
            }
            """);

        var loaded = Assert.Single(await repository.GetAllAsync());

        Assert.Equal(MemorySettingsMode.Manual, loaded.MemorySettingsMode);
        Assert.Equal(4096, loaded.MemoryMb);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData(".dfg")]
    [InlineData("name.")]
    [InlineData("../Pack")]
    [InlineData(@"..\Pack")]
    [InlineData(@"C:\Pack")]
    public void GetVersionDirectoryRejectsUnsafeVersionName(string versionName)
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);

        Assert.Throws<ArgumentException>(() => repository.GetVersionDirectory(settings.MinecraftDirectory, versionName));
    }

    [Fact]
    public void DeleteVersionDirectoryRejectsUnsafeVersionNameAndDoesNotDeleteOutsideVersions()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        var sentinelDirectory = Path.Combine(settings.MinecraftDirectory, "sentinel");
        Directory.CreateDirectory(sentinelDirectory);
        File.WriteAllText(Path.Combine(sentinelDirectory, "keep.txt"), "keep");

        Assert.Throws<ArgumentException>(() => repository.DeleteVersionDirectory(settings.MinecraftDirectory, @"..\sentinel"));

        Assert.True(File.Exists(Path.Combine(sentinelDirectory, "keep.txt")));
    }

    [Fact]
    public async Task RenameVersionAsyncRetriesDirectoryMoveWhenAccessFailsTemporarily()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var attempts = 0;
        var repository = new JsonGameInstanceRepository(
            settingsService,
            moveDirectoryAsync: (source, destination, cancellationToken) =>
            {
                attempts++;
                if (attempts < 3)
                    throw new IOException("temporarily locked");

                Directory.Move(source, destination);
                return Task.CompletedTask;
            });
        CreateVersionDirectory(settings.MinecraftDirectory, "Old Pack");
        var instance = new GameInstance
        {
            Id = "old",
            Name = "Old Pack",
            MinecraftVersion = "1.20.1",
            VersionName = "Old Pack",
            InstanceDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "Old Pack")
        };
        await repository.SaveAllAsync([instance]);

        await repository.RenameVersionAsync(
            settings.MinecraftDirectory, instance, "New Pack", null, DateTimeOffset.UtcNow);

        Assert.Equal(4, attempts); // three staging attempts, then the committed move to the final directory
        Assert.False(Directory.Exists(Path.Combine(settings.MinecraftDirectory, "versions", "Old Pack")));
        Assert.True(File.Exists(Path.Combine(settings.MinecraftDirectory, "versions", "New Pack", "New Pack.json")));
    }

    [Fact]
    public async Task RenameVersionAsyncPreservesSourceDirectoryWhenDirectoryMoveRetriesAreExhausted()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var attempts = 0;
        var repository = new JsonGameInstanceRepository(
            settingsService,
            moveDirectoryAsync: (_, _, _) =>
            {
                attempts++;
                throw new IOException("still locked");
            });
        CreateVersionDirectory(settings.MinecraftDirectory, "Old Pack");
        var instance = new GameInstance
        {
            Id = "old",
            Name = "Old Pack",
            MinecraftVersion = "1.20.1",
            VersionName = "Old Pack",
            InstanceDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "Old Pack")
        };
        await repository.SaveAllAsync([instance]);

        await Assert.ThrowsAsync<IOException>(() =>
            repository.RenameVersionAsync(
                settings.MinecraftDirectory, instance, "New Pack", null, DateTimeOffset.UtcNow));

        Assert.Equal(5, attempts);
        Assert.True(File.Exists(Path.Combine(settings.MinecraftDirectory, "versions", "Old Pack", "Old Pack.json")));
        Assert.False(File.Exists(Path.Combine(
            settings.MinecraftDirectory, "versions", "Old Pack", ".bhl-rename-pending.json")));
        Assert.False(Directory.Exists(Path.Combine(settings.MinecraftDirectory, "versions", "New Pack")));
    }

    [Fact]
    public async Task PendingRenameMarkerInSourceDirectoryIsRecovered()
    {
        var (settings, settingsService) = CreateSettings();
        var repository = new JsonGameInstanceRepository(settingsService);
        CreateVersionDirectory(settings.MinecraftDirectory, "Old Pack");
        var oldDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "Old Pack");
        Directory.CreateDirectory(Path.Combine(oldDirectory, "Old Pack-natives"));
        var instance = new GameInstance
        {
            Id = "old",
            Name = "Old Pack",
            MinecraftVersion = "1.20.1",
            VersionName = "Old Pack",
            InstanceDirectory = oldDirectory
        };
        await repository.SaveAllAsync([instance]);
        await File.WriteAllTextAsync(
            Path.Combine(oldDirectory, ".bhl-rename-pending.json"),
            """
            {
              "schemaVersion": 1,
              "transactionId": "a83f21c4000000000000000000000000",
              "instanceId": "old",
              "oldName": "Old Pack",
              "newName": "New Pack",
              "newIconSource": "icon.png",
              "updatedAtUtc": "2026-07-13T00:00:00Z"
            }
            """);

        await repository.RecoverPendingVersionRenamesAsync(settings.MinecraftDirectory);

        var newDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "New Pack");
        Assert.True(File.Exists(Path.Combine(newDirectory, "New Pack.json")));
        Assert.True(Directory.Exists(Path.Combine(newDirectory, "New Pack-natives")));
        Assert.False(File.Exists(Path.Combine(newDirectory, ".bhl-rename-pending.json")));
        var stored = Assert.Single(await repository.GetAllAsync());
        Assert.Equal("New Pack", stored.Name);
        Assert.Equal("New Pack", stored.VersionName);
        Assert.Equal("icon.png", stored.IconSource);
    }

    [Fact]
    public async Task PendingRenameDirectoryWithValidMetadataIsNeverDiscovered()
    {
        var (settings, settingsService) = CreateSettings();
        var repository = new JsonGameInstanceRepository(settingsService);
        var pendingDirectory = Path.Combine(
            settings.MinecraftDirectory,
            "versions",
            ".bhl-rename-pending-Test-a83f21c4");
        Directory.CreateDirectory(Path.Combine(pendingDirectory, "BHL"));
        await File.WriteAllTextAsync(Path.Combine(pendingDirectory, "Test.json"), """{"id":"Test","type":"release"}""");
        await File.WriteAllTextAsync(
            Path.Combine(pendingDirectory, "BHL", "instance-settings.json"),
            """{"id":"test","name":"Test","versionName":"Test"}""");

        Assert.Empty(await repository.DiscoverInstalledVersionsAsync(settings.MinecraftDirectory));
        Assert.Empty(await repository.GetAllAsync());
    }

    [Fact]
    public async Task RenameRetriesWithAnotherGuidWhenPendingDirectoryExists()
    {
        var (settings, settingsService) = CreateSettings();
        var guids = new Queue<Guid>(
        [
            Guid.ParseExact("a83f21c4000000000000000000000000", "N"),
            Guid.ParseExact("b94e32d5000000000000000000000000", "N")
        ]);
        var repository = new JsonGameInstanceRepository(
            settingsService,
            logger: null,
            moveDirectoryAsync: null,
            deletionGuidFactory: null,
            stageDeletionMove: null,
            deleteStagedDirectory: null,
            recycleStagedDirectory: null,
            renameGuidFactory: () => guids.Dequeue());
        CreateVersionDirectory(settings.MinecraftDirectory, "Old Pack");
        var oldDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "Old Pack");
        var instance = new GameInstance
        {
            Id = "old",
            Name = "Old Pack",
            MinecraftVersion = "1.20.1",
            VersionName = "Old Pack",
            InstanceDirectory = oldDirectory
        };
        await repository.SaveAllAsync([instance]);
        var versionsDirectory = Path.Combine(settings.MinecraftDirectory, "versions");
        Directory.CreateDirectory(Path.Combine(versionsDirectory, ".bhl-rename-pending-Old Pack-a83f21c4"));

        await repository.RenameVersionAsync(
            settings.MinecraftDirectory, instance, "New Pack", null, DateTimeOffset.UtcNow);

        Assert.True(Directory.Exists(Path.Combine(versionsDirectory, "New Pack")));
        Assert.True(Directory.Exists(Path.Combine(versionsDirectory, ".bhl-rename-pending-Old Pack-a83f21c4")));
        Assert.False(Directory.Exists(Path.Combine(versionsDirectory, ".bhl-rename-pending-Old Pack-b94e32d5")));
    }

    [Fact]
    public async Task RenameCancelsPreparationAfterThreePendingDirectoryConflicts()
    {
        var (settings, settingsService) = CreateSettings();
        var guids = new Queue<Guid>(
        [
            Guid.ParseExact("a83f21c4000000000000000000000000", "N"),
            Guid.ParseExact("b94e32d5000000000000000000000000", "N"),
            Guid.ParseExact("c05f43e6000000000000000000000000", "N")
        ]);
        var repository = new JsonGameInstanceRepository(
            settingsService,
            logger: null,
            moveDirectoryAsync: null,
            deletionGuidFactory: null,
            stageDeletionMove: null,
            deleteStagedDirectory: null,
            recycleStagedDirectory: null,
            renameGuidFactory: () => guids.Dequeue());
        CreateVersionDirectory(settings.MinecraftDirectory, "Old Pack");
        var oldDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "Old Pack");
        var instance = new GameInstance
        {
            Id = "old",
            Name = "Old Pack",
            MinecraftVersion = "1.20.1",
            VersionName = "Old Pack",
            InstanceDirectory = oldDirectory
        };
        await repository.SaveAllAsync([instance]);
        var versionsDirectory = Path.Combine(settings.MinecraftDirectory, "versions");
        foreach (var suffix in new[] { "a83f21c4", "b94e32d5", "c05f43e6" })
            Directory.CreateDirectory(Path.Combine(versionsDirectory, $".bhl-rename-pending-Old Pack-{suffix}"));

        await Assert.ThrowsAsync<IOException>(() => repository.RenameVersionAsync(
            settings.MinecraftDirectory, instance, "New Pack", null, DateTimeOffset.UtcNow));

        Assert.True(Directory.Exists(oldDirectory));
        Assert.True(File.Exists(Path.Combine(oldDirectory, "Old Pack.json")));
        Assert.False(File.Exists(Path.Combine(oldDirectory, ".bhl-rename-pending.json")));
        Assert.False(Directory.Exists(Path.Combine(versionsDirectory, "New Pack")));
    }

    [Fact]
    public async Task StageVersionForDeletionRetriesWhenGeneratedDestinationAlreadyExists()
    {
        var (settings, settingsService) = CreateSettings();
        CreateVersionDirectory(settings.MinecraftDirectory, "Test");
        var versionsDirectory = Path.Combine(settings.MinecraftDirectory, "versions");
        Directory.CreateDirectory(Path.Combine(versionsDirectory, ".bhl-delete-pending-Test-a83f21c4"));
        var guids = new Queue<Guid>(
        [
            Guid.ParseExact("a83f21c4000000000000000000000000", "N"),
            Guid.ParseExact("b94e32d5000000000000000000000000", "N")
        ]);
        var repository = new JsonGameInstanceRepository(
            settingsService,
            logger: null,
            moveDirectoryAsync: null,
            deletionGuidFactory: () => guids.Dequeue(),
            stageDeletionMove: Directory.Move,
            deleteStagedDirectory: null);

        var stagedDirectory = await repository.StageVersionForDeletionAsync(settings.MinecraftDirectory, "Test");

        Assert.EndsWith(".bhl-delete-pending-Test-b94e32d5", stagedDirectory, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(versionsDirectory, "Test")));
        Assert.True(Directory.Exists(stagedDirectory));
    }

    [Fact]
    public async Task StageVersionForDeletionRetriesWhenDestinationAppearsDuringMove()
    {
        var (settings, settingsService) = CreateSettings();
        CreateVersionDirectory(settings.MinecraftDirectory, "Test");
        var guids = new Queue<Guid>(
        [
            Guid.ParseExact("a83f21c4000000000000000000000000", "N"),
            Guid.ParseExact("b94e32d5000000000000000000000000", "N")
        ]);
        var moves = 0;
        var repository = new JsonGameInstanceRepository(
            settingsService,
            logger: null,
            moveDirectoryAsync: null,
            deletionGuidFactory: () => guids.Dequeue(),
            stageDeletionMove: (source, destination) =>
            {
                moves++;
                if (moves == 1)
                {
                    Directory.CreateDirectory(destination);
                    throw new IOException("destination won the race");
                }

                Directory.Move(source, destination);
            },
            deleteStagedDirectory: null);

        var stagedDirectory = await repository.StageVersionForDeletionAsync(settings.MinecraftDirectory, "Test");

        Assert.Equal(2, moves);
        Assert.EndsWith(".bhl-delete-pending-Test-b94e32d5", stagedDirectory, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StageVersionForDeletionStopsAfterThreeDestinationCollisions()
    {
        var (settings, settingsService) = CreateSettings();
        CreateVersionDirectory(settings.MinecraftDirectory, "Test");
        var versionsDirectory = Path.Combine(settings.MinecraftDirectory, "versions");
        var values = new[] { "a83f21c4", "b94e32d5", "c05f43e6" };
        foreach (var value in values)
            Directory.CreateDirectory(Path.Combine(versionsDirectory, $".bhl-delete-pending-Test-{value}"));
        var guids = new Queue<Guid>(values.Select(value => Guid.ParseExact(value + new string('0', 24), "N")));
        var moveAttempts = 0;
        var repository = new JsonGameInstanceRepository(
            settingsService,
            logger: null,
            moveDirectoryAsync: null,
            deletionGuidFactory: () => guids.Dequeue(),
            stageDeletionMove: (_, _) => moveAttempts++,
            deleteStagedDirectory: null);

        await Assert.ThrowsAsync<IOException>(() =>
            repository.StageVersionForDeletionAsync(settings.MinecraftDirectory, "Test"));

        Assert.Equal(0, moveAttempts);
        Assert.True(Directory.Exists(Path.Combine(versionsDirectory, "Test")));
    }

    [Fact]
    public async Task StageVersionForDeletionDoesNotRetryNonCollisionMoveFailure()
    {
        var (settings, settingsService) = CreateSettings();
        CreateVersionDirectory(settings.MinecraftDirectory, "Test");
        var moveAttempts = 0;
        var repository = new JsonGameInstanceRepository(
            settingsService,
            logger: null,
            moveDirectoryAsync: null,
            deletionGuidFactory: () => Guid.ParseExact("a83f21c4000000000000000000000000", "N"),
            stageDeletionMove: (_, _) =>
            {
                moveAttempts++;
                throw new IOException("source is locked");
            },
            deleteStagedDirectory: null);

        await Assert.ThrowsAsync<IOException>(() =>
            repository.StageVersionForDeletionAsync(settings.MinecraftDirectory, "Test"));

        Assert.Equal(1, moveAttempts);
        Assert.True(Directory.Exists(Path.Combine(settings.MinecraftDirectory, "versions", "Test")));
    }

    [Fact]
    public async Task InstanceScansIgnorePendingDeletionDirectoryWithValidMetadata()
    {
        var (settings, settingsService) = CreateSettings();
        var name = ".BHL-DELETE-PENDING-Test-a83f21c4";
        var directory = Path.Combine(settings.MinecraftDirectory, "versions", name);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, $"{name}.json"),
            $$"""{"id":"{{name}}","type":"release","minecraftVersion":"1.20.1"}""");
        var metadataDirectory = Path.Combine(directory, InstanceMetadataDirectoryName);
        Directory.CreateDirectory(metadataDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(metadataDirectory, "instance-settings.json"),
            """{"Id":"deleted","Name":"Deleted","MinecraftVersion":"1.20.1","VersionName":"Test"}""");
        var repository = new JsonGameInstanceRepository(settingsService);

        Assert.Empty(await repository.GetAllAsync());
        Assert.Empty(await repository.DiscoverInstalledVersionsAsync(settings.MinecraftDirectory));
    }

    [Fact]
    public async Task CleanupStagedVersionDirectoriesContinuesAfterIndividualFailure()
    {
        var (settings, settingsService) = CreateSettings();
        var versionsDirectory = Path.Combine(settings.MinecraftDirectory, "versions");
        var retained = Path.Combine(versionsDirectory, ".bhl-delete-pending-Retained-a83f21c4");
        var removed = Path.Combine(versionsDirectory, ".BHL-DELETE-PENDING-Removed-b94e32d5");
        Directory.CreateDirectory(retained);
        Directory.CreateDirectory(removed);
        var repository = new JsonGameInstanceRepository(
            settingsService,
            logger: null,
            moveDirectoryAsync: null,
            deletionGuidFactory: null,
            stageDeletionMove: null,
            deleteStagedDirectory: (path, recursive) =>
            {
                if (string.Equals(path, retained, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("still locked");
                Directory.Delete(path, recursive);
            },
            recycleStagedDirectory: _ => throw new IOException("recycle bin unavailable"));

        await repository.CleanupStagedVersionDirectoriesAsync(settings.MinecraftDirectory);

        Assert.True(Directory.Exists(retained));
        Assert.False(Directory.Exists(removed));
    }

    [Fact]
    public async Task TryDeleteStagedVersionDirectoryUsesRecycleBinBeforePermanentDeletion()
    {
        var (settings, settingsService) = CreateSettings();
        var stagedDirectory = Path.Combine(
            settings.MinecraftDirectory,
            "versions",
            ".bhl-delete-pending-Test-a83f21c4");
        Directory.CreateDirectory(stagedDirectory);
        var recycleAttempts = 0;
        var permanentDeleteAttempts = 0;
        var repository = new JsonGameInstanceRepository(
            settingsService,
            logger: null,
            moveDirectoryAsync: null,
            deletionGuidFactory: null,
            stageDeletionMove: null,
            deleteStagedDirectory: (_, _) => permanentDeleteAttempts++,
            recycleStagedDirectory: path =>
            {
                recycleAttempts++;
                Directory.Delete(path, recursive: true);
            });

        Assert.True(await repository.TryDeleteStagedVersionDirectoryAsync(
            settings.MinecraftDirectory,
            stagedDirectory));

        Assert.Equal(1, recycleAttempts);
        Assert.Equal(0, permanentDeleteAttempts);
        Assert.False(Directory.Exists(stagedDirectory));
    }

    private (LauncherSettings Settings, TestSettingsService SettingsService) CreateSettings()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        return (settings, new TestSettingsService(settings));
    }

    private static void CreateVersionDirectory(string minecraftDirectory, string versionName)
    {
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        Directory.CreateDirectory(versionDirectory);
        File.WriteAllText(
            Path.Combine(versionDirectory, $"{versionName}.json"),
            $$"""
            {
              "id": "{{versionName}}",
              "jar": "{{versionName}}"
            }
            """);
        File.WriteAllText(Path.Combine(versionDirectory, $"{versionName}.jar"), "fake jar");
    }
}
