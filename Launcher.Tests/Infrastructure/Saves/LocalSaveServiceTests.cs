using Launcher.Domain.Models;
using Launcher.Infrastructure;
using Launcher.Infrastructure.FileSystem;
using System.Formats.Tar;
using System.IO.Compression;

namespace Launcher.Tests.Infrastructure.Saves;

public sealed class LocalSaveServiceTests
{
    [Fact]
    public async Task GetSavesAsyncLoadsTopLevelSaveDirectoriesAndReadsIcons()
    {
        var instanceDirectory = CreateTempDirectory();
        var appDataDirectory = CreateTempDirectory();
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

            var service = new LocalSaveService(new LauncherPathProvider(appDataDirectory));
            var saves = await service.GetSavesAsync(CreateInstance(instanceDirectory));

            Assert.Equal(2, saves.Count);
            Assert.DoesNotContain(saves, save => string.Equals(save.DirectoryName, "region", StringComparison.OrdinalIgnoreCase));

            var cherry = saves.Single(save => save.DirectoryName == "Cherry Grove");
            Assert.Equal("Cherry Grove", cherry.Name);
            Assert.Equal(withIconDirectory, cherry.FullPath);
            Assert.NotNull(cherry.IconSource);
            Assert.NotEqual(Path.Combine(withIconDirectory, "icon.png"), cherry.IconSource);
            Assert.True(File.Exists(new Uri(cherry.IconSource!, UriKind.Absolute).LocalPath));
            Assert.Equal(createdAt, cherry.CreatedAt);

            var starter = saves.Single(save => save.DirectoryName == "Starter Base");
            Assert.Null(starter.IconSource);
        }
        finally
        {
            DeleteDirectoryIfExists(instanceDirectory);
            DeleteDirectoryIfExists(appDataDirectory);
        }
    }

    [Fact]
    public async Task GetSavesAsyncReturnsEmptyWhenSavesDirectoryIsMissing()
    {
        var instanceDirectory = CreateTempDirectory();
        var appDataDirectory = CreateTempDirectory();
        try
        {
            var service = new LocalSaveService(new LauncherPathProvider(appDataDirectory));

            var saves = await service.GetSavesAsync(CreateInstance(instanceDirectory));

            Assert.Empty(saves);
        }
        finally
        {
            DeleteDirectoryIfExists(instanceDirectory);
            DeleteDirectoryIfExists(appDataDirectory);
        }
    }

    [Fact]
    public async Task DeleteAsyncRemovesSingleSaveDirectory()
    {
        var instanceDirectory = CreateTempDirectory();
        var appDataDirectory = CreateTempDirectory();
        try
        {
            var saveDirectory = Path.Combine(instanceDirectory, "saves", "Alpha Base");
            Directory.CreateDirectory(saveDirectory);

            var service = new LocalSaveService(new LauncherPathProvider(appDataDirectory));
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
            DeleteDirectoryIfExists(appDataDirectory);
        }
    }

    [Fact]
    public async Task DeleteAsyncRemovesMultipleSaveDirectories()
    {
        var instanceDirectory = CreateTempDirectory();
        var appDataDirectory = CreateTempDirectory();
        try
        {
            var alphaDirectory = Path.Combine(instanceDirectory, "saves", "Alpha Base");
            var betaDirectory = Path.Combine(instanceDirectory, "saves", "Beta Base");
            Directory.CreateDirectory(alphaDirectory);
            Directory.CreateDirectory(betaDirectory);

            var service = new LocalSaveService(new LauncherPathProvider(appDataDirectory));
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
            DeleteDirectoryIfExists(appDataDirectory);
        }
    }

    [Fact]
    public async Task ImportFromArchiveAsyncImportsRootZipArchiveIntoSavesDirectory()
    {
        var instanceDirectory = CreateTempDirectory();
        var appDataDirectory = CreateTempDirectory();
        try
        {
            var archivePath = Path.Combine(instanceDirectory, "Cherry Grove.zip");
            CreateZipArchive(
                archivePath,
                rootDirectoryName: null,
                ("level.dat", "level"),
                ("region/r.0.0.mca", "region-data"),
                ("icon.png", "icon"));

            var service = new LocalSaveService(new LauncherPathProvider(appDataDirectory));
            var result = await service.ImportFromArchiveAsync(CreateInstance(instanceDirectory), archivePath);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.ImportedSave);
            var importedSave = result.ImportedSave!;
            Assert.Equal("Cherry Grove", importedSave.DirectoryName);
            Assert.True(File.Exists(Path.Combine(importedSave.FullPath, "level.dat")));
            Assert.True(File.Exists(Path.Combine(importedSave.FullPath, "region", "r.0.0.mca")));
            Assert.NotNull(importedSave.IconSource);
            Assert.True(File.Exists(new Uri(importedSave.IconSource!, UriKind.Absolute).LocalPath));
        }
        finally
        {
            DeleteDirectoryIfExists(instanceDirectory);
            DeleteDirectoryIfExists(appDataDirectory);
        }
    }

    [Fact]
    public async Task ImportFromArchiveAsyncImportsSingleTopLevelFolderFromTarGzAndRenamesDuplicates()
    {
        var instanceDirectory = CreateTempDirectory();
        var appDataDirectory = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(instanceDirectory, "saves", "My World"));
            var archivePath = Path.Combine(instanceDirectory, "duplicate.tar.gz");
            CreateTarGzArchive(
                archivePath,
                ("My World/level.dat", "level"),
                ("My World/icon.png", "icon"),
                ("My World/region/r.0.0.mca", "region-data"));

            var service = new LocalSaveService(new LauncherPathProvider(appDataDirectory));
            var result = await service.ImportFromArchiveAsync(CreateInstance(instanceDirectory), archivePath);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.ImportedSave);
            var importedSave = result.ImportedSave!;
            Assert.Equal("My World (1)", importedSave.DirectoryName);
            Assert.True(File.Exists(Path.Combine(importedSave.FullPath, "level.dat")));
            Assert.NotNull(importedSave.IconSource);
            Assert.True(File.Exists(new Uri(importedSave.IconSource!, UriKind.Absolute).LocalPath));
        }
        finally
        {
            DeleteDirectoryIfExists(instanceDirectory);
            DeleteDirectoryIfExists(appDataDirectory);
        }
    }

    [Fact]
    public async Task ImportFromArchiveAsyncReturnsInvalidWhenArchiveDoesNotContainLevelDat()
    {
        var instanceDirectory = CreateTempDirectory();
        var appDataDirectory = CreateTempDirectory();
        try
        {
            var archivePath = Path.Combine(instanceDirectory, "Broken Save.zip");
            CreateZipArchive(
                archivePath,
                rootDirectoryName: "Broken Save",
                ("Broken Save/region/r.0.0.mca", "region-data"));

            var service = new LocalSaveService(new LauncherPathProvider(appDataDirectory));
            var result = await service.ImportFromArchiveAsync(CreateInstance(instanceDirectory), archivePath);

            Assert.False(result.IsSuccess);
            Assert.Equal(LocalSaveImportFailureReason.InvalidMinecraftSaveArchive, result.FailureReason);
            Assert.False(Directory.Exists(Path.Combine(instanceDirectory, "saves", "Broken Save")));
        }
        finally
        {
            DeleteDirectoryIfExists(instanceDirectory);
            DeleteDirectoryIfExists(appDataDirectory);
        }
    }

    [Fact]
    public async Task ImportFromArchiveAsyncBlocksPathTraversalEntries()
    {
        var instanceDirectory = CreateTempDirectory();
        var appDataDirectory = CreateTempDirectory();
        try
        {
            var archivePath = Path.Combine(instanceDirectory, "Unsafe.zip");
            CreateZipArchive(
                archivePath,
                rootDirectoryName: null,
                ("level.dat", "level"),
                ("../escape.txt", "escape"));
            var escapedPath = Path.Combine(instanceDirectory, "escape.txt");

            var service = new LocalSaveService(new LauncherPathProvider(appDataDirectory));
            var result = await service.ImportFromArchiveAsync(CreateInstance(instanceDirectory), archivePath);

            Assert.False(result.IsSuccess);
            Assert.Equal(LocalSaveImportFailureReason.UnexpectedError, result.FailureReason);
            Assert.False(File.Exists(escapedPath));
            Assert.False(Directory.Exists(Path.Combine(instanceDirectory, "saves", "Unsafe")));
        }
        finally
        {
            DeleteDirectoryIfExists(instanceDirectory);
            DeleteDirectoryIfExists(appDataDirectory);
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

    private static void CreateZipArchive(
        string archivePath,
        string? rootDirectoryName,
        params (string Path, string Content)[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        foreach (var entry in entries)
        {
            var entryPath = rootDirectoryName is null || entry.Path.StartsWith(rootDirectoryName, StringComparison.Ordinal)
                ? entry.Path
                : rootDirectoryName + "/" + entry.Path.TrimStart('/');
            var archiveEntry = archive.CreateEntry(entryPath);
            using var stream = archiveEntry.Open();
            using var writer = new StreamWriter(stream);
            writer.Write(entry.Content);
        }
    }

    private static void CreateTarGzArchive(string archivePath, params (string Path, string Content)[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        using var fileStream = File.Create(archivePath);
        using var gzipStream = new GZipStream(fileStream, CompressionLevel.SmallestSize);
        using var writer = new TarWriter(gzipStream, leaveOpen: false);
        foreach (var entry in entries)
        {
            var entryStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(entry.Content));
            var tarEntry = new PaxTarEntry(TarEntryType.RegularFile, entry.Path)
            {
                DataStream = entryStream
            };
            writer.WriteEntry(tarEntry);
        }
    }
}
