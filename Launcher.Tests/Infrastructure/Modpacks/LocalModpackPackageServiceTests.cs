using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure;
using Launcher.Infrastructure.CurseForge;
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
    public async Task PrepareAsyncRejectsManifestEntryOverSizeLimit()
    {
        var archivePath = Path.Combine(TempRoot, "large-manifest.zip");
        CreateArchive(
            archivePath,
            archive => AddEntry(
                archive,
                "manifest.json",
                new string(' ', (int)ModpackArchiveUtility.MaxManifestBytes + 1)));
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<ModpackImportException>(() => service.PrepareAsync(archivePath));

        Assert.Equal(ModpackImportFailureReason.InvalidManifest, exception.FailureReason);
    }

    [Fact]
    public void ZipExtractionBudgetRejectsTotalOverrideSizeOverLimit()
    {
        var budget = new ZipExtractionBudget(1);

        var exception = Assert.Throws<ModpackImportException>(() => budget.Reserve(2));

        Assert.Equal(ModpackImportFailureReason.InvalidManifest, exception.FailureReason);
    }

    [Fact]
    public async Task PrepareAsyncParsesCurseForgeNeoForgeManifest()
    {
        var archivePath = Path.Combine(TempRoot, "curseforge-neoforge.zip");
        CreateArchive(
            archivePath,
            archive =>
            {
                AddEntry(
                    archive,
                    "manifest.json",
                    """
                    {
                      "name": "NeoForge Curse Pack",
                      "minecraft": {
                        "version": "1.20.4",
                        "modLoaders": [
                          { "id": "neoforge-20.4.237", "primary": true }
                        ]
                      },
                      "files": []
                    }
                    """);
                AddEntry(archive, "overrides/config/neoforge.txt", "neoforge");
            });
        var service = CreateService();

        var prepared = await service.PrepareAsync(archivePath);

        Assert.Equal(ModpackPackageKind.CurseForge, prepared.PackageKind);
        Assert.Equal("NeoForge Curse Pack", prepared.PackageName);
        Assert.Equal("1.20.4", prepared.MinecraftVersion);
        Assert.Equal(LoaderKind.NeoForge, prepared.Loader);
        Assert.Equal("20.4.237", prepared.LoaderVersion);
        Assert.NotNull(prepared.OverridesDirectory);
        Assert.True(File.Exists(Path.Combine(prepared.OverridesDirectory!, "config", "neoforge.txt")));
    }

    [Fact]
    public async Task PrepareAsyncDoesNotRequireCurseForgeApiKeyWhenManifestContainsFiles()
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
            var prepared = await service.PrepareAsync(archivePath);
            var file = Assert.Single(prepared.Files);
            Assert.Equal(10, file.ProjectId);
            Assert.Equal(20, file.FileId);
        }
        finally
        {
            Directory.SetCurrentDirectory(previousCurrentDirectory);
            Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", previousValue);
        }
    }

    [Fact]
    public async Task DownloadFilesAsyncUsesLocalSecretFileForCurseForgeApiKey()
    {
        var previousValue = Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY");
        var previousCurrentDirectory = Directory.GetCurrentDirectory();
        Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", null);
        Directory.CreateDirectory(TempRoot);
        Directory.SetCurrentDirectory(TempRoot);

        var expectedApiKey = "local-secret-key";
        await WriteLocalCurseForgeSecretAsync(expectedApiKey);

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
            var instanceDirectory = Path.Combine(TempRoot, "instance");
            Directory.CreateDirectory(instanceDirectory);
            var instance = new GameInstance
            {
                Name = "Curse Pack",
                VersionName = "Curse Pack",
                MinecraftVersion = "1.20.1",
                InstanceDirectory = instanceDirectory
            };

            await service.DownloadFilesAsync(prepared, instance, progress: null);

            Assert.Equal(expectedApiKey, handler.LastApiKey);
            Assert.True(File.Exists(Path.Combine(instanceDirectory, "mods", "example.jar")));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousCurrentDirectory);
            Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", previousValue);
        }
    }

    [Fact]
    public async Task DownloadFilesAsyncUsesCurrentDirectorySecretFileForCurseForgeApiKey()
    {
        var previousValue = Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY");
        var previousCurrentDirectory = Directory.GetCurrentDirectory();
        Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", null);
        Directory.CreateDirectory(TempRoot);
        Directory.SetCurrentDirectory(TempRoot);
        var expectedApiKey = "current-directory-secret";
        var secretsDirectory = Path.Combine(TempRoot, ".local-secrets");
        Directory.CreateDirectory(secretsDirectory);
        await File.WriteAllTextAsync(Path.Combine(secretsDirectory, "curseforge.key"), expectedApiKey);

        var handler = new CurseForgeMetadataHandler();
        var service = CreateService(new HttpClient(handler));
        var prepared = new PreparedModpack
        {
            PackageKind = ModpackPackageKind.CurseForge,
            SourceArchivePath = Path.Combine(TempRoot, "pack.zip"),
            WorkingDirectory = Path.Combine(TempRoot, "work"),
            PackageName = "Curse Pack",
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Vanilla,
            Files =
            [
                new PreparedModpackDownload
                {
                    ProjectId = 10,
                    FileId = 20,
                    TargetDirectory = "mods"
                }
            ]
        };
        var instance = new GameInstance
        {
            Name = "Curse Pack",
            VersionName = "Curse Pack",
            MinecraftVersion = "1.20.1",
            InstanceDirectory = Path.Combine(TempRoot, "instance")
        };

        try
        {
            await service.DownloadFilesAsync(prepared, instance, progress: null);

            Assert.Equal(expectedApiKey, handler.LastApiKey);
        }
        finally
        {
            Directory.SetCurrentDirectory(previousCurrentDirectory);
            Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", previousValue);
        }
    }

    [Fact]
    public async Task PrepareAsyncKeepsCurseForgeFileReferencesLocalWhenDownloadUrlEndpointWouldNeedFallback()
    {
        var previousValue = Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY");
        var previousCurrentDirectory = Directory.GetCurrentDirectory();
        Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", null);
        Directory.CreateDirectory(TempRoot);
        Directory.SetCurrentDirectory(TempRoot);

        var archivePath = Path.Combine(TempRoot, "curseforge-cdn-fallback.zip");
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
                    { "projectID": 348025, "fileID": 4436467 }
                  ]
                }
                """));
        var handler = new CurseForgeCdnFallbackHandler(
            metadataJson:
            """
            {
              "data": {
                "displayName": "SRP v 1.9.11",
                "fileName": "SRParasites-1.12.2v1.9.11.jar",
                "downloadUrl": null,
                "hashes": [
                  { "algo": 1, "value": "d7dacae2c968388960bf8970080a980ed5c5dcb7" }
                ]
              }
            }
            """);
        var service = CreateService(new HttpClient(handler));

        try
        {
            var prepared = await service.PrepareAsync(archivePath);

            var file = Assert.Single(prepared.Files);
            Assert.Equal(348025, file.ProjectId);
            Assert.Equal(4436467, file.FileId);
            Assert.Equal("mods", file.TargetDirectory);
            Assert.Equal(string.Empty, file.SourceUrl);
            Assert.Equal(string.Empty, file.FileName);
        }
        finally
        {
            Directory.SetCurrentDirectory(previousCurrentDirectory);
            Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", previousValue);
        }
    }

    [Fact]
    public async Task DownloadFilesAsyncReportsCurseForgeResolutionProgress()
    {
        var previousValue = Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY");
        var previousCurrentDirectory = Directory.GetCurrentDirectory();
        Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", null);
        Directory.CreateDirectory(TempRoot);
        Directory.SetCurrentDirectory(TempRoot);

        await WriteLocalCurseForgeSecretAsync("local-secret-key");

        var archivePath = Path.Combine(TempRoot, "curseforge-progress.zip");
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
        var progress = new ProgressCollector();

        try
        {
            var prepared = await service.PrepareAsync(archivePath);
            var instanceDirectory = Path.Combine(TempRoot, "instance");
            Directory.CreateDirectory(instanceDirectory);
            var instance = new GameInstance
            {
                Name = "Curse Pack",
                VersionName = "Curse Pack",
                MinecraftVersion = "1.20.1",
                InstanceDirectory = instanceDirectory
            };

            await service.DownloadFilesAsync(prepared, instance, progress: progress);

            Assert.Contains(
                progress.Values,
                value => value.Stage == ImportProgressStages.ResolvingPackFiles
                    && value.Message == "1/1"
                    && value.Percent == 100);
        }
        finally
        {
            Directory.SetCurrentDirectory(previousCurrentDirectory);
            Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", previousValue);
        }
    }

    [Fact]
    public async Task DownloadFilesAsyncLimitsConcurrentCurseForgeApiRequestsToTwo()
    {
        var previousValue = Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY");
        Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", "local-secret-key");

        var archivePath = Path.Combine(TempRoot, "curseforge-api-concurrency.zip");
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
                    { "projectID": 10, "fileID": 20 },
                    { "projectID": 11, "fileID": 21 },
                    { "projectID": 12, "fileID": 22 }
                  ]
                }
                """));
        var handler = new ConcurrentCurseForgeApiHandler();
        var service = CreateService(new HttpClient(handler));

        try
        {
            var prepared = await service.PrepareAsync(archivePath);
            var instanceDirectory = Path.Combine(TempRoot, "instance");
            Directory.CreateDirectory(instanceDirectory);
            var instance = new GameInstance
            {
                Name = "Curse Pack",
                VersionName = "Curse Pack",
                MinecraftVersion = "1.20.1",
                InstanceDirectory = instanceDirectory
            };

            await service.DownloadFilesAsync(prepared, instance, progress: null);

            Assert.Equal(3, prepared.Files.Count);
            Assert.Equal(2, handler.MaxConcurrentRequests);
            Assert.Equal(6, handler.RequestCount);
        }
        finally
        {
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
                      { "id": "rift-1.0.0", "primary": true }
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
    public async Task PrepareAsyncParsesCurseForgeQuiltManifest()
    {
        var archivePath = Path.Combine(TempRoot, "curseforge-quilt.zip");
        CreateArchive(
            archivePath,
            archive =>
            {
                AddEntry(
                    archive,
                    "manifest.json",
                    """
                    {
                      "name": "Quilt Curse Pack",
                      "minecraft": {
                        "version": "1.20.2",
                        "modLoaders": [
                          { "id": "quilt-0.29.2", "primary": true }
                        ]
                      },
                      "files": []
                    }
                    """);
                AddEntry(archive, "overrides/config/quilt.txt", "quilt");
            });
        var service = CreateService();

        var prepared = await service.PrepareAsync(archivePath);

        Assert.Equal(ModpackPackageKind.CurseForge, prepared.PackageKind);
        Assert.Equal("Quilt Curse Pack", prepared.PackageName);
        Assert.Equal("1.20.2", prepared.MinecraftVersion);
        Assert.Equal(LoaderKind.Quilt, prepared.Loader);
        Assert.Equal("0.29.2", prepared.LoaderVersion);
        Assert.NotNull(prepared.OverridesDirectory);
        Assert.True(File.Exists(Path.Combine(prepared.OverridesDirectory!, "config", "quilt.txt")));
    }

    [Fact]
    public async Task PrepareAsyncParsesModrinthNeoForgeManifest()
    {
        var archivePath = Path.Combine(TempRoot, "neoforge-pack.mrpack");
        CreateArchive(
            archivePath,
            archive =>
            {
                AddEntry(
                    archive,
                    "modrinth.index.json",
                    """
                    {
                      "name": "NeoForge Pack",
                      "dependencies": {
                        "minecraft": "1.20.4",
                        "neoforge": "20.4.237"
                      },
                      "files": []
                    }
                    """);
                AddEntry(archive, "overrides/config/neoforge-options.txt", "value=true");
            });
        var service = CreateService();

        var prepared = await service.PrepareAsync(archivePath);

        Assert.Equal(ModpackPackageKind.Modrinth, prepared.PackageKind);
        Assert.Equal("NeoForge Pack", prepared.PackageName);
        Assert.Equal("1.20.4", prepared.MinecraftVersion);
        Assert.Equal(LoaderKind.NeoForge, prepared.Loader);
        Assert.Equal("20.4.237", prepared.LoaderVersion);
        Assert.NotNull(prepared.OverridesDirectory);
        Assert.True(File.Exists(Path.Combine(prepared.OverridesDirectory!, "config", "neoforge-options.txt")));
    }

    [Fact]
    public async Task PrepareAsyncParsesModrinthQuiltManifest()
    {
        var archivePath = Path.Combine(TempRoot, "quilt-pack.mrpack");
        CreateArchive(
            archivePath,
            archive =>
            {
                AddEntry(
                    archive,
                    "modrinth.index.json",
                    """
                    {
                      "name": "Quilt Pack",
                      "dependencies": {
                        "minecraft": "1.20.2",
                        "quilt-loader": "0.29.2"
                      },
                      "files": []
                    }
                    """);
                AddEntry(archive, "overrides/config/quilt-options.txt", "value=true");
            });
        var service = CreateService();

        var prepared = await service.PrepareAsync(archivePath);

        Assert.Equal(ModpackPackageKind.Modrinth, prepared.PackageKind);
        Assert.Equal("Quilt Pack", prepared.PackageName);
        Assert.Equal("1.20.2", prepared.MinecraftVersion);
        Assert.Equal(LoaderKind.Quilt, prepared.Loader);
        Assert.Equal("0.29.2", prepared.LoaderVersion);
        Assert.NotNull(prepared.OverridesDirectory);
        Assert.True(File.Exists(Path.Combine(prepared.OverridesDirectory!, "config", "quilt-options.txt")));
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

    [Fact]
    public async Task InstallContentAsyncLimitsConcurrentDownloadsToFourFiles()
    {
        var handler = new ConcurrentDownloadHandler();
        var service = CreateService(new HttpClient(handler));
        var prepared = new PreparedModpack
        {
            PackageKind = ModpackPackageKind.Modrinth,
            SourceArchivePath = Path.Combine(TempRoot, "pack.mrpack"),
            WorkingDirectory = Path.Combine(TempRoot, "work"),
            PackageName = "Concurrent Pack",
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Vanilla,
            Files =
            [
                new PreparedModpackDownload { FileName = "mod-1.jar", RelativePath = "mods/mod-1.jar", SourceUrl = "https://example.com/mod-1.jar" },
                new PreparedModpackDownload { FileName = "mod-2.jar", RelativePath = "mods/mod-2.jar", SourceUrl = "https://example.com/mod-2.jar" },
                new PreparedModpackDownload { FileName = "mod-3.jar", RelativePath = "mods/mod-3.jar", SourceUrl = "https://example.com/mod-3.jar" },
                new PreparedModpackDownload { FileName = "mod-4.jar", RelativePath = "mods/mod-4.jar", SourceUrl = "https://example.com/mod-4.jar" },
                new PreparedModpackDownload { FileName = "mod-5.jar", RelativePath = "mods/mod-5.jar", SourceUrl = "https://example.com/mod-5.jar" }
            ]
        };
        Directory.CreateDirectory(prepared.WorkingDirectory);
        var instance = new GameInstance
        {
            Name = "Concurrent Pack",
            VersionName = "Concurrent Pack",
            MinecraftVersion = "1.20.1",
            InstanceDirectory = Path.Combine(TempRoot, "instance")
        };
        Directory.CreateDirectory(instance.InstanceDirectory);

        await service.InstallContentAsync(prepared, instance, progress: null);

        Assert.Equal(5, handler.RequestCount);
        Assert.Equal(4, handler.MaxConcurrentRequests);
        Assert.True(File.Exists(Path.Combine(instance.InstanceDirectory, "mods", "mod-1.jar")));
        Assert.True(File.Exists(Path.Combine(instance.InstanceDirectory, "mods", "mod-5.jar")));
    }

    [Fact]
    public async Task InstallContentAsyncAppliesSharedSpeedLimitAcrossParallelDownloads()
    {
        var handler = new ConcurrentDownloadHandler(
            contentFactory: () => new ByteArrayContent(new byte[1024 * 1024]));
        var service = CreateService(new HttpClient(handler));
        var prepared = new PreparedModpack
        {
            PackageKind = ModpackPackageKind.Modrinth,
            SourceArchivePath = Path.Combine(TempRoot, "pack.mrpack"),
            WorkingDirectory = Path.Combine(TempRoot, "work"),
            PackageName = "Throttled Pack",
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Vanilla,
            Files =
            [
                new PreparedModpackDownload { FileName = "mod-1.jar", RelativePath = "mods/mod-1.jar", SourceUrl = "https://example.com/mod-1.jar" },
                new PreparedModpackDownload { FileName = "mod-2.jar", RelativePath = "mods/mod-2.jar", SourceUrl = "https://example.com/mod-2.jar" },
                new PreparedModpackDownload { FileName = "mod-3.jar", RelativePath = "mods/mod-3.jar", SourceUrl = "https://example.com/mod-3.jar" },
                new PreparedModpackDownload { FileName = "mod-4.jar", RelativePath = "mods/mod-4.jar", SourceUrl = "https://example.com/mod-4.jar" }
            ]
        };
        Directory.CreateDirectory(prepared.WorkingDirectory);
        var instance = new GameInstance
        {
            Name = "Throttled Pack",
            VersionName = "Throttled Pack",
            MinecraftVersion = "1.20.1",
            InstanceDirectory = Path.Combine(TempRoot, "instance")
        };
        Directory.CreateDirectory(instance.InstanceDirectory);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await service.InstallContentAsync(
            prepared,
            instance,
            progress: null,
            downloadSpeedLimitMbPerSecond: 2);
        stopwatch.Stop();

        Assert.Equal(4, handler.RequestCount);
        Assert.Equal(4, handler.MaxConcurrentRequests);
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(1300));
    }

    [Fact]
    public async Task InstallContentAsyncKeepsCurseForgeImportAndWritesManualDownloadListWhenAllSourcesFail()
    {
        var previousValue = Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY");
        var previousCurrentDirectory = Directory.GetCurrentDirectory();
        Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", null);
        Directory.CreateDirectory(TempRoot);
        Directory.SetCurrentDirectory(TempRoot);

        await WriteLocalCurseForgeSecretAsync("local-secret-key");

        var handler = new CurseForgeCdnFallbackHandler(
            metadataJson:
            """
            {
              "data": {
                "displayName": "SRP v 1.9.11",
                "fileName": "SRParasites-1.12.2v1.9.11.jar",
                "downloadUrl": null,
                "hashes": [
                  { "algo": 1, "value": "d7dacae2c968388960bf8970080a980ed5c5dcb7" }
                ]
              }
            }
            """,
            edgeStatusCode: HttpStatusCode.Forbidden,
            mediafilezStatusCode: HttpStatusCode.Forbidden);
        var service = CreateService(new HttpClient(handler));
        var prepared = new PreparedModpack
        {
            PackageKind = ModpackPackageKind.CurseForge,
            SourceArchivePath = Path.Combine(TempRoot, "pack.zip"),
            WorkingDirectory = Path.Combine(TempRoot, "work"),
            PackageName = "Curse Pack",
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Vanilla,
            Files =
            [
                new PreparedModpackDownload
                {
                    ProjectId = 348025,
                    FileId = 4436467,
                    TargetDirectory = "mods"
                }
            ]
        };
        Directory.CreateDirectory(prepared.WorkingDirectory);
        var instance = new GameInstance
        {
            Name = "Curse Pack",
            VersionName = "Curse Pack",
            MinecraftVersion = "1.20.1",
            InstanceDirectory = Path.Combine(TempRoot, "instance")
        };
        Directory.CreateDirectory(instance.InstanceDirectory);

        try
        {
            await service.InstallContentAsync(prepared, instance, progress: null);

            var manualDownload = Assert.Single(prepared.ManualDownloads);
            Assert.Equal(348025, manualDownload.ProjectId);
            Assert.Equal(4436467, manualDownload.FileId);
            Assert.Equal("SRParasites-1.12.2v1.9.11.jar", manualDownload.FileName);
            Assert.Equal("https://edge.forgecdn.net/files/4436/467/SRParasites-1.12.2v1.9.11.jar", manualDownload.SuggestedUrl);
            Assert.NotNull(prepared.ManualDownloadsFilePath);
            Assert.True(File.Exists(prepared.ManualDownloadsFilePath));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousCurrentDirectory);
            Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", previousValue);
        }
    }

    [Fact]
    public async Task InstallContentAsyncValidatesCurseForgeCdnFallbackDownloads()
    {
        var previousValue = Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY");
        var previousCurrentDirectory = Directory.GetCurrentDirectory();
        Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", null);
        Directory.CreateDirectory(TempRoot);
        Directory.SetCurrentDirectory(TempRoot);

        await WriteLocalCurseForgeSecretAsync("local-secret-key");

        var handler = new CurseForgeCdnFallbackHandler(
            metadataJson:
            """
            {
              "data": {
                "displayName": "SRP v 1.9.11",
                "fileName": "SRParasites-1.12.2v1.9.11.jar",
                "downloadUrl": null,
                "hashes": [
                  { "algo": 1, "value": "d7dacae2c968388960bf8970080a980ed5c5dcb7" }
                ]
              }
            }
            """,
            edgeContent: "wrong-bytes");
        var service = CreateService(new HttpClient(handler));
        var prepared = new PreparedModpack
        {
            PackageKind = ModpackPackageKind.CurseForge,
            SourceArchivePath = Path.Combine(TempRoot, "pack.zip"),
            WorkingDirectory = Path.Combine(TempRoot, "work"),
            PackageName = "Curse Pack",
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Vanilla,
            Files =
            [
                new PreparedModpackDownload
                {
                    ProjectId = 348025,
                    FileId = 4436467,
                    TargetDirectory = "mods"
                }
            ]
        };
        Directory.CreateDirectory(prepared.WorkingDirectory);
        var instance = new GameInstance
        {
            Name = "Curse Pack",
            VersionName = "Curse Pack",
            MinecraftVersion = "1.20.1",
            InstanceDirectory = Path.Combine(TempRoot, "instance")
        };
        Directory.CreateDirectory(instance.InstanceDirectory);

        try
        {
            await service.InstallContentAsync(prepared, instance, progress: null);

            var manualDownload = Assert.Single(prepared.ManualDownloads);
            Assert.Equal("hash_mismatch", manualDownload.FailureSummary);
            Assert.Equal("local-secret-key", handler.LastEdgeApiKey);
        }
        finally
        {
            Directory.SetCurrentDirectory(previousCurrentDirectory);
            Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", previousValue);
        }
    }

    private LocalModpackPackageService CreateService(HttpClient? httpClient = null)
    {
        var pathProvider = new LauncherPathProvider(TempRoot);
        return new LocalModpackPackageService(
            pathProvider,
            httpClient: httpClient,
            curseForgeApiKeyResolver: new CurseForgeApiKeyResolver(
                pathProvider,
                embeddedApiKeyProvider: _ => Task.FromResult<string?>(null)));
    }

    private async Task WriteLocalCurseForgeSecretAsync(string apiKey)
    {
        var secretsDirectory = Path.Combine(
            new LauncherPathProvider(TempRoot).DefaultDataDirectory,
            ".local-secrets");
        Directory.CreateDirectory(secretsDirectory);
        await File.WriteAllTextAsync(Path.Combine(secretsDirectory, "curseforge.key"), apiKey);
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

    private sealed class ConcurrentDownloadHandler : HttpMessageHandler
    {
        private readonly Func<HttpContent> contentFactory;
        private int requestCount;
        private int activeRequests;
        private int maxConcurrentRequests;

        public ConcurrentDownloadHandler(Func<HttpContent>? contentFactory = null)
        {
            this.contentFactory = contentFactory
                ?? (() => new StringContent("downloaded-bytes", Encoding.UTF8, "application/octet-stream"));
        }

        public int RequestCount => Volatile.Read(ref requestCount);

        public int MaxConcurrentRequests => Volatile.Read(ref maxConcurrentRequests);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requestCount);
            var activeRequestCount = Interlocked.Increment(ref activeRequests);
            UpdateMaxConcurrentRequests(activeRequestCount);

            try
            {
                await Task.Delay(80, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = contentFactory()
                };
            }
            finally
            {
                Interlocked.Decrement(ref activeRequests);
            }
        }

        private void UpdateMaxConcurrentRequests(int activeRequestCount)
        {
            while (true)
            {
                var currentMax = Volatile.Read(ref maxConcurrentRequests);
                if (activeRequestCount <= currentMax)
                    return;

                if (Interlocked.CompareExchange(ref maxConcurrentRequests, activeRequestCount, currentMax) == currentMax)
                    return;
            }
        }
    }

    private sealed class ConcurrentCurseForgeApiHandler : HttpMessageHandler
    {
        private int requestCount;
        private int activeRequests;
        private int maxConcurrentRequests;

        public int RequestCount => Volatile.Read(ref requestCount);

        public int MaxConcurrentRequests => Volatile.Read(ref maxConcurrentRequests);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var shouldTrackConcurrency = string.Equals(request.RequestUri?.Host, "api.curseforge.com", StringComparison.OrdinalIgnoreCase);
            if (shouldTrackConcurrency)
            {
                Interlocked.Increment(ref requestCount);
                var activeRequestCount = Interlocked.Increment(ref activeRequests);
                UpdateMaxConcurrentRequests(activeRequestCount);
            }

            try
            {
                await Task.Delay(80, cancellationToken);
                var content = request.RequestUri?.AbsolutePath.EndsWith("/download-url", StringComparison.OrdinalIgnoreCase) == true
                    ? """
                      {
                        "data": "https://example.com/example.jar"
                      }
                      """
                    : """
                      {
                        "data": {
                          "displayName": "example",
                          "fileName": "example.jar",
                          "downloadUrl": "https://example.com/example.jar"
                        }
                      }
                      """;

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(content, Encoding.UTF8, "application/json")
                };
            }
            finally
            {
                if (shouldTrackConcurrency)
                    Interlocked.Decrement(ref activeRequests);
            }
        }

        private void UpdateMaxConcurrentRequests(int activeRequestCount)
        {
            while (true)
            {
                var currentMax = Volatile.Read(ref maxConcurrentRequests);
                if (activeRequestCount <= currentMax)
                    return;

                if (Interlocked.CompareExchange(ref maxConcurrentRequests, activeRequestCount, currentMax) == currentMax)
                    return;
            }
        }
    }

    private sealed class CurseForgeMetadataHandler : HttpMessageHandler
    {
        public string? LastApiKey { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.TryGetValues("x-api-key", out var values))
                LastApiKey = values.SingleOrDefault();

            var content = request.RequestUri?.AbsolutePath.EndsWith("/download-url", StringComparison.OrdinalIgnoreCase) == true
                ? """
                  {
                    "data": "https://example.com/example.jar"
                  }
                  """
                : """
                  {
                    "data": {
                      "fileName": "example.jar",
                      "downloadUrl": "https://example.com/example.jar"
                    }
                  }
                  """;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class CurseForgeCdnFallbackHandler : HttpMessageHandler
    {
        private readonly string metadataJson;
        private readonly HttpStatusCode edgeStatusCode;
        private readonly HttpStatusCode mediafilezStatusCode;
        private readonly string edgeContent;

        public CurseForgeCdnFallbackHandler(
            string metadataJson,
            HttpStatusCode edgeStatusCode = HttpStatusCode.OK,
            HttpStatusCode mediafilezStatusCode = HttpStatusCode.OK,
            string edgeContent = "expected-bytes")
        {
            this.metadataJson = metadataJson;
            this.edgeStatusCode = edgeStatusCode;
            this.mediafilezStatusCode = mediafilezStatusCode;
            this.edgeContent = edgeContent;
        }

        public string? LastEdgeApiKey { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is null)
                throw new InvalidOperationException("RequestUri was not provided.");

            if (request.RequestUri.Host.Equals("api.curseforge.com", StringComparison.OrdinalIgnoreCase))
            {
                if (request.RequestUri.AbsolutePath.EndsWith("/download-url", StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(metadataJson, Encoding.UTF8, "application/json")
                });
            }

            if (request.RequestUri.Host.Equals("edge.forgecdn.net", StringComparison.OrdinalIgnoreCase))
            {
                LastEdgeApiKey = request.Headers.TryGetValues("x-api-key", out var values)
                    ? values.SingleOrDefault()
                    : null;
                return Task.FromResult(new HttpResponseMessage(edgeStatusCode)
                {
                    Content = new StringContent(edgeContent, Encoding.UTF8, "application/octet-stream")
                });
            }

            if (request.RequestUri.Host.Equals("mediafilez.forgecdn.net", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(mediafilezStatusCode)
                {
                    Content = new StringContent("backup-content", Encoding.UTF8, "application/octet-stream")
                });
            }

            throw new InvalidOperationException($"Unexpected request URI: {request.RequestUri}");
        }
    }

    private sealed class ProgressCollector : IProgress<LauncherProgress>
    {
        public List<LauncherProgress> Values { get; } = [];

        public void Report(LauncherProgress value)
        {
            Values.Add(value);
        }
    }
}
