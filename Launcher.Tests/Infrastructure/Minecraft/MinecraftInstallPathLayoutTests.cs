/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO.Compression;
using System.Diagnostics;
using System.Net;
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

}
