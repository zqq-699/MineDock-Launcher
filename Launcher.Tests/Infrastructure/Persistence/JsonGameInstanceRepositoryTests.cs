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
using Launcher.Application.Services;
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
    public async Task DiscoverInstalledVersionsResolvesFlattenedForgeMetadataFromFmlLoader()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", "Better MC");
        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(versionDirectory, "Better MC.json"),
            """
            {
              "id": "Better MC",
              "launcher": { "minecraftVersion": "1.20.1" },
              "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher",
              "libraries": [
                { "name": "net.minecraftforge:fmlloader:1.20.1-47.4.20" }
              ],
              "arguments": {
                "game": [ "--fml.mcVersion", "1.20.1", "--fml.forgeVersion", "47.4.20" ]
              }
            }
            """);
        var repository = new JsonGameInstanceRepository(new TestSettingsService(new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = minecraftDirectory
        }));

        var versions = await repository.DiscoverInstalledVersionsAsync(minecraftDirectory);

        var forge = Assert.Single(versions);
        Assert.Equal("1.20.1", forge.MinecraftVersion);
        Assert.Equal(LoaderKind.Forge, forge.Loader);
        Assert.Equal("47.4.20", forge.LoaderVersion);
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
    public async Task SaveAllAsyncDoesNotDeleteSettingsForOmittedInstances()
    {
        var (settings, settingsService) = CreateSettings();
        var repository = new JsonGameInstanceRepository(settingsService);
        var firstDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "First");
        var secondDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "Second");
        repository.CreateInstanceDirectories(firstDirectory);
        repository.CreateInstanceDirectories(secondDirectory);
        var first = CreateInstance("first", "First", firstDirectory);
        var second = CreateInstance("second", "Second", secondDirectory);
        await repository.SaveAllAsync([first, second]);

        await repository.SaveAllAsync([first]);

        var secondSettingsPath = Path.Combine(
            secondDirectory,
            InstanceMetadataDirectoryName,
            "instance-settings.json");
        Assert.True(File.Exists(secondSettingsPath));
        Assert.Contains(await repository.GetAllAsync(), instance => instance.Id == "second");
    }

    [Fact]
    public async Task SaveAllAsyncWithStaleSnapshotDoesNotDeleteInstanceSavedByAnotherRepository()
    {
        var (settings, settingsService) = CreateSettings();
        var staleRepository = new JsonGameInstanceRepository(settingsService);
        var currentRepository = new JsonGameInstanceRepository(settingsService);
        var existingDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "Existing");
        staleRepository.CreateInstanceDirectories(existingDirectory);
        await staleRepository.SaveAllAsync([CreateInstance("existing", "Existing", existingDirectory)]);
        var staleSnapshot = await staleRepository.GetAllAsync();

        var newDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "NewlyInstalled");
        currentRepository.CreateInstanceDirectories(newDirectory);
        var newlyInstalled = CreateInstance("newly-installed", "NewlyInstalled", newDirectory);
        await currentRepository.SaveAllAsync([.. staleSnapshot, newlyInstalled]);

        await staleRepository.SaveAllAsync(staleSnapshot);

        var newSettingsPath = Path.Combine(
            newDirectory,
            InstanceMetadataDirectoryName,
            "instance-settings.json");
        Assert.True(File.Exists(newSettingsPath));
        var reloaded = await new JsonGameInstanceRepository(settingsService).GetAllAsync();
        Assert.Contains(reloaded, instance => instance.Id == "newly-installed");
    }

    [Fact]
    public async Task SaveAllAsyncDoesNotOverwriteNewerSettingsForSameInstance()
    {
        var (settings, settingsService) = CreateSettings();
        var staleRepository = new JsonGameInstanceRepository(settingsService);
        var currentRepository = new JsonGameInstanceRepository(settingsService);
        var directory = Path.Combine(settings.MinecraftDirectory, "versions", "Test");
        staleRepository.CreateInstanceDirectories(directory);
        var original = CreateInstance("test", "Test", directory);
        original.Description = "original";
        original.UpdatedAt = new DateTimeOffset(2026, 7, 13, 1, 0, 0, TimeSpan.Zero);
        await staleRepository.SaveAllAsync([original]);
        var staleSnapshot = Assert.Single(await staleRepository.GetAllAsync());

        var current = Assert.Single(await currentRepository.GetAllAsync());
        current.Description = "newer settings";
        current.MemoryMb = 8192;
        current.UpdatedAt = new DateTimeOffset(2026, 7, 13, 2, 0, 0, TimeSpan.Zero);
        await currentRepository.UpdateInstanceAsync(settings.MinecraftDirectory, current);

        staleSnapshot.Description = "stale overwrite";
        staleSnapshot.MemoryMb = 2048;
        await staleRepository.SaveAllAsync([staleSnapshot]);

        var reloaded = Assert.Single(await currentRepository.GetAllAsync());
        Assert.Equal("newer settings", reloaded.Description);
        Assert.Equal(8192, reloaded.MemoryMb);
        Assert.Equal(current.UpdatedAt, reloaded.UpdatedAt);
    }

    [Fact]
    public async Task SaveAllAsyncDoesNotOverwriteSameNameReplacementInstance()
    {
        var (settings, settingsService) = CreateSettings();
        var staleRepository = new JsonGameInstanceRepository(settingsService);
        var currentRepository = new JsonGameInstanceRepository(settingsService);
        var directory = await CreateStoredInstanceAsync(staleRepository, settings.MinecraftDirectory, "Test");
        var staleInstance = Assert.Single(await staleRepository.GetAllAsync());
        Directory.Delete(directory, recursive: true);
        CreateVersionDirectory(settings.MinecraftDirectory, "Test");
        var replacement = CreateInstance("replacement", "Test", directory);
        replacement.Description = "keep replacement";
        await currentRepository.SaveAllAsync([replacement]);

        staleInstance.Description = "stale overwrite";
        await staleRepository.SaveAllAsync([staleInstance]);

        var reloaded = Assert.Single(await currentRepository.GetAllAsync());
        Assert.Equal("replacement", reloaded.Id);
        Assert.Equal("keep replacement", reloaded.Description);
    }

    [Fact]
    public async Task UpdateInstanceAsyncRejectsSameNameReplacementInstance()
    {
        var (settings, settingsService) = CreateSettings();
        var repository = new JsonGameInstanceRepository(settingsService);
        var directory = await CreateStoredInstanceAsync(repository, settings.MinecraftDirectory, "Test");
        var staleInstance = Assert.Single(await repository.GetAllAsync());
        Directory.Delete(directory, recursive: true);
        CreateVersionDirectory(settings.MinecraftDirectory, "Test");
        var replacement = CreateInstance("replacement", "Test", directory);
        replacement.IconSource = "replacement.png";
        await repository.SaveAllAsync([replacement]);

        staleInstance.IconSource = "stale.png";
        await Assert.ThrowsAsync<GameInstanceMutationConflictException>(() => repository.UpdateInstanceAsync(
            settings.MinecraftDirectory,
            staleInstance));

        var reloaded = Assert.Single(await repository.GetAllAsync());
        Assert.Equal("replacement", reloaded.Id);
        Assert.Equal("replacement.png", reloaded.IconSource);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenameVersionAsyncRejectsOccupiedDestinationBeforeStaging(bool destinationIsDirectory)
    {
        var (settings, settingsService) = CreateSettings();
        var repository = new JsonGameInstanceRepository(settingsService);
        var oldDirectory = await CreateStoredInstanceAsync(repository, settings.MinecraftDirectory, "Old Pack");
        var instance = Assert.Single(await repository.GetAllAsync());
        var destination = Path.Combine(settings.MinecraftDirectory, "versions", "New Pack");
        if (destinationIsDirectory)
        {
            Directory.CreateDirectory(destination);
            await File.WriteAllTextAsync(Path.Combine(destination, "keep.txt"), "keep");
        }
        else
        {
            await File.WriteAllTextAsync(destination, "keep");
        }

        await Assert.ThrowsAsync<InstanceInstallNameConflictException>(() => repository.RenameVersionAsync(
            settings.MinecraftDirectory,
            instance,
            "New Pack",
            null,
            DateTimeOffset.UtcNow));

        Assert.True(Directory.Exists(oldDirectory));
        Assert.True(File.Exists(Path.Combine(oldDirectory, "Old Pack.json")));
        Assert.False(File.Exists(Path.Combine(oldDirectory, ".bhl-rename-pending.json")));
        Assert.Empty(Directory.EnumerateDirectories(
            Path.Combine(settings.MinecraftDirectory, "versions"),
            ".bhl-rename-pending-*"));
        if (destinationIsDirectory)
            Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(destination, "keep.txt")));
        else
            Assert.Equal("keep", await File.ReadAllTextAsync(destination));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenameVersionAsyncRollsBackWhenDestinationAppearsDuringFinalMove(bool destinationIsDirectory)
    {
        var (settings, settingsService) = CreateSettings();
        var moveAttempts = 0;
        var repository = new JsonGameInstanceRepository(
            settingsService,
            moveDirectoryAsync: (source, destination, _) =>
            {
                moveAttempts++;
                if (string.Equals(Path.GetFileName(destination), "New Pack", StringComparison.OrdinalIgnoreCase))
                {
                    if (destinationIsDirectory)
                    {
                        Directory.CreateDirectory(destination);
                        File.WriteAllText(Path.Combine(destination, "keep.txt"), "keep");
                    }
                    else
                    {
                        File.WriteAllText(destination, "keep");
                    }

                    throw new IOException("destination won the race");
                }

                Directory.Move(source, destination);
                return Task.CompletedTask;
            });
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

        await Assert.ThrowsAsync<InstanceInstallNameConflictException>(() => repository.RenameVersionAsync(
            settings.MinecraftDirectory,
            instance,
            "New Pack",
            null,
            DateTimeOffset.UtcNow));

        Assert.Equal(3, moveAttempts);
        Assert.True(File.Exists(Path.Combine(oldDirectory, "Old Pack.json")));
        Assert.True(File.Exists(Path.Combine(oldDirectory, "Old Pack.jar")));
        Assert.True(Directory.Exists(Path.Combine(oldDirectory, "Old Pack-natives")));
        Assert.False(File.Exists(Path.Combine(oldDirectory, ".bhl-rename-pending.json")));
        Assert.Empty(Directory.EnumerateDirectories(
            Path.Combine(settings.MinecraftDirectory, "versions"),
            ".bhl-rename-pending-*"));
        using (var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(oldDirectory, "Old Pack.json"))))
        {
            Assert.Equal("Old Pack", document.RootElement.GetProperty("id").GetString());
            Assert.Equal("Old Pack", document.RootElement.GetProperty("jar").GetString());
        }
        Assert.Equal("Old Pack", Assert.Single(await repository.GetAllAsync()).VersionName);

        var destination = Path.Combine(settings.MinecraftDirectory, "versions", "New Pack");
        if (destinationIsDirectory)
            Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(destination, "keep.txt")));
        else
            Assert.Equal("keep", await File.ReadAllTextAsync(destination));
    }

    [Fact]
    public async Task PendingRenameRecoveryRollsBackWhenDestinationIsAFile()
    {
        var (settings, settingsService) = CreateSettings();
        var repository = new JsonGameInstanceRepository(settingsService);
        var oldDirectory = await CreateStoredInstanceAsync(repository, settings.MinecraftDirectory, "Old Pack");
        Directory.CreateDirectory(Path.Combine(oldDirectory, "Old Pack-natives"));
        var versionsDirectory = Path.Combine(settings.MinecraftDirectory, "versions");
        var pendingDirectory = Path.Combine(versionsDirectory, ".bhl-rename-pending-Old Pack-a83f21c4");
        await File.WriteAllTextAsync(
            Path.Combine(oldDirectory, ".bhl-rename-pending.json"),
            CreateRenameMarkerJson("old pack", "Old Pack", "New Pack"));
        Directory.Move(oldDirectory, pendingDirectory);
        File.Move(Path.Combine(pendingDirectory, "Old Pack.json"), Path.Combine(pendingDirectory, "New Pack.json"));
        File.Move(Path.Combine(pendingDirectory, "Old Pack.jar"), Path.Combine(pendingDirectory, "New Pack.jar"));
        Directory.Move(
            Path.Combine(pendingDirectory, "Old Pack-natives"),
            Path.Combine(pendingDirectory, "New Pack-natives"));
        await File.WriteAllTextAsync(
            Path.Combine(pendingDirectory, "New Pack.json"),
            """{"id":"New Pack","jar":"New Pack"}""");
        var destination = Path.Combine(versionsDirectory, "New Pack");
        await File.WriteAllTextAsync(destination, "keep");

        await repository.RecoverPendingVersionRenamesAsync(settings.MinecraftDirectory);
        await repository.RecoverPendingVersionRenamesAsync(settings.MinecraftDirectory);

        Assert.Equal("keep", await File.ReadAllTextAsync(destination));
        Assert.True(File.Exists(Path.Combine(oldDirectory, "Old Pack.json")));
        Assert.True(File.Exists(Path.Combine(oldDirectory, "Old Pack.jar")));
        Assert.True(Directory.Exists(Path.Combine(oldDirectory, "Old Pack-natives")));
        Assert.False(Directory.Exists(pendingDirectory));
        Assert.False(File.Exists(Path.Combine(oldDirectory, ".bhl-rename-pending.json")));
        Assert.Equal("Old Pack", Assert.Single(await repository.GetAllAsync()).VersionName);
    }

    [Fact]
    public async Task PendingRenameRecoveryCompletesRollbackAfterDirectoryWasAlreadyMovedBack()
    {
        var (settings, settingsService) = CreateSettings();
        var repository = new JsonGameInstanceRepository(settingsService);
        var oldDirectory = await CreateStoredInstanceAsync(repository, settings.MinecraftDirectory, "Old Pack");
        var markerPath = Path.Combine(oldDirectory, ".bhl-rename-pending.json");
        await File.WriteAllTextAsync(
            markerPath,
            CreateRenameMarkerJson("old pack", "Old Pack", "New Pack"));
        var destination = Path.Combine(settings.MinecraftDirectory, "versions", "New Pack");
        await File.WriteAllTextAsync(destination, "keep");

        await repository.RecoverPendingVersionRenamesAsync(settings.MinecraftDirectory);
        await repository.RecoverPendingVersionRenamesAsync(settings.MinecraftDirectory);

        Assert.Equal("keep", await File.ReadAllTextAsync(destination));
        Assert.True(Directory.Exists(oldDirectory));
        Assert.True(File.Exists(Path.Combine(oldDirectory, "Old Pack.json")));
        Assert.False(File.Exists(markerPath));
        Assert.Equal("Old Pack", Assert.Single(await repository.GetAllAsync()).VersionName);
    }

    [Fact]
    public async Task PendingRenameRollbackQuarantinesMarkerWhenDeletionFails()
    {
        var (settings, settingsService) = CreateSettings();
        var repository = new JsonGameInstanceRepository(
            settingsService,
            logger: null,
            moveDirectoryAsync: null,
            deletionGuidFactory: null,
            stageDeletionMove: null,
            deleteStagedDirectory: null,
            recycleStagedDirectory: null,
            renameGuidFactory: null,
            deleteRenameMarker: _ => throw new IOException("marker is locked"));
        var oldDirectory = await CreateStoredInstanceAsync(repository, settings.MinecraftDirectory, "Old Pack");
        var markerPath = Path.Combine(oldDirectory, ".bhl-rename-pending.json");
        await File.WriteAllTextAsync(
            markerPath,
            CreateRenameMarkerJson("old pack", "Old Pack", "New Pack"));
        var destination = Path.Combine(settings.MinecraftDirectory, "versions", "New Pack");
        await File.WriteAllTextAsync(destination, "keep");

        await repository.RecoverPendingVersionRenamesAsync(settings.MinecraftDirectory);

        Assert.False(File.Exists(markerPath));
        Assert.True(File.Exists(Path.Combine(oldDirectory, ".bhl-rename-aborted.json")));
        Assert.Equal("keep", await File.ReadAllTextAsync(destination));
        Assert.Equal("Old Pack", Assert.Single(await repository.GetAllAsync()).VersionName);
    }

    [Fact]
    public async Task RenameVersionAsyncRejectsSameNameReplacementBeforeWritingMarkerOrMoving()
    {
        var (settings, settingsService) = CreateSettings();
        var moveAttempts = 0;
        var repository = new JsonGameInstanceRepository(
            settingsService,
            moveDirectoryAsync: (source, destination, _) =>
            {
                moveAttempts++;
                Directory.Move(source, destination);
                return Task.CompletedTask;
            });
        var directory = await CreateStoredInstanceAsync(repository, settings.MinecraftDirectory, "Old Pack");
        var staleInstance = Assert.Single(await repository.GetAllAsync());
        Directory.Delete(directory, recursive: true);
        CreateVersionDirectory(settings.MinecraftDirectory, "Old Pack");
        WriteInstanceSettings(directory, "replacement");
        await File.WriteAllTextAsync(Path.Combine(directory, "replacement.txt"), "keep");

        await Assert.ThrowsAsync<GameInstanceMutationConflictException>(() => repository.RenameVersionAsync(
            settings.MinecraftDirectory,
            staleInstance,
            "New Pack",
            null,
            DateTimeOffset.UtcNow));

        Assert.Equal(0, moveAttempts);
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(directory, "replacement.txt")));
        Assert.False(File.Exists(Path.Combine(directory, ".bhl-rename-pending.json")));
        Assert.False(Directory.Exists(Path.Combine(settings.MinecraftDirectory, "versions", "New Pack")));
    }

    [Fact]
    public async Task RenameVersionAsyncNeverMovesReplacementThatAppearsAfterValidation()
    {
        var (settings, settingsService) = CreateSettings();
        var versionsDirectory = Path.Combine(settings.MinecraftDirectory, "versions");
        var displacedOriginal = Path.Combine(versionsDirectory, "displaced-original");
        var hookCount = 0;
        var repository = new JsonGameInstanceRepository(
            settingsService,
            logger: null,
            moveDirectoryAsync: null,
            deletionGuidFactory: null,
            stageDeletionMove: null,
            deleteStagedDirectory: null,
            beforeOwnedRenameMove: (source, _) =>
            {
                hookCount++;
                Directory.Move(source, displacedOriginal);
                CreateVersionDirectory(settings.MinecraftDirectory, "Old Pack");
                WriteInstanceSettings(source, "replacement");
                File.WriteAllText(Path.Combine(source, "replacement.txt"), "keep");
            });
        var oldDirectory = await CreateStoredInstanceAsync(repository, settings.MinecraftDirectory, "Old Pack");
        var instance = Assert.Single(await repository.GetAllAsync());

        await Assert.ThrowsAsync<GameInstanceMutationConflictException>(() => repository.RenameVersionAsync(
            settings.MinecraftDirectory,
            instance,
            "New Pack",
            null,
            DateTimeOffset.UtcNow));

        Assert.Equal(1, hookCount);
        Assert.True(Directory.Exists(oldDirectory));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(oldDirectory, "replacement.txt")));
        Assert.False(File.Exists(Path.Combine(oldDirectory, ".bhl-rename-pending.json")));
        Assert.False(Directory.Exists(Path.Combine(versionsDirectory, "New Pack")));
        Assert.True(Directory.Exists(displacedOriginal));
    }

    [Fact]
    public async Task PendingRenameWithDifferentInstanceIdIsQuarantinedWithoutRollbackMoveOrArtifactChanges()
    {
        var (settings, settingsService) = CreateSettings();
        var moveAttempts = 0;
        var repository = new JsonGameInstanceRepository(
            settingsService,
            moveDirectoryAsync: (_, _, _) =>
            {
                moveAttempts++;
                throw new InvalidOperationException("A mismatched instance must never be moved.");
            });
        var pendingDirectory = Path.Combine(
            settings.MinecraftDirectory,
            "versions",
            ".bhl-rename-pending-Old Pack-a83f21c4");
        Directory.CreateDirectory(pendingDirectory);
        WriteInstanceSettings(pendingDirectory, "replacement");
        var jsonPath = Path.Combine(pendingDirectory, "Old Pack.json");
        await File.WriteAllTextAsync(jsonPath, """{"id":"Old Pack","jar":"Old Pack","sentinel":"keep"}""");
        await File.WriteAllTextAsync(
            Path.Combine(pendingDirectory, ".bhl-rename-pending.json"),
            CreateRenameMarkerJson("expected", "Old Pack", "New Pack"));

        await repository.RecoverPendingVersionRenamesAsync(settings.MinecraftDirectory);

        Assert.Equal(0, moveAttempts);
        Assert.True(Directory.Exists(pendingDirectory));
        Assert.Equal(
            """{"id":"Old Pack","jar":"Old Pack","sentinel":"keep"}""",
            await File.ReadAllTextAsync(jsonPath));
        Assert.False(File.Exists(Path.Combine(pendingDirectory, ".bhl-rename-pending.json")));
        Assert.True(File.Exists(Path.Combine(pendingDirectory, ".bhl-rename-aborted.json")));

        await repository.RecoverPendingVersionRenamesAsync(settings.MinecraftDirectory);
        Assert.Equal(0, moveAttempts);
        Assert.True(Directory.Exists(pendingDirectory));
    }

    [Fact]
    public async Task RenameRollbackDoesNotMoveOwnedPendingDirectoryOverDifferentInstanceAtOldPath()
    {
        var (settings, settingsService) = CreateSettings();
        var versionsDirectory = Path.Combine(settings.MinecraftDirectory, "versions");
        var moveAttempts = 0;
        var repository = new JsonGameInstanceRepository(
            settingsService,
            moveDirectoryAsync: (_, _, _) =>
            {
                moveAttempts++;
                throw new InvalidOperationException("Rollback must not move when the old path is occupied.");
            });
        var oldDirectory = await CreateStoredInstanceAsync(
            new JsonGameInstanceRepository(settingsService),
            settings.MinecraftDirectory,
            "Old Pack");
        await File.WriteAllTextAsync(
            Path.Combine(oldDirectory, ".bhl-rename-pending.json"),
            CreateRenameMarkerJson("old pack", "Old Pack", "New Pack"));
        var pendingDirectory = Path.Combine(versionsDirectory, ".bhl-rename-pending-Old Pack-a83f21c4");
        Directory.Move(oldDirectory, pendingDirectory);
        Directory.CreateDirectory(oldDirectory);
        WriteInstanceSettings(oldDirectory, "replacement");
        await File.WriteAllTextAsync(Path.Combine(oldDirectory, "replacement.txt"), "keep");
        await File.WriteAllTextAsync(Path.Combine(versionsDirectory, "New Pack"), "occupied");

        await repository.RecoverPendingVersionRenamesAsync(settings.MinecraftDirectory);
        await repository.RecoverPendingVersionRenamesAsync(settings.MinecraftDirectory);

        Assert.Equal(0, moveAttempts);
        Assert.True(Directory.Exists(pendingDirectory));
        Assert.True(Directory.Exists(oldDirectory));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(oldDirectory, "replacement.txt")));
        Assert.True(File.Exists(Path.Combine(pendingDirectory, ".bhl-rename-pending.json")));
    }

    [Fact]
    public async Task RenameDoesNotRollbackDifferentInstanceInjectedAfterFinalMove()
    {
        var (settings, settingsService) = CreateSettings();
        var versionsDirectory = Path.Combine(settings.MinecraftDirectory, "versions");
        var displacedOriginal = Path.Combine(versionsDirectory, "displaced-original");
        var moveAttempts = 0;
        var repository = new JsonGameInstanceRepository(
            settingsService,
            moveDirectoryAsync: (source, destination, _) =>
            {
                moveAttempts++;
                Directory.Move(source, destination);
                if (string.Equals(Path.GetFileName(destination), "New Pack", StringComparison.OrdinalIgnoreCase))
                {
                    Directory.Move(destination, displacedOriginal);
                    CreateVersionDirectory(settings.MinecraftDirectory, "New Pack");
                    WriteInstanceSettings(destination, "replacement");
                    File.Copy(
                        Path.Combine(displacedOriginal, ".bhl-rename-pending.json"),
                        Path.Combine(destination, ".bhl-rename-pending.json"));
                    File.WriteAllText(Path.Combine(destination, "replacement.txt"), "keep");
                }

                return Task.CompletedTask;
            });
        var oldDirectory = await CreateStoredInstanceAsync(repository, settings.MinecraftDirectory, "Old Pack");
        var instance = Assert.Single(await repository.GetAllAsync());

        await Assert.ThrowsAsync<GameInstanceMutationConflictException>(() => repository.RenameVersionAsync(
            settings.MinecraftDirectory,
            instance,
            "New Pack",
            null,
            DateTimeOffset.UtcNow));

        var replacementDirectory = Path.Combine(versionsDirectory, "New Pack");
        Assert.Equal(2, moveAttempts);
        Assert.False(Directory.Exists(oldDirectory));
        Assert.True(Directory.Exists(displacedOriginal));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(replacementDirectory, "replacement.txt")));
        Assert.True(File.Exists(Path.Combine(replacementDirectory, ".bhl-rename-aborted.json")));
        Assert.False(File.Exists(Path.Combine(replacementDirectory, ".bhl-rename-pending.json")));
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

    [Theory]
    [InlineData("{")]
    [InlineData("""{"schemaVersion":1,"transactionId":"a83f21c4000000000000000000000000","oldName":"Test","newName":"Renamed"}""")]
    [InlineData("""{"schemaVersion":1,"transactionId":"a83f21c4000000000000000000000000","instanceId":"test","oldName":"Other","newName":"Renamed","updatedAtUtc":"2026-07-13T00:00:00Z"}""")]
    public async Task InvalidRenameMarkerInOrdinaryDirectoryDoesNotHideInstance(string markerJson)
    {
        var (settings, settingsService) = CreateSettings();
        var repository = new JsonGameInstanceRepository(settingsService);
        var directory = await CreateStoredInstanceAsync(repository, settings.MinecraftDirectory, "Test");
        var markerPath = Path.Combine(directory, ".bhl-rename-pending.json");
        await File.WriteAllTextAsync(markerPath, markerJson);

        await repository.RecoverPendingVersionRenamesAsync(settings.MinecraftDirectory);

        Assert.False(File.Exists(markerPath));
        Assert.True(File.Exists(Path.Combine(directory, ".bhl-rename-aborted.json")));
        Assert.Equal("Test", Assert.Single(await repository.GetAllAsync()).VersionName);
        Assert.Equal("Test", Assert.Single(await repository.DiscoverInstalledVersionsAsync(settings.MinecraftDirectory)).VersionName);
    }

    [Fact]
    public async Task InvalidRenameMarkerDoesNotHideInstanceWhenQuarantineFails()
    {
        var (settings, settingsService) = CreateSettings();
        var repository = new JsonGameInstanceRepository(
            settingsService,
            logger: null,
            moveDirectoryAsync: null,
            deletionGuidFactory: null,
            stageDeletionMove: null,
            deleteStagedDirectory: null,
            quarantineRenameMarker: (_, _) => throw new IOException("quarantine failed"));
        var directory = await CreateStoredInstanceAsync(repository, settings.MinecraftDirectory, "Test");
        var markerPath = Path.Combine(directory, ".bhl-rename-pending.json");
        await File.WriteAllTextAsync(markerPath, "{");

        await repository.RecoverPendingVersionRenamesAsync(settings.MinecraftDirectory);

        Assert.True(File.Exists(markerPath));
        Assert.Equal("Test", Assert.Single(await repository.GetAllAsync()).VersionName);
        Assert.Equal("Test", Assert.Single(await repository.DiscoverInstalledVersionsAsync(settings.MinecraftDirectory)).VersionName);
    }

    [Fact]
    public async Task UnreadableRenameMarkerInOrdinaryDirectoryKeepsInstanceHidden()
    {
        var (settings, settingsService) = CreateSettings();
        var repository = new JsonGameInstanceRepository(settingsService);
        var directory = await CreateStoredInstanceAsync(repository, settings.MinecraftDirectory, "Test");
        var markerPath = Path.Combine(directory, ".bhl-rename-pending.json");
        await File.WriteAllTextAsync(markerPath, CreateRenameMarkerJson("test", "Test", "Renamed"));

        await using (File.Open(markerPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Empty(await repository.GetAllAsync());
            Assert.Empty(await repository.DiscoverInstalledVersionsAsync(settings.MinecraftDirectory));
        }
    }

    [Fact]
    public async Task ValidRenameMarkerRemainsHiddenWhenRecoveryCannotMoveSource()
    {
        var (settings, settingsService) = CreateSettings();
        var repository = new JsonGameInstanceRepository(
            settingsService,
            moveDirectoryAsync: (_, _, _) => throw new IOException("source is locked"));
        var directory = await CreateStoredInstanceAsync(repository, settings.MinecraftDirectory, "Test");
        await File.WriteAllTextAsync(
            Path.Combine(directory, ".bhl-rename-pending.json"),
            CreateRenameMarkerJson("test", "Test", "Renamed"));

        await repository.RecoverPendingVersionRenamesAsync(settings.MinecraftDirectory);

        Assert.Empty(await repository.GetAllAsync());
        Assert.Empty(await repository.DiscoverInstalledVersionsAsync(settings.MinecraftDirectory));
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
        await File.WriteAllTextAsync(Path.Combine(pendingDirectory, ".bhl-rename-pending.json"), "{");

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
    public async Task RenameRetriesWithAnotherGuidWhenPendingPathIsAFile()
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
        var occupiedPendingPath = Path.Combine(versionsDirectory, ".bhl-rename-pending-Old Pack-a83f21c4");
        await File.WriteAllTextAsync(occupiedPendingPath, "keep");

        await repository.RenameVersionAsync(
            settings.MinecraftDirectory,
            instance,
            "New Pack",
            null,
            DateTimeOffset.UtcNow);

        Assert.Equal("keep", await File.ReadAllTextAsync(occupiedPendingPath));
        Assert.True(Directory.Exists(Path.Combine(versionsDirectory, "New Pack")));
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
    public async Task StageVersionForDeletionRejectsSameNameReplacementBeforeWritingMarkerOrMoving()
    {
        var (settings, settingsService) = CreateSettings();
        var moveAttempts = 0;
        var repository = new JsonGameInstanceRepository(
            settingsService,
            logger: null,
            moveDirectoryAsync: null,
            deletionGuidFactory: null,
            stageDeletionMove: (_, _) => moveAttempts++,
            deleteStagedDirectory: null);
        var directory = await CreateStoredInstanceAsync(repository, settings.MinecraftDirectory, "Test");
        Directory.Delete(directory, recursive: true);
        CreateVersionDirectory(settings.MinecraftDirectory, "Test");
        await repository.SaveAllAsync([CreateInstance("replacement", "Test", directory)]);
        await File.WriteAllTextAsync(Path.Combine(directory, "replacement.txt"), "keep");

        await Assert.ThrowsAsync<GameInstanceMutationConflictException>(() =>
            repository.StageVersionForDeletionAsync(settings.MinecraftDirectory, "Test", "test"));

        Assert.Equal(0, moveAttempts);
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(directory, "replacement.txt")));
        Assert.False(File.Exists(Path.Combine(directory, ".bhl-delete-pending-.json")));
    }

    [Fact]
    public async Task StageVersionForDeletionMovesOwnedDirectoryByHandle()
    {
        var (settings, settingsService) = CreateSettings();
        var repository = new JsonGameInstanceRepository(settingsService);
        var sourceDirectory = await CreateStoredInstanceAsync(
            repository,
            settings.MinecraftDirectory,
            "Test");

        var stagedDirectory = await repository.StageVersionForDeletionAsync(
            settings.MinecraftDirectory,
            "Test",
            "test");

        Assert.False(Directory.Exists(sourceDirectory));
        Assert.True(Directory.Exists(stagedDirectory));
        Assert.True(File.Exists(Path.Combine(
            stagedDirectory,
            InstanceMetadataDirectoryName,
            "instance-settings.json")));
    }

    [Fact]
    public async Task StageVersionForDeletionNeverMovesReplacementThatAppearsAfterValidation()
    {
        var (settings, settingsService) = CreateSettings();
        var versionsDirectory = Path.Combine(settings.MinecraftDirectory, "versions");
        var sourceDirectory = await CreateStoredInstanceAsync(
            new JsonGameInstanceRepository(settingsService),
            settings.MinecraftDirectory,
            "Test");
        var displacedOriginal = Path.Combine(versionsDirectory, "displaced-original");
        var moveAttempts = 0;
        var recycleAttempts = 0;
        var deleteAttempts = 0;
        var repository = new JsonGameInstanceRepository(
            settingsService,
            logger: null,
            moveDirectoryAsync: null,
            deletionGuidFactory: () => Guid.ParseExact("a83f21c4000000000000000000000000", "N"),
            stageDeletionMove: null,
            deleteStagedDirectory: (_, _) => deleteAttempts++,
            recycleStagedDirectory: _ => recycleAttempts++,
            beforeStageDeletionMove: (source, _) =>
            {
                moveAttempts++;
                Directory.Move(source, displacedOriginal);
                CreateVersionDirectory(settings.MinecraftDirectory, "Test");
                WriteInstanceSettings(sourceDirectory, "replacement");
                File.WriteAllText(Path.Combine(sourceDirectory, "replacement.txt"), "keep");
            });

        await Assert.ThrowsAsync<GameInstanceMutationConflictException>(() =>
            repository.StageVersionForDeletionAsync(settings.MinecraftDirectory, "Test", "test"));

        var stagedDirectory = Path.Combine(versionsDirectory, ".bhl-delete-pending-Test-a83f21c4");
        Assert.Equal(1, moveAttempts);
        Assert.False(Directory.Exists(stagedDirectory));
        Assert.True(Directory.Exists(sourceDirectory));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(sourceDirectory, "replacement.txt")));
        Assert.False(File.Exists(Path.Combine(sourceDirectory, ".bhl-delete-pending-.json")));
        Assert.False(File.Exists(Path.Combine(sourceDirectory, ".bhl-delete-aborted.json")));
        Assert.True(Directory.Exists(displacedOriginal));
        Assert.Equal(0, recycleAttempts);
        Assert.Equal(0, deleteAttempts);
    }

    [Fact]
    public async Task SchemaTwoDeletionMarkerWithDifferentInstanceIdIsNeverCleaned()
    {
        var (settings, settingsService) = CreateSettings();
        var stagedDirectory = Path.Combine(
            settings.MinecraftDirectory,
            "versions",
            ".bhl-delete-pending-Test-a83f21c4");
        Directory.CreateDirectory(stagedDirectory);
        WriteInstanceSettings(stagedDirectory, "replacement");
        await File.WriteAllTextAsync(
            Path.Combine(stagedDirectory, ".bhl-delete-pending-.json"),
            """{"schemaVersion":2,"transactionId":"a83f21c4000000000000000000000000","versionName":"Test","instanceId":"expected","createdAtUtc":"2026-07-13T00:00:00Z"}""");
        var recycleAttempts = 0;
        var deleteAttempts = 0;
        var repository = new JsonGameInstanceRepository(
            settingsService,
            logger: null,
            moveDirectoryAsync: null,
            deletionGuidFactory: null,
            stageDeletionMove: null,
            deleteStagedDirectory: (_, _) => deleteAttempts++,
            recycleStagedDirectory: _ => recycleAttempts++);

        await repository.CleanupStagedVersionDirectoriesAsync(settings.MinecraftDirectory);
        await repository.CleanupStagedVersionDirectoriesAsync(settings.MinecraftDirectory);

        Assert.True(Directory.Exists(stagedDirectory));
        Assert.True(File.Exists(Path.Combine(stagedDirectory, ".bhl-delete-aborted.json")));
        Assert.Equal(0, recycleAttempts);
        Assert.Equal(0, deleteAttempts);
    }

    [Fact]
    public async Task PermanentDeletionIsSkippedWhenIdentityChangesAfterRecycleFailure()
    {
        var (settings, settingsService) = CreateSettings();
        var stagedDirectory = Path.Combine(
            settings.MinecraftDirectory,
            "versions",
            ".bhl-delete-pending-Test-a83f21c4");
        Directory.CreateDirectory(stagedDirectory);
        WriteInstanceSettings(stagedDirectory, "expected");
        await File.WriteAllTextAsync(
            Path.Combine(stagedDirectory, ".bhl-delete-pending-.json"),
            """{"schemaVersion":2,"transactionId":"a83f21c4000000000000000000000000","versionName":"Test","instanceId":"expected","createdAtUtc":"2026-07-13T00:00:00Z"}""");
        var deleteAttempts = 0;
        var repository = new JsonGameInstanceRepository(
            settingsService,
            logger: null,
            moveDirectoryAsync: null,
            deletionGuidFactory: null,
            stageDeletionMove: null,
            deleteStagedDirectory: (_, _) => deleteAttempts++,
            recycleStagedDirectory: path =>
            {
                WriteInstanceSettings(path, "replacement");
                File.WriteAllText(Path.Combine(path, "replacement.txt"), "keep");
                throw new IOException("recycle failed after replacement");
            });

        Assert.False(await repository.TryDeleteStagedVersionDirectoryAsync(
            settings.MinecraftDirectory,
            stagedDirectory));

        Assert.Equal(0, deleteAttempts);
        Assert.True(Directory.Exists(stagedDirectory));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(stagedDirectory, "replacement.txt")));
        Assert.True(File.Exists(Path.Combine(stagedDirectory, ".bhl-delete-aborted.json")));
    }

    [Fact]
    public async Task StageVersionForDeletionRetriesWhenGeneratedDestinationAlreadyExists()
    {
        var (settings, settingsService) = CreateSettings();
        await CreateStoredInstanceAsync(
            new JsonGameInstanceRepository(settingsService),
            settings.MinecraftDirectory,
            "Test");
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

        var stagedDirectory = await repository.StageVersionForDeletionAsync(settings.MinecraftDirectory, "Test", "test");

        Assert.EndsWith(".bhl-delete-pending-Test-b94e32d5", stagedDirectory, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(versionsDirectory, "Test")));
        Assert.True(Directory.Exists(stagedDirectory));
        Assert.True(File.Exists(Path.Combine(stagedDirectory, ".bhl-delete-pending-.json")));
    }

    [Fact]
    public async Task StageVersionForDeletionRetriesWhenDestinationAppearsDuringMove()
    {
        var (settings, settingsService) = CreateSettings();
        await CreateStoredInstanceAsync(
            new JsonGameInstanceRepository(settingsService),
            settings.MinecraftDirectory,
            "Test");
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
                Assert.True(File.Exists(Path.Combine(source, ".bhl-delete-pending-.json")));
                if (moves == 1)
                {
                    Directory.CreateDirectory(destination);
                    throw new IOException("destination won the race");
                }

                Directory.Move(source, destination);
            },
            deleteStagedDirectory: null);

        var stagedDirectory = await repository.StageVersionForDeletionAsync(settings.MinecraftDirectory, "Test", "test");

        Assert.Equal(2, moves);
        Assert.EndsWith(".bhl-delete-pending-Test-b94e32d5", stagedDirectory, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StageVersionForDeletionStopsAfterThreeDestinationCollisions()
    {
        var (settings, settingsService) = CreateSettings();
        await CreateStoredInstanceAsync(
            new JsonGameInstanceRepository(settingsService),
            settings.MinecraftDirectory,
            "Test");
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
            repository.StageVersionForDeletionAsync(settings.MinecraftDirectory, "Test", "test"));

        Assert.Equal(0, moveAttempts);
        Assert.True(Directory.Exists(Path.Combine(versionsDirectory, "Test")));
    }

    [Fact]
    public async Task StageVersionForDeletionDoesNotRetryNonCollisionMoveFailure()
    {
        var (settings, settingsService) = CreateSettings();
        await CreateStoredInstanceAsync(
            new JsonGameInstanceRepository(settingsService),
            settings.MinecraftDirectory,
            "Test");
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
            repository.StageVersionForDeletionAsync(settings.MinecraftDirectory, "Test", "test"));

        Assert.Equal(1, moveAttempts);
        var sourceDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "Test");
        Assert.True(Directory.Exists(sourceDirectory));
        Assert.False(File.Exists(Path.Combine(sourceDirectory, ".bhl-delete-pending-.json")));
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
        WriteDeletionMarker(retained, "Retained", "a83f21c4000000000000000000000000");
        WriteDeletionMarker(removed, "Removed", "b94e32d5000000000000000000000000");
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
        WriteDeletionMarker(stagedDirectory, "Test", "a83f21c4000000000000000000000000");
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

    [Fact]
    public async Task CleanupPreservesPrefixedDeletionDirectoryWithoutValidMarker()
    {
        var (settings, settingsService) = CreateSettings();
        var directory = Path.Combine(
            settings.MinecraftDirectory,
            "versions",
            ".bhl-delete-pending-UserData-a83f21c4");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "keep.txt"), "keep");
        var repository = new JsonGameInstanceRepository(settingsService);

        await repository.CleanupStagedVersionDirectoriesAsync(settings.MinecraftDirectory);

        Assert.True(Directory.Exists(directory));
        Assert.True(File.Exists(Path.Combine(directory, "keep.txt")));
    }

    private static void WriteDeletionMarker(string directory, string versionName, string transactionId)
    {
        File.WriteAllText(
            Path.Combine(directory, ".bhl-delete-pending-.json"),
            $$"""{"schemaVersion":1,"transactionId":"{{transactionId}}","versionName":"{{versionName}}","createdAtUtc":"2026-07-13T00:00:00Z"}""");
    }

    private static string CreateRenameMarkerJson(string instanceId, string oldName, string newName) =>
        $$"""{"schemaVersion":1,"transactionId":"a83f21c4000000000000000000000000","instanceId":"{{instanceId}}","oldName":"{{oldName}}","newName":"{{newName}}","updatedAtUtc":"2026-07-13T00:00:00Z"}""";

    private static async Task<string> CreateStoredInstanceAsync(
        JsonGameInstanceRepository repository,
        string minecraftDirectory,
        string versionName)
    {
        CreateVersionDirectory(minecraftDirectory, versionName);
        var directory = Path.Combine(minecraftDirectory, "versions", versionName);
        await repository.SaveAllAsync(
        [
            new GameInstance
            {
                Id = versionName.ToLowerInvariant(),
                Name = versionName,
                MinecraftVersion = "1.20.1",
                VersionName = versionName,
                InstanceDirectory = directory
            }
        ]);
        return directory;
    }

    private static GameInstance CreateInstance(string id, string versionName, string instanceDirectory)
    {
        return new GameInstance
        {
            Id = id,
            Name = versionName,
            MinecraftVersion = "1.20.1",
            VersionName = versionName,
            InstanceDirectory = instanceDirectory
        };
    }

    private static void WriteInstanceSettings(string instanceDirectory, string instanceId)
    {
        var metadataDirectory = Path.Combine(instanceDirectory, InstanceMetadataDirectoryName);
        Directory.CreateDirectory(metadataDirectory);
        File.WriteAllText(
            Path.Combine(metadataDirectory, "instance-settings.json"),
            JsonSerializer.Serialize(new GameInstance
            {
                Id = instanceId,
                Name = Path.GetFileName(instanceDirectory),
                MinecraftVersion = "1.20.1",
                VersionName = Path.GetFileName(instanceDirectory),
                InstanceDirectory = instanceDirectory
            }));
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
