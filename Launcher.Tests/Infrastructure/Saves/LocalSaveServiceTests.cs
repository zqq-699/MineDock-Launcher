using Launcher.Domain.Models;
using Launcher.Infrastructure.FileSystem;

namespace Launcher.Tests.Infrastructure.Saves;

public sealed class LocalSaveServiceTests
{
    [Fact]
    public async Task GetSavesAsyncLoadsTopLevelSaveDirectoriesAndReadsIcons()
    {
        var instanceDirectory = CreateTempDirectory();
        try
        {
            var savesDirectory = Path.Combine(instanceDirectory, "saves");
            var withIconDirectory = Path.Combine(savesDirectory, "Cherry Grove");
            var withoutIconDirectory = Path.Combine(savesDirectory, "Starter Base");
            var nestedDirectory = Path.Combine(withIconDirectory, "region");
            Directory.CreateDirectory(withIconDirectory);
            Directory.CreateDirectory(withoutIconDirectory);
            Directory.CreateDirectory(nestedDirectory);
            File.WriteAllText(Path.Combine(withIconDirectory, "icon.png"), "not-a-real-png");

            var createdAt = new DateTimeOffset(2026, 1, 3, 10, 20, 30, TimeSpan.Zero);
            Directory.SetCreationTimeUtc(withIconDirectory, createdAt.UtcDateTime);

            var service = new LocalSaveService();
            var saves = await service.GetSavesAsync(CreateInstance(instanceDirectory));

            Assert.Equal(2, saves.Count);
            Assert.DoesNotContain(saves, save => string.Equals(save.DirectoryName, "region", StringComparison.OrdinalIgnoreCase));

            var cherry = saves.Single(save => save.DirectoryName == "Cherry Grove");
            Assert.Equal("Cherry Grove", cherry.Name);
            Assert.Equal(withIconDirectory, cherry.FullPath);
            Assert.Equal(Path.Combine(withIconDirectory, "icon.png"), cherry.IconSource);
            Assert.Equal(createdAt, cherry.CreatedAt);

            var starter = saves.Single(save => save.DirectoryName == "Starter Base");
            Assert.Null(starter.IconSource);
        }
        finally
        {
            DeleteDirectoryIfExists(instanceDirectory);
        }
    }

    [Fact]
    public async Task GetSavesAsyncReturnsEmptyWhenSavesDirectoryIsMissing()
    {
        var instanceDirectory = CreateTempDirectory();
        try
        {
            var service = new LocalSaveService();

            var saves = await service.GetSavesAsync(CreateInstance(instanceDirectory));

            Assert.Empty(saves);
        }
        finally
        {
            DeleteDirectoryIfExists(instanceDirectory);
        }
    }

    [Fact]
    public async Task DeleteAsyncRemovesSingleSaveDirectory()
    {
        var instanceDirectory = CreateTempDirectory();
        try
        {
            var saveDirectory = Path.Combine(instanceDirectory, "saves", "Alpha Base");
            Directory.CreateDirectory(saveDirectory);

            var service = new LocalSaveService();
            await service.DeleteAsync(new LocalSave
            {
                Name = "Alpha Base",
                DirectoryName = "Alpha Base",
                FullPath = saveDirectory,
                CreatedAt = DateTimeOffset.UtcNow
            });

            Assert.False(Directory.Exists(saveDirectory));
        }
        finally
        {
            DeleteDirectoryIfExists(instanceDirectory);
        }
    }

    [Fact]
    public async Task DeleteAsyncRemovesMultipleSaveDirectories()
    {
        var instanceDirectory = CreateTempDirectory();
        try
        {
            var alphaDirectory = Path.Combine(instanceDirectory, "saves", "Alpha Base");
            var betaDirectory = Path.Combine(instanceDirectory, "saves", "Beta Base");
            Directory.CreateDirectory(alphaDirectory);
            Directory.CreateDirectory(betaDirectory);

            var service = new LocalSaveService();
            await service.DeleteAsync(
            [
                new LocalSave
                {
                    Name = "Alpha Base",
                    DirectoryName = "Alpha Base",
                    FullPath = alphaDirectory,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new LocalSave
                {
                    Name = "Beta Base",
                    DirectoryName = "Beta Base",
                    FullPath = betaDirectory,
                    CreatedAt = DateTimeOffset.UtcNow
                }
            ]);

            Assert.False(Directory.Exists(alphaDirectory));
            Assert.False(Directory.Exists(betaDirectory));
        }
        finally
        {
            DeleteDirectoryIfExists(instanceDirectory);
        }
    }

    private static GameInstance CreateInstance(string instanceDirectory)
    {
        return new GameInstance
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Test Instance",
            MinecraftVersion = "1.21.4",
            Loader = LoaderKind.Vanilla,
            InstanceDirectory = instanceDirectory
        };
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
