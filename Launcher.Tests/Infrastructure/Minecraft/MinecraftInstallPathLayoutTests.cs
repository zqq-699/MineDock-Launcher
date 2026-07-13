/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO.Compression;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using CmlLib.Core.Files;
using CmlLib.Core.FileExtractors;
using CmlLib.Core.Rules;
using CmlLib.Core.Version;
using Launcher.Infrastructure.Minecraft;

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
    public async Task ForgePrerequisitesSeedOnlyReferencedLibrariesAndPublishOnlyDelta()
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

        Assert.False(File.Exists(Path.Combine(destination, "libraries", referencedRelativePath)));
        Assert.True(File.Exists(Path.Combine(destination, "libraries", newRelativePath)));
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
}
