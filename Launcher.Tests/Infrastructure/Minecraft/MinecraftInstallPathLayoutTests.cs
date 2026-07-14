/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO.Compression;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using CmlLib.Core;
using CmlLib.Core.Files;
using CmlLib.Core.Installers;
using CmlLib.Core.FileExtractors;
using CmlLib.Core.Rules;
using CmlLib.Core.Version;
using Launcher.Infrastructure.Minecraft;
using Launcher.Infrastructure.Modpacks;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class MinecraftInstallPathLayoutTests : TestTempDirectory
{
    [Fact]
    public void SplitLayoutKeepsVersionsPrivateAndSharedRuntimeOutsideSandbox()
    {
        var sandbox = Path.Combine(TempRoot, "sandbox", ".minecraft");
        var realMinecraft = Path.Combine(TempRoot, "real", ".minecraft");

        var layout = MinecraftInstallPathLayout.Create(sandbox, realMinecraft);

        Assert.Equal(Path.GetFullPath(Path.Combine(sandbox, "versions")), layout.Path.Versions);
        Assert.Equal(Path.GetFullPath(Path.Combine(realMinecraft, "libraries")), layout.Path.Library);
        Assert.Equal(Path.GetFullPath(Path.Combine(realMinecraft, "assets")), layout.Path.Assets);
        Assert.Equal(Path.GetFullPath(Path.Combine(realMinecraft, "resources")), layout.Path.Resource);
        Assert.Equal(Path.GetFullPath(Path.Combine(realMinecraft, "runtime")), layout.Path.Runtime);
        Assert.False(Directory.Exists(Path.Combine(sandbox, "libraries")));
        Assert.False(Directory.Exists(Path.Combine(sandbox, "assets", "objects")));
        Assert.False(Directory.Exists(Path.Combine(realMinecraft, "versions", "Test")));
    }

    [Fact]
    public void CmlLibResolvesLibraryAndVersionArtifactsThroughSeparatedProperties()
    {
        var sandbox = Path.Combine(TempRoot, "sandbox", ".minecraft");
        var realMinecraft = Path.Combine(TempRoot, "real", ".minecraft");
        var path = MinecraftInstallPathLayout.Create(sandbox, realMinecraft).Path;
        var relativeLibraryPath = "com/example/library/1.0/library-1.0.jar";
        var library = new MLibrary("com.example:library:1.0")
        {
            Artifact = new MFileMetadata
            {
                Path = relativeLibraryPath,
                Url = "https://example.invalid/library.jar",
                Sha1 = new string('a', 40)
            }
        };

        var resolvedLibrary = Assert.Single(LibraryFileExtractor.Extractor.ExtractTasks(
            "https://libraries.minecraft.net/",
            path,
            library,
            new RulesEvaluatorContext(LauncherOSRule.Current)));

        Assert.Equal(
            Path.GetFullPath(Path.Combine(realMinecraft, "libraries", relativeLibraryPath)),
            resolvedLibrary.Path);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(sandbox, "versions", "Test", "Test.jar")),
            path.GetVersionJarPath("Test"));
        Assert.StartsWith(Path.GetFullPath(Path.Combine(realMinecraft, "assets")), path.GetAssetObjectPath("assets"));
        Assert.False(Directory.Exists(Path.Combine(sandbox, "libraries")));
        Assert.False(Directory.Exists(Path.Combine(sandbox, "assets", "objects")));
        Assert.False(Directory.Exists(Path.Combine(realMinecraft, "versions", "Test")));
    }

    [Fact]
    public void LauncherReplacesCmlLibAssetExtractorWithSafeAdapter()
    {
        var sandbox = Path.Combine(TempRoot, "sandbox", ".minecraft");
        var realMinecraft = Path.Combine(TempRoot, "real", ".minecraft");
        var layout = MinecraftInstallPathLayout.Create(sandbox, realMinecraft);

        var launcher = VanillaLoaderProvider.CreateLauncher(layout.Path, progress: null);

        Assert.Contains(launcher.FileExtractors, extractor => extractor is SafeAssetFileExtractor);
        Assert.DoesNotContain(
            launcher.FileExtractors,
            extractor => extractor.GetType() == typeof(CmlLib.Core.FileExtractors.AssetFileExtractor));
        Assert.Equal(Path.Combine(sandbox, "versions"), launcher.MinecraftPath.Versions);
        Assert.Equal(Path.Combine(realMinecraft, "libraries"), launcher.MinecraftPath.Library);
    }

    [Fact]
    public void GameInstallerUsesGlobalDownloadMaximumWhileKeepingCheckerLimit()
    {
        var sandbox = Path.Combine(TempRoot, "sandbox", ".minecraft");
        var realMinecraft = Path.Combine(TempRoot, "real", ".minecraft");
        var layout = MinecraftInstallPathLayout.Create(sandbox, realMinecraft);

        var launcher = VanillaLoaderProvider.CreateLauncher(layout.Path, progress: null);
        var installer = Assert.IsType<DownloadSpeedTrackingGameInstaller>(launcher.GameInstaller);

        Assert.Equal(Math.Min(4, Math.Max(1, Environment.ProcessorCount)), installer.ConfiguredMaxChecker);
        Assert.Equal(ImportConcurrencyLimiter.MaximumDownloadConcurrency, installer.ConfiguredMaxDownloader);
        Assert.Equal(16, installer.ConfiguredMaxDownloader);
    }

    [Fact]
    public void CmlFileChecksRemainCheckingWhileAnotherFileDownloads()
    {
        var reports = new List<LauncherProgress>();
        var launcher = VanillaLoaderProvider.CreateLauncher(
            new MinecraftPath(Path.Combine(TempRoot, ".minecraft")),
            new InlineProgress(reports));
        VanillaLoaderProvider.AttachProgress(launcher, new InlineProgress(reports));
        var installer = Assert.IsType<DownloadSpeedTrackingGameInstaller>(launcher.GameInstaller);
        var speedReporter = Assert.IsType<SlidingWindowDownloadSpeedReporter>(GetPrivateField(installer, "speedReporter"));
        speedReporter.BeginTransfer();

        RaisePrivateEvent(
            launcher,
            "FileProgressChanged",
            new InstallerProgressChangedEventArgs(2, 1, "library.jar", InstallerEventType.Done));
        RaisePrivateEvent(launcher, "ByteProgressChanged", new ByteProgress(50, 100));

        Assert.Collection(reports,
            progress => Assert.Equal(LaunchProgressStages.CheckingFiles, progress.Stage),
            progress => Assert.Equal(LaunchProgressStages.DownloadingFiles, progress.Stage));
    }

    [Fact]
    public async Task CmlLibDownloadCannotEscapeConfiguredInstallRoots()
    {
        var sandbox = Path.Combine(TempRoot, "sandbox", ".minecraft");
        var realMinecraft = Path.Combine(TempRoot, "real", ".minecraft");
        var layout = MinecraftInstallPathLayout.Create(sandbox, realMinecraft);
        var launcher = VanillaLoaderProvider.CreateLauncher(layout.Path, progress: null);
        var installer = Assert.IsType<DownloadSpeedTrackingGameInstaller>(launcher.GameInstaller);
        var escapedPath = Path.Combine(TempRoot, "escaped.jar");

        await Assert.ThrowsAsync<InvalidDataException>(() => installer.DownloadGameFileAsync(
            new GameFile("escaped")
            {
                Path = escapedPath,
                Url = "https://example.invalid/escaped.jar"
            },
            progress: null,
            CancellationToken.None));

        Assert.False(File.Exists(escapedPath));
    }

    [Fact]
    public async Task AtomicPublisherRejectsDifferentExistingContent()
    {
        var source = CreateFile("source.bin", "source");
        var destination = CreateFile("destination.bin", "different");

        await Assert.ThrowsAsync<IOException>(() => AtomicSharedFilePublisher.PublishCopyAsync(
            source,
            destination,
            ComputeSha1("source")));

        Assert.Equal("different", await File.ReadAllTextAsync(destination));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(destination)!, ".destination.bin.*.tmp"));
    }

    [Fact]
    public async Task AtomicPublisherTreatsConcurrentIdenticalContentAsSuccess()
    {
        var firstSource = CreateFile(Path.Combine("first", "library.jar"), new string('A', 256 * 1024));
        var secondSource = CreateFile(Path.Combine("second", "library.jar"), new string('A', 256 * 1024));
        var destination = Path.Combine(TempRoot, "shared", "library.jar");
        var hash = AtomicSharedFilePublisher.ComputeSha1(firstSource);

        await Task.WhenAll(
            AtomicSharedFilePublisher.PublishCopyAsync(firstSource, destination, hash),
            AtomicSharedFilePublisher.PublishCopyAsync(secondSource, destination, hash));

        Assert.Equal(hash, AtomicSharedFilePublisher.ComputeSha1(destination), ignoreCase: true);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(destination)!, ".library.jar.*.tmp"));
    }

    [Fact]
    public async Task AtomicPublisherReplacesDifferentContentOnlyAfterSourceHashIsVerified()
    {
        var source = CreateFile("source-index.json", "current");
        var destination = CreateFile("destination-index.json", "stale");
        var expectedSha1 = AtomicSharedFilePublisher.ComputeSha1(source);

        var result = await AtomicSharedFilePublisher.PublishVerifiedReplacementAsync(
            source,
            destination,
            expectedSha1,
            CancellationToken.None);

        Assert.Equal(SharedFilePublishDisposition.Replaced, result.Disposition);
        Assert.Equal("current", await File.ReadAllTextAsync(destination));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(destination)!, ".destination-index.json.*.tmp"));
    }

    [Fact]
    public async Task AtomicPublisherPreservesDestinationWhenReplacementSourceHashIsInvalid()
    {
        var source = CreateFile("source-index.json", "untrusted");
        var destination = CreateFile("destination-index.json", "stale");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            AtomicSharedFilePublisher.PublishVerifiedReplacementAsync(
                source,
                destination,
                ComputeSha1("expected"),
                CancellationToken.None));

        Assert.Equal("stale", await File.ReadAllTextAsync(destination));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(destination)!, ".destination-index.json.*.tmp"));
    }

    [Fact]
    public async Task ForgePrerequisitesSeedOnlyReferencedLibrariesAndPublishRequiredWorkspaceFiles()
    {
        var shared = Path.Combine(TempRoot, "shared");
        var workspace = Path.Combine(TempRoot, "installer", ".minecraft");
        var referencedRelativePath = Path.Combine("com", "example", "needed", "1.0", "needed-1.0.jar");
        var unrelatedRelativePath = Path.Combine("com", "example", "unused", "1.0", "unused-1.0.jar");
        var referencedSource = CreateFile(Path.Combine("shared", "libraries", referencedRelativePath), "needed");
        CreateFile(Path.Combine("shared", "libraries", unrelatedRelativePath), "unused");
        var installerJar = Path.Combine(TempRoot, "installer.jar");
        CreateInstallerArchive(installerJar, referencedRelativePath.Replace('\\', '/'), AtomicSharedFilePublisher.ComputeSha1(referencedSource));
        var seeder = new LoaderInstallerPrerequisiteSeeder();

        var snapshot = await seeder.SeedAsync(shared, workspace, "1.20.1", installerJar, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(workspace, "libraries", referencedRelativePath)));
        Assert.False(File.Exists(Path.Combine(workspace, "libraries", unrelatedRelativePath)));
        var newRelativePath = Path.Combine("net", "forge", "new", "1.0", "new-1.0.jar");
        CreateFile(Path.Combine("installer", ".minecraft", "libraries", newRelativePath), "new");
        var destination = Path.Combine(TempRoot, "published");
        await seeder.PublishDeltaAsync(snapshot, destination, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(destination, "libraries", referencedRelativePath)));
        Assert.True(File.Exists(Path.Combine(destination, "libraries", newRelativePath)));
        Assert.False(File.Exists(Path.Combine(destination, "libraries", unrelatedRelativePath)));
    }

    [Fact]
    public async Task ForgeSandboxSeedingDoesNotMirrorTheBaseVersionLibraryGraph()
    {
        var shared = Path.Combine(TempRoot, "shared");
        var workspace = Path.Combine(TempRoot, "installer", ".minecraft");
        var baseLibrary = Path.Combine("com", "example", "base", "1.0", "base-1.0.jar");
        var clientSha1 = ComputeSha1("jar");
        CreateFile(Path.Combine("shared", "versions", "1.20.1", "1.20.1.json"), $$"""
            {
              "id": "1.20.1",
              "downloads": { "client": { "sha1": "{{clientSha1}}", "size": 3 } },
              "libraries": [{ "name": "com.example:base:1.0" }]
            }
            """);
        CreateFile(Path.Combine("shared", "versions", "1.20.1", "1.20.1.jar"), "jar");
        CreateFile(Path.Combine("shared", "libraries", baseLibrary), "base");

        await new LoaderInstallerPrerequisiteSeeder().SeedAsync(
            shared,
            workspace,
            "1.20.1",
            Path.Combine(TempRoot, "missing-installer.jar"),
            CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(workspace, "versions", "1.20.1", "1.20.1.json")));
        Assert.True(File.Exists(Path.Combine(workspace, "versions", "1.20.1", "1.20.1.jar")));
        Assert.False(File.Exists(Path.Combine(workspace, "libraries", baseLibrary)));
    }

    [Fact]
    public async Task ForgePrerequisiteModificationStopsPublication()
    {
        var shared = Path.Combine(TempRoot, "shared");
        var workspace = Path.Combine(TempRoot, "installer", ".minecraft");
        var relativePath = Path.Combine("com", "example", "needed", "1.0", "needed-1.0.jar");
        var source = CreateFile(Path.Combine("shared", "libraries", relativePath), "needed");
        var installerJar = Path.Combine(TempRoot, "installer.jar");
        CreateInstallerArchive(installerJar, relativePath.Replace('\\', '/'), AtomicSharedFilePublisher.ComputeSha1(source));
        var seeder = new LoaderInstallerPrerequisiteSeeder();
        var snapshot = await seeder.SeedAsync(shared, workspace, "1.20.1", installerJar, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(workspace, "libraries", relativePath), "modified");

        await Assert.ThrowsAsync<InvalidDataException>(() => seeder.PublishDeltaAsync(
            snapshot,
            Path.Combine(TempRoot, "published"),
            CancellationToken.None));
    }

    [Fact]
    public async Task TrustedSharedPublicationRecordsTheVerifiedFormalFileForTheCurrentOperation()
    {
        var workspace = Path.Combine(TempRoot, "installer", ".minecraft");
        var relativePath = "libraries/com/example/needed/1.0/needed-1.0.jar";
        var source = CreateFile(Path.Combine("installer", ".minecraft", relativePath.Replace('/', Path.DirectorySeparatorChar)), "needed");
        var expectation = new VerifiedSharedFileExpectation(
            AtomicSharedFilePublisher.ComputeSha1(source),
            new FileInfo(source).Length);
        var destination = Path.Combine(TempRoot, "shared");
        using var operation = new MinecraftDownloadOperationContext(destination);

        await new LoaderInstallerPrerequisiteSeeder().PublishDeltaAsync(
            EmptySnapshot(workspace),
            destination,
            CancellationToken.None,
            new Dictionary<string, VerifiedSharedFileExpectation>(StringComparer.OrdinalIgnoreCase)
            {
                [relativePath] = expectation
            },
            operation);

        var publishedPath = Path.Combine(destination, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(operation.IsVerified(
            publishedPath,
            DownloadIntegrityExpectation.Sha1(expectation.Sha1, expectation.Size)));
    }

    [Fact]
    public async Task ForgePrerequisiteConflictStopsPublication()
    {
        var shared = Path.Combine(TempRoot, "shared");
        var workspace = Path.Combine(TempRoot, "installer", ".minecraft");
        var relativePath = Path.Combine("com", "example", "needed", "1.0", "needed-1.0.jar");
        var source = CreateFile(Path.Combine("shared", "libraries", relativePath), "needed");
        var installerJar = Path.Combine(TempRoot, "installer.jar");
        CreateInstallerArchive(installerJar, relativePath.Replace('\\', '/'), AtomicSharedFilePublisher.ComputeSha1(source));
        var seeder = new LoaderInstallerPrerequisiteSeeder();
        var snapshot = await seeder.SeedAsync(shared, workspace, "1.20.1", installerJar, CancellationToken.None);
        var destination = Path.Combine(TempRoot, "published");
        var conflictingFile = CreateFile(Path.Combine("published", "libraries", relativePath), "different");

        await Assert.ThrowsAsync<IOException>(() => seeder.PublishDeltaAsync(
            snapshot,
            destination,
            CancellationToken.None));

        Assert.Equal("different", await File.ReadAllTextAsync(conflictingFile));
    }

    [Fact]
    public async Task LoaderDeltaReplacesStaleAssetIndexWhenVersionMetadataMatchesSource()
    {
        var workspace = Path.Combine(TempRoot, "installer", ".minecraft");
        var source = CreateFile(Path.Combine("installer", ".minecraft", "assets", "indexes", "5.json"), "current");
        var sourceSha1 = AtomicSharedFilePublisher.ComputeSha1(source);
        CreateFile(
            Path.Combine("installer", ".minecraft", "versions", "Test", "Test.json"),
            $"{{\"assetIndex\":{{\"id\":\"5\",\"sha1\":\"{sourceSha1}\",\"size\":{new FileInfo(source).Length}}}}}");
        var destination = Path.Combine(TempRoot, "published");
        var destinationIndex = CreateFile(Path.Combine("published", "assets", "indexes", "5.json"), "stale");
        var snapshot = new LoaderInstallerWorkspaceSnapshot(
            workspace,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        await new LoaderInstallerPrerequisiteSeeder().PublishDeltaAsync(
            snapshot,
            destination,
            CancellationToken.None);

        Assert.Equal("current", await File.ReadAllTextAsync(destinationIndex));
    }

    [Fact]
    public async Task LoaderDeltaPreservesStaleAssetIndexWhenVersionMetadataDoesNotMatchSource()
    {
        var workspace = Path.Combine(TempRoot, "installer", ".minecraft");
        CreateFile(Path.Combine("installer", ".minecraft", "assets", "indexes", "5.json"), "tampered");
        CreateFile(
            Path.Combine("installer", ".minecraft", "versions", "Test", "Test.json"),
            $"{{\"assetIndex\":{{\"id\":\"5\",\"sha1\":\"{ComputeSha1("expected")}\",\"size\":8}}}}");
        var destination = Path.Combine(TempRoot, "published");
        var destinationIndex = CreateFile(Path.Combine("published", "assets", "indexes", "5.json"), "stale");
        var snapshot = new LoaderInstallerWorkspaceSnapshot(
            workspace,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new LoaderInstallerPrerequisiteSeeder().PublishDeltaAsync(
                snapshot,
                destination,
                CancellationToken.None));

        Assert.Equal("stale", await File.ReadAllTextAsync(destinationIndex));
    }

    [Fact]
    public async Task LoaderDeltaReplacesLoggingConfigWhenVersionMetadataMatchesSource()
    {
        var workspace = Path.Combine(TempRoot, "installer", ".minecraft");
        var source = CreateFile(
            Path.Combine("installer", ".minecraft", "assets", "log_configs", "client.xml"),
            "current-log");
        var sourceSha1 = AtomicSharedFilePublisher.ComputeSha1(source);
        CreateFile(
            Path.Combine("installer", ".minecraft", "versions", "Test", "Test.json"),
            "{\"logging\":{\"client\":{\"file\":{\"id\":\"client.xml\",\"sha1\":\""
            + sourceSha1
            + "\",\"size\":"
            + new FileInfo(source).Length
            + "}}}}");
        var destination = Path.Combine(TempRoot, "published");
        var destinationConfig = CreateFile(
            Path.Combine("published", "assets", "log_configs", "client.xml"),
            "stale-log");

        await new LoaderInstallerPrerequisiteSeeder().PublishDeltaAsync(
            EmptySnapshot(workspace),
            destination,
            CancellationToken.None);

        Assert.Equal("current-log", await File.ReadAllTextAsync(destinationConfig));
    }

    [Fact]
    public async Task LoaderDeltaReplacesVerifiedVirtualAndLegacyResources()
    {
        var workspace = Path.Combine(TempRoot, "installer", ".minecraft");
        const string assetContent = "current";
        var assetSha1 = ComputeSha1(assetContent);
        var indexContent = $"{{\"virtual\":true,\"map_to_resources\":true,\"objects\":{{\"minecraft/lang/en_us.json\":{{\"hash\":\"{assetSha1}\",\"size\":{assetContent.Length}}}}}}}";
        var index = CreateFile(
            Path.Combine("installer", ".minecraft", "assets", "indexes", "5.json"),
            indexContent);
        CreateFile(
            Path.Combine("installer", ".minecraft", "assets", "virtual", "5", "minecraft", "lang", "en_us.json"),
            assetContent);
        CreateFile(
            Path.Combine("installer", ".minecraft", "resources", "minecraft", "lang", "en_us.json"),
            assetContent);
        CreateFile(
            Path.Combine("installer", ".minecraft", "versions", "Test", "Test.json"),
            $"{{\"assetIndex\":{{\"id\":\"5\",\"sha1\":\"{AtomicSharedFilePublisher.ComputeSha1(index)}\",\"size\":{new FileInfo(index).Length}}}}}");
        var destination = Path.Combine(TempRoot, "published");
        var virtualDestination = CreateFile(
            Path.Combine("published", "assets", "virtual", "5", "minecraft", "lang", "en_us.json"),
            "stale!!");
        var resourceDestination = CreateFile(
            Path.Combine("published", "resources", "minecraft", "lang", "en_us.json"),
            "stale!!");

        await new LoaderInstallerPrerequisiteSeeder().PublishDeltaAsync(
            EmptySnapshot(workspace),
            destination,
            CancellationToken.None);

        Assert.Equal(assetContent, await File.ReadAllTextAsync(virtualDestination));
        Assert.Equal(assetContent, await File.ReadAllTextAsync(resourceDestination));
    }

    [Fact]
    public async Task LoaderDeltaDoesNotTrustDerivedResourcesFromTamperedAssetIndex()
    {
        var workspace = Path.Combine(TempRoot, "installer", ".minecraft");
        const string assetContent = "current";
        var assetSha1 = ComputeSha1(assetContent);
        var tamperedIndex = CreateFile(
            Path.Combine("installer", ".minecraft", "assets", "indexes", "5.json"),
            $"{{\"map_to_resources\":true,\"objects\":{{\"minecraft/lang/en_us.json\":{{\"hash\":\"{assetSha1}\",\"size\":{assetContent.Length}}}}}}}");
        CreateFile(
            Path.Combine("installer", ".minecraft", "resources", "minecraft", "lang", "en_us.json"),
            assetContent);
        CreateFile(
            Path.Combine("installer", ".minecraft", "versions", "Test", "Test.json"),
            $"{{\"assetIndex\":{{\"id\":\"5\",\"sha1\":\"{ComputeSha1("trusted-index")}\",\"size\":13}}}}");
        var destination = Path.Combine(TempRoot, "published");
        var resourceDestination = CreateFile(
            Path.Combine("published", "resources", "minecraft", "lang", "en_us.json"),
            "stale!!");
        var snapshot = new LoaderInstallerWorkspaceSnapshot(
            workspace,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["assets/indexes/5.json"] = AtomicSharedFilePublisher.ComputeSha1(tamperedIndex)
            });

        await Assert.ThrowsAsync<IOException>(() =>
            new LoaderInstallerPrerequisiteSeeder().PublishDeltaAsync(
                snapshot,
                destination,
                CancellationToken.None));

        Assert.Equal("stale!!", await File.ReadAllTextAsync(resourceDestination));
    }

    [Fact]
    public async Task LoaderDeltaDoesNotTrustDerivedResourcesFromAmbiguousAssetIndexMetadata()
    {
        var workspace = Path.Combine(TempRoot, "installer", ".minecraft");
        const string assetContent = "current";
        var assetSha1 = ComputeSha1(assetContent);
        var index = CreateFile(
            Path.Combine("installer", ".minecraft", "assets", "indexes", "5.json"),
            $"{{\"map_to_resources\":true,\"objects\":{{\"minecraft/lang/en_us.json\":{{\"hash\":\"{assetSha1}\",\"size\":{assetContent.Length}}}}}}}");
        var indexSha1 = AtomicSharedFilePublisher.ComputeSha1(index);
        CreateFile(
            Path.Combine("installer", ".minecraft", "resources", "minecraft", "lang", "en_us.json"),
            assetContent);
        CreateFile(
            Path.Combine("installer", ".minecraft", "versions", "First", "First.json"),
            $"{{\"assetIndex\":{{\"id\":\"5\",\"sha1\":\"{indexSha1}\",\"size\":{new FileInfo(index).Length}}}}}");
        CreateFile(
            Path.Combine("installer", ".minecraft", "versions", "Second", "Second.json"),
            $"{{\"assetIndex\":{{\"id\":\"5\",\"sha1\":\"{ComputeSha1("other-index")}\",\"size\":11}}}}");
        var destination = Path.Combine(TempRoot, "published");
        var resourceDestination = CreateFile(
            Path.Combine("published", "resources", "minecraft", "lang", "en_us.json"),
            "stale!!");
        var snapshot = new LoaderInstallerWorkspaceSnapshot(
            workspace,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["assets/indexes/5.json"] = indexSha1
            });

        await Assert.ThrowsAsync<IOException>(() =>
            new LoaderInstallerPrerequisiteSeeder().PublishDeltaAsync(
                snapshot,
                destination,
                CancellationToken.None));

        Assert.Equal("stale!!", await File.ReadAllTextAsync(resourceDestination));
    }

    [Fact]
    public async Task LoaderDeltaKeepsLibraryConflictsStrict()
    {
        var workspace = Path.Combine(TempRoot, "installer", ".minecraft");
        CreateFile(Path.Combine("installer", ".minecraft", "libraries", "example", "library.jar"), "new-library");
        var destination = Path.Combine(TempRoot, "published");
        var destinationLibrary = CreateFile(
            Path.Combine("published", "libraries", "example", "library.jar"),
            "old-library");

        await Assert.ThrowsAsync<IOException>(() =>
            new LoaderInstallerPrerequisiteSeeder().PublishDeltaAsync(
                EmptySnapshot(workspace),
                destination,
                CancellationToken.None));

        Assert.Equal("old-library", await File.ReadAllTextAsync(destinationLibrary));
    }

    [Fact]
    public async Task LoaderDeltaDoesNotPublishRuntimeDirectory()
    {
        var workspace = Path.Combine(TempRoot, "installer", ".minecraft");
        CreateFile(Path.Combine("installer", ".minecraft", "runtime", "java", "bin", "java.exe"), "runtime");
        var destination = Path.Combine(TempRoot, "published");

        await new LoaderInstallerPrerequisiteSeeder().PublishDeltaAsync(
            EmptySnapshot(workspace),
            destination,
            CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(destination, "runtime", "java", "bin", "java.exe")));
    }

    [Fact]
    public async Task LoaderDeltaRejectsAssetObjectConflict()
    {
        var workspace = Path.Combine(TempRoot, "installer", ".minecraft");
        var relativePath = Path.Combine("assets", "objects", "aa", new string('a', 40));
        CreateFile(Path.Combine("installer", ".minecraft", relativePath), "new-object");
        var destination = Path.Combine(TempRoot, "published");
        var destinationObject = CreateFile(Path.Combine("published", relativePath), "old-object");

        await Assert.ThrowsAsync<IOException>(() =>
            new LoaderInstallerPrerequisiteSeeder().PublishDeltaAsync(
                EmptySnapshot(workspace),
                destination,
                CancellationToken.None));

        Assert.Equal("old-object", await File.ReadAllTextAsync(destinationObject));
    }

    [Fact]
    public async Task LoaderDeltaRejectsAmbiguousLoggingMetadata()
    {
        var workspace = Path.Combine(TempRoot, "installer", ".minecraft");
        CreateFile(
            Path.Combine("installer", ".minecraft", "assets", "log_configs", "client.xml"),
            "current-log");
        CreateFile(
            Path.Combine("installer", ".minecraft", "versions", "First", "First.json"),
            "{\"logging\":{\"client\":{\"file\":{\"id\":\"client.xml\",\"sha1\":\""
            + ComputeSha1("current-log")
            + "\",\"size\":11}}}}");
        CreateFile(
            Path.Combine("installer", ".minecraft", "versions", "Second", "Second.json"),
            "{\"logging\":{\"client\":{\"file\":{\"id\":\"client.xml\",\"sha1\":\""
            + ComputeSha1("other-value")
            + "\",\"size\":11}}}}");
        var destination = Path.Combine(TempRoot, "published");
        var destinationConfig = CreateFile(
            Path.Combine("published", "assets", "log_configs", "client.xml"),
            "stale-log");

        await Assert.ThrowsAsync<IOException>(() =>
            new LoaderInstallerPrerequisiteSeeder().PublishDeltaAsync(
                EmptySnapshot(workspace),
                destination,
                CancellationToken.None));

        Assert.Equal("stale-log", await File.ReadAllTextAsync(destinationConfig));
    }

    [Fact]
    public async Task LoaderDeltaPublicationRejectsJunctionWithoutReadingExternalFile()
    {
        var workspace = Path.Combine(TempRoot, "installer", ".minecraft");
        var libraries = Path.Combine(workspace, "libraries");
        var external = Path.Combine(TempRoot, "external");
        Directory.CreateDirectory(libraries);
        Directory.CreateDirectory(external);
        File.WriteAllText(Path.Combine(external, "outside.jar"), "outside");
        var junction = Path.Combine(libraries, "linked");
        CreateDirectoryJunction(junction, external);
        var snapshot = new LoaderInstallerWorkspaceSnapshot(
            workspace,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        var destination = Path.Combine(TempRoot, "published");

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                new LoaderInstallerPrerequisiteSeeder().PublishDeltaAsync(
                    snapshot,
                    destination,
                    CancellationToken.None));

            Assert.False(File.Exists(Path.Combine(destination, "libraries", "linked", "outside.jar")));
        }
        finally
        {
            if (Directory.Exists(junction))
                Directory.Delete(junction, recursive: false);
        }
    }

    [Fact]
    public async Task ForgePrerequisitePathTraversalIsIgnored()
    {
        var shared = Path.Combine(TempRoot, "shared");
        var workspace = Path.Combine(TempRoot, "installer", ".minecraft");
        var escapedSource = CreateFile(Path.Combine("shared", "escaped.jar"), "escaped");
        var installerJar = Path.Combine(TempRoot, "installer.jar");
        CreateInstallerArchive(installerJar, "../escaped.jar", AtomicSharedFilePublisher.ComputeSha1(escapedSource));

        await new LoaderInstallerPrerequisiteSeeder().SeedAsync(
            shared,
            workspace,
            "1.20.1",
            installerJar,
            CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(workspace, "escaped.jar")));
        Assert.False(File.Exists(Path.Combine(TempRoot, "installer", "escaped.jar")));
    }

    private string CreateFile(string relativePath, string content)
    {
        var path = Path.Combine(TempRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static LoaderInstallerWorkspaceSnapshot EmptySnapshot(string workspace)
    {
        return new LoaderInstallerWorkspaceSnapshot(
            workspace,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static void CreateDirectoryJunction(string linkPath, string targetPath)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "/c", "mklink", "/J", linkPath, targetPath }
        }) ?? throw new InvalidOperationException("Failed to start junction creation process.");
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        Assert.True((File.GetAttributes(linkPath) & FileAttributes.ReparsePoint) != 0);
    }

    private static void CreateInstallerArchive(string path, string libraryPath, string sha1)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("install_profile.json");
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write($$"""
        {
          "libraries": [
            {
              "name": "com.example:needed:1.0",
              "downloads": {
                "artifact": {
                  "path": "{{libraryPath}}",
                  "sha1": "{{sha1}}"
                }
              }
            }
          ]
        }
        """);
    }

    private static string ComputeSha1(string content)
    {
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(content)));
    }

    private static object GetPrivateField(object instance, string fieldName) =>
        instance.GetType()
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(instance)
        ?? throw new InvalidOperationException($"Expected private field '{fieldName}'.");

    private static void RaisePrivateEvent<T>(object instance, string fieldName, T args)
    {
        var handler = Assert.IsType<EventHandler<T>>(GetPrivateField(instance, fieldName));
        handler.Invoke(instance, args);
    }

    private sealed class InlineProgress(List<LauncherProgress> reports) : IProgress<LauncherProgress>
    {
        public void Report(LauncherProgress value) => reports.Add(value);
    }
}
