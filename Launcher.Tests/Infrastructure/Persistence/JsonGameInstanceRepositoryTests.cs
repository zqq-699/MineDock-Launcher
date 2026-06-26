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
            InstanceDirectory = "stale-path"
        };

        await using (var stream = File.Create(settingsPath))
        {
            await JsonSerializer.SerializeAsync(stream, storedInstance, new JsonSerializerOptions { WriteIndented = true });
        }

        var loaded = Assert.Single(await repository.GetAllAsync());
        Assert.Equal("Demo Pack", loaded.Name);
        Assert.Equal("loaded from instance folder", loaded.Description);
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

        await repository.RenameVersionAsync(settings.MinecraftDirectory, "Old Pack", "New Pack");

        Assert.Equal(3, attempts);
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

        await Assert.ThrowsAsync<IOException>(() =>
            repository.RenameVersionAsync(settings.MinecraftDirectory, "Old Pack", "New Pack"));

        Assert.Equal(5, attempts);
        Assert.True(File.Exists(Path.Combine(settings.MinecraftDirectory, "versions", "Old Pack", "Old Pack.json")));
        Assert.False(Directory.Exists(Path.Combine(settings.MinecraftDirectory, "versions", "New Pack")));
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
