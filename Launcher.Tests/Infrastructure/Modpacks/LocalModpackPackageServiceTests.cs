using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using Launcher.Application.Services;
using Launcher.Infrastructure;
using Launcher.Infrastructure.Modpacks;

namespace Launcher.Tests.Infrastructure.Modpacks;

public sealed class LocalModpackPackageServiceTests : TestTempDirectory
{
    [Fact]
    public async Task RecognizeAsyncAcceptsMrpack()
    {
        var archivePath = Path.Combine(TempRoot, "recognized.mrpack");
        CreateArchive(
            archivePath,
            archive => AddEntry(
                archive,
                "modrinth.index.json",
                """
                {
                  "name": "Recognized Pack",
                  "dependencies": {
                    "minecraft": "1.20.1"
                  },
                  "files": []
                }
                """));
        var service = CreateService();

        var result = await service.RecognizeAsync(archivePath);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task RecognizeAsyncAcceptsZipWrapperContainingSingleMrpack()
    {
        var archivePath = Path.Combine(TempRoot, "wrapper-recognize.zip");
        var embeddedMrpackBytes = CreateArchiveBytes(
            archive => AddEntry(
                archive,
                "modrinth.index.json",
                """
                {
                  "name": "Wrapped Pack",
                  "dependencies": {
                    "minecraft": "1.21.1",
                    "fabric-loader": "0.16.10"
                  },
                  "files": []
                }
                """));
        CreateArchive(
            archivePath,
            archive =>
            {
                AddEntry(archive, "launcher.exe", "stub");
                AddBinaryEntry(archive, "modpack.mrpack", embeddedMrpackBytes);
            });
        var service = CreateService();

        var result = await service.RecognizeAsync(archivePath);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task RecognizeAsyncRejectsUnsupportedFile()
    {
        var archivePath = Path.Combine(TempRoot, "unsupported.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        await File.WriteAllTextAsync(archivePath, "not a modpack");
        var service = CreateService();

        var result = await service.RecognizeAsync(archivePath);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModpackRecognitionFailureReason.UnsupportedArchive, result.FailureReason);
    }

    [Fact]
    public async Task PrepareAsyncRecognizesModrinthArchiveAndParsesManifest()
    {
        var archivePath = Path.Combine(TempRoot, "pack.mrpack");
        CreateArchive(
            archivePath,
            archive =>
            {
                AddEntry(
                    archive,
                    "modrinth.index.json",
                    """
                    {
                      "name": "Demo Pack",
                      "dependencies": {
                        "minecraft": "1.20.1",
                        "fabric-loader": "0.16.10"
                      },
                      "files": [
                        {
                          "path": "mods/keep.jar",
                          "hashes": {
                            "sha1": "abc"
                          },
                          "downloads": [
                            "https://example.com/mods/keep.jar"
                          ]
                        },
                        {
                          "path": "mods/server-only.jar",
                          "hashes": {
                            "sha1": "def"
                          },
                          "downloads": [
                            "https://example.com/mods/server-only.jar"
                          ],
                          "env": {
                            "client": "unsupported"
                          }
                        }
                      ]
                    }
                    """);
                AddEntry(archive, "overrides/config/demo.txt", "config");
                AddEntry(archive, "client-overrides/options.txt", "options");
            });
        var service = CreateService();

        var prepared = await service.PrepareAsync(archivePath);

        Assert.Equal(ModpackPackageKind.Modrinth, prepared.PackageKind);
        Assert.Equal("Demo Pack", prepared.PackageName);
        Assert.Equal("1.20.1", prepared.MinecraftVersion);
        Assert.Equal(LoaderKind.Fabric, prepared.Loader);
        Assert.Equal("0.16.10", prepared.LoaderVersion);
        var file = Assert.Single(prepared.Files);
        Assert.Equal("mods/keep.jar", file.RelativePath);
        Assert.NotNull(prepared.OverridesDirectory);
        Assert.True(File.Exists(Path.Combine(prepared.OverridesDirectory!, "config", "demo.txt")));
        Assert.True(File.Exists(Path.Combine(prepared.OverridesDirectory!, "options.txt")));
    }

    [Fact]
    public async Task PrepareAsyncRejectsZipWithoutManifest()
    {
        var archivePath = Path.Combine(TempRoot, "broken.zip");
        CreateArchive(archivePath, archive => AddEntry(archive, "readme.txt", "missing manifest"));
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<ModpackImportException>(() => service.PrepareAsync(archivePath));

        Assert.Equal(ModpackImportFailureReason.InvalidManifest, exception.FailureReason);
    }

    [Fact]
    public async Task PrepareAsyncAcceptsZipWrapperContainingSingleMrpack()
    {
        var archivePath = Path.Combine(TempRoot, "wrapper.zip");
        var embeddedMrpackBytes = CreateArchiveBytes(
            archive =>
            {
                AddEntry(
                    archive,
                    "modrinth.index.json",
                    """
                    {
                      "name": "Wrapped Pack",
                      "dependencies": {
                        "minecraft": "1.21.1",
                        "fabric-loader": "0.16.10"
                      },
                      "files": []
                    }
                    """);
                AddEntry(archive, "overrides/config/wrapped.txt", "wrapped");
            });
        CreateArchive(
            archivePath,
            archive =>
            {
                AddEntry(archive, "launcher.exe", "stub");
                AddBinaryEntry(archive, "modpack.mrpack", embeddedMrpackBytes);
            });
        var service = CreateService();

        var prepared = await service.PrepareAsync(archivePath);

        Assert.Equal(ModpackPackageKind.Modrinth, prepared.PackageKind);
        Assert.Equal(archivePath, prepared.SourceArchivePath);
        Assert.Equal("Wrapped Pack", prepared.PackageName);
        Assert.Equal("1.21.1", prepared.MinecraftVersion);
        Assert.Equal(LoaderKind.Fabric, prepared.Loader);
        Assert.NotNull(prepared.OverridesDirectory);
        Assert.True(File.Exists(Path.Combine(prepared.OverridesDirectory!, "config", "wrapped.txt")));
    }

    [Fact]
    public async Task PrepareAsyncParsesCurseForgeManifestAndUsesPrimaryLoader()
    {
        var archivePath = Path.Combine(TempRoot, "curseforge.zip");
        CreateArchive(
            archivePath,
            archive =>
            {
                AddEntry(
                    archive,
                    "manifest.json",
                    """
                    {
                      "name": "Curse Pack",
                      "minecraft": {
                        "version": "1.20.1",
                        "modLoaders": [
                          { "id": "fabric-0.15.0", "primary": false },
                          { "id": "forge-47.4.20", "primary": true }
                        ]
                      },
                      "files": []
                    }
                    """);
                AddEntry(archive, "overrides/config/curse.txt", "curse");
            });
        var service = CreateService();

        var prepared = await service.PrepareAsync(archivePath);

        Assert.Equal(ModpackPackageKind.CurseForge, prepared.PackageKind);
        Assert.Equal("Curse Pack", prepared.PackageName);
        Assert.Equal("1.20.1", prepared.MinecraftVersion);
        Assert.Equal(LoaderKind.Forge, prepared.Loader);
        Assert.Equal("47.4.20", prepared.LoaderVersion);
        Assert.NotNull(prepared.OverridesDirectory);
        Assert.True(File.Exists(Path.Combine(prepared.OverridesDirectory!, "config", "curse.txt")));
    }

    [Fact]
    public async Task PrepareAsyncRequiresCurseForgeApiKeyWhenManifestContainsFiles()
    {
        var previousValue = Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY");
        var previousCurrentDirectory = Directory.GetCurrentDirectory();
        Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", null);
        Directory.CreateDirectory(TempRoot);
        Directory.SetCurrentDirectory(TempRoot);
        var archivePath = Path.Combine(TempRoot, "curseforge-with-files.zip");
        CreateArchive(
            archivePath,
            archive => AddEntry(
                archive,
                "manifest.json",
                """
                {
                  "name": "Curse Pack",
                  "minecraft": {
                    "version": "1.20.1",
                    "modLoaders": []
                  },
                  "files": [
                    { "projectID": 10, "fileID": 20 }
                  ]
                }
                """));
        var service = CreateService();

        try
        {
            var exception = await Assert.ThrowsAsync<ModpackImportException>(() => service.PrepareAsync(archivePath));
            Assert.Equal(ModpackImportFailureReason.MissingCurseForgeApiKey, exception.FailureReason);
        }
        finally
        {
            Directory.SetCurrentDirectory(previousCurrentDirectory);
            Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", previousValue);
        }
    }

    [Fact]
    public async Task PrepareAsyncUsesLocalSecretFileForCurseForgeApiKey()
    {
        var previousValue = Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY");
        var previousCurrentDirectory = Directory.GetCurrentDirectory();
        Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", null);
        Directory.CreateDirectory(TempRoot);
        Directory.SetCurrentDirectory(TempRoot);

        var secretsDirectory = Path.Combine(TempRoot, ".local-secrets");
        Directory.CreateDirectory(secretsDirectory);
        var expectedApiKey = "local-secret-key";
        await File.WriteAllTextAsync(Path.Combine(secretsDirectory, "curseforge.key"), expectedApiKey);

        var archivePath = Path.Combine(TempRoot, "curseforge-local-secret.zip");
        CreateArchive(
            archivePath,
            archive => AddEntry(
                archive,
                "manifest.json",
                """
                {
                  "name": "Curse Pack",
                  "minecraft": {
                    "version": "1.20.1",
                    "modLoaders": []
                  },
                  "files": [
                    { "projectID": 10, "fileID": 20 }
                  ]
                }
                """));
        var handler = new CurseForgeMetadataHandler();
        var service = CreateService(new HttpClient(handler));

        try
        {
            var prepared = await service.PrepareAsync(archivePath);

            var file = Assert.Single(prepared.Files);
            Assert.Equal(expectedApiKey, handler.LastApiKey);
            Assert.Equal("example.jar", file.FileName);
            Assert.Equal("mods/example.jar", file.RelativePath);
            Assert.Equal("https://example.com/example.jar", file.SourceUrl);
        }
        finally
        {
            Directory.SetCurrentDirectory(previousCurrentDirectory);
            Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", previousValue);
        }
    }

    [Fact]
    public async Task PrepareAsyncRejectsUnsupportedCurseForgeLoader()
    {
        var archivePath = Path.Combine(TempRoot, "unsupported-loader.zip");
        CreateArchive(
            archivePath,
            archive => AddEntry(
                archive,
                "manifest.json",
                """
                {
                  "name": "Unsupported Pack",
                  "minecraft": {
                    "version": "1.20.1",
                    "modLoaders": [
                      { "id": "quilt-0.26.0", "primary": true }
                    ]
                  },
                  "files": []
                }
                """));
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<ModpackImportException>(() => service.PrepareAsync(archivePath));

        Assert.Equal(ModpackImportFailureReason.UnsupportedLoader, exception.FailureReason);
    }

    [Fact]
    public async Task PrepareAsyncRejectsPathTraversalEntries()
    {
        var archivePath = Path.Combine(TempRoot, "unsafe.mrpack");
        CreateArchive(
            archivePath,
            archive =>
            {
                AddEntry(
                    archive,
                    "modrinth.index.json",
                    """
                    {
                      "name": "Unsafe Pack",
                      "dependencies": {
                        "minecraft": "1.20.1"
                      },
                      "files": []
                    }
                    """);
                AddEntry(archive, "overrides/../evil.txt", "bad");
            });
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<ModpackImportException>(() => service.PrepareAsync(archivePath));

        Assert.Equal(ModpackImportFailureReason.InvalidManifest, exception.FailureReason);
        Assert.False(File.Exists(Path.Combine(TempRoot, "evil.txt")));
    }

    [Fact]
    public async Task InstallContentAsyncThrowsOnHashMismatch()
    {
        var service = CreateService(new HttpClient(new FixedResponseHandler("actual-bytes")));
        var instanceDirectory = Path.Combine(TempRoot, "instance");
        Directory.CreateDirectory(instanceDirectory);
        var prepared = new PreparedModpack
        {
            PackageKind = ModpackPackageKind.Modrinth,
            SourceArchivePath = Path.Combine(TempRoot, "pack.mrpack"),
            WorkingDirectory = Path.Combine(TempRoot, "work"),
            PackageName = "Hash Pack",
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Vanilla,
            Files =
            [
                new PreparedModpackDownload
                {
                    FileName = "keep.jar",
                    RelativePath = "mods/keep.jar",
                    SourceUrl = "https://example.com/mods/keep.jar",
                    Sha1 = "0000000000000000000000000000000000000000"
                }
            ]
        };
        Directory.CreateDirectory(prepared.WorkingDirectory);
        var instance = new GameInstance
        {
            Name = "Hash Pack",
            VersionName = "Hash Pack",
            MinecraftVersion = "1.20.1",
            InstanceDirectory = instanceDirectory
        };

        var exception = await Assert.ThrowsAsync<ModpackImportException>(() =>
            service.InstallContentAsync(prepared, instance, progress: null));

        Assert.Equal(ModpackImportFailureReason.HashMismatch, exception.FailureReason);
    }

    [Fact]
    public async Task InstallContentAsyncCopiesOverridesIntoInstanceDirectory()
    {
        var service = CreateService();
        var overridesDirectory = Path.Combine(TempRoot, "overrides");
        Directory.CreateDirectory(Path.Combine(overridesDirectory, "config"));
        await File.WriteAllTextAsync(Path.Combine(overridesDirectory, "config", "settings.txt"), "demo");
        var prepared = new PreparedModpack
        {
            PackageKind = ModpackPackageKind.Modrinth,
            SourceArchivePath = Path.Combine(TempRoot, "pack.mrpack"),
            WorkingDirectory = Path.Combine(TempRoot, "work"),
            PackageName = "Overrides Pack",
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Vanilla,
            OverridesDirectory = overridesDirectory
        };
        var instance = new GameInstance
        {
            Name = "Overrides Pack",
            VersionName = "Overrides Pack",
            MinecraftVersion = "1.20.1",
            InstanceDirectory = Path.Combine(TempRoot, "instance")
        };
        Directory.CreateDirectory(instance.InstanceDirectory);

        await service.InstallContentAsync(prepared, instance, progress: null);

        Assert.True(File.Exists(Path.Combine(instance.InstanceDirectory, "config", "settings.txt")));
    }

    private LocalModpackPackageService CreateService(HttpClient? httpClient = null)
    {
        return new LocalModpackPackageService(
            new LauncherPathProvider(TempRoot),
            httpClient: httpClient);
    }

    private static void CreateArchive(string archivePath, Action<ZipArchive> configure)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        using var stream = File.Create(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);
        configure(archive);
    }

    private static byte[] CreateArchiveBytes(Action<ZipArchive> configure)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            configure(archive);

        return stream.ToArray();
    }

    private static void AddEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private static void AddBinaryEntry(ZipArchive archive, string entryName, byte[] content)
    {
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        stream.Write(content, 0, content.Length);
    }

    private sealed class FixedResponseHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }
    }

    private sealed class CurseForgeMetadataHandler : HttpMessageHandler
    {
        public string? LastApiKey { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastApiKey = request.Headers.TryGetValues("x-api-key", out var values)
                ? values.SingleOrDefault()
                : null;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "data": {
                        "fileName": "example.jar",
                        "downloadUrl": "https://example.com/example.jar"
                      }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }
}
