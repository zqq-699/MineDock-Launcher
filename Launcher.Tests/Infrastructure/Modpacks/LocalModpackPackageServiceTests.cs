/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO.Compression;
using System.Net;
using System.Text;
using Launcher.Application.Services;
using Launcher.Infrastructure;
using Launcher.Infrastructure.CurseForge;
using Launcher.Infrastructure.Modpacks;

namespace Launcher.Tests.Infrastructure.Modpacks;

public sealed class LocalModpackPackageServiceTests : TestTempDirectory
{
    [Theory]
    [InlineData("modrinth.index.json", ModpackPackageKind.Modrinth, LoaderKind.Fabric)]
    [InlineData("manifest.json", ModpackPackageKind.CurseForge, LoaderKind.NeoForge)]
    public async Task PrepareParsesSupportedPackage(string manifestName, ModpackPackageKind kind, LoaderKind loader)
    {
        var path = Path.Combine(TempRoot, kind == ModpackPackageKind.Modrinth ? "pack.mrpack" : "pack.zip");
        CreateArchive(path, archive => AddEntry(archive, manifestName, kind == ModpackPackageKind.Modrinth
            ? """{"name":"Demo","dependencies":{"minecraft":"1.20.1","fabric-loader":"0.16.10"},"files":[]}"""
            : """{"name":"Demo","minecraft":{"version":"1.20.4","modLoaders":[{"id":"neoforge-20.4.237","primary":true}]},"files":[]}"""));

        var prepared = await CreateService().PrepareAsync(path);

        Assert.Equal(kind, prepared.PackageKind);
        Assert.Equal(loader, prepared.Loader);
        Assert.Equal("Demo", prepared.PackageName);
    }

    [Fact]
    public async Task PrepareRejectsPathTraversal()
    {
        var path = Path.Combine(TempRoot, "unsafe.mrpack");
        CreateArchive(path, archive =>
        {
            AddEntry(archive, "modrinth.index.json", """{"name":"Unsafe","dependencies":{"minecraft":"1.20.1"},"files":[]}""");
            AddEntry(archive, "overrides/../evil.txt", "bad");
        });

        var exception = await Assert.ThrowsAsync<ModpackImportException>(() => CreateService().PrepareAsync(path));

        Assert.Equal(ModpackImportFailureReason.InvalidManifest, exception.FailureReason);
        Assert.False(File.Exists(Path.Combine(TempRoot, "evil.txt")));
    }

    [Fact]
    public void ExtractionBudgetRejectsOversizedContent()
    {
        var exception = Assert.Throws<ModpackImportException>(() => new ZipExtractionBudget(1).Reserve(2));
        Assert.Equal(ModpackImportFailureReason.InvalidManifest, exception.FailureReason);
    }

    [Fact]
    public async Task InstallRejectsHashMismatchAndRemovesPartialFile()
    {
        var service = CreateService(new HttpClient(new FixedHandler("actual-bytes")));
        var instance = new GameInstance { Name = "Pack", InstanceDirectory = Path.Combine(TempRoot, "instance") };
        Directory.CreateDirectory(instance.InstanceDirectory);
        var prepared = new PreparedModpack
        {
            PackageKind = ModpackPackageKind.Modrinth,
            SourceArchivePath = Path.Combine(TempRoot, "pack.mrpack"),
            WorkingDirectory = Path.Combine(TempRoot, "work"),
            PackageName = "Pack",
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Vanilla,
            Files = [new PreparedModpackDownload
            {
                FileName = "mod.jar",
                RelativePath = "mods/mod.jar",
                SourceUrl = "https://download/mod.jar",
                Sha1 = new string('0', 40)
            }]
        };
        Directory.CreateDirectory(prepared.WorkingDirectory);

        var exception = await Assert.ThrowsAsync<ModpackImportException>(() =>
            service.InstallContentAsync(prepared, instance, null));

        Assert.Equal(ModpackImportFailureReason.HashMismatch, exception.FailureReason);
        Assert.Empty(Directory.EnumerateFiles(instance.InstanceDirectory, "*.download", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task InstallCopiesOverridesIntoInstance()
    {
        var path = Path.Combine(TempRoot, "overrides.mrpack");
        CreateArchive(path, archive =>
        {
            AddEntry(archive, "modrinth.index.json", """{"name":"Overrides","dependencies":{"minecraft":"1.20.1"},"files":[]}""");
            AddEntry(archive, "overrides/config/settings.txt", "demo");
        });
        var service = CreateService();
        var prepared = await service.PrepareAsync(path);
        var instance = new GameInstance { Name = "Overrides", InstanceDirectory = Path.Combine(TempRoot, "instance") };

        await service.InstallContentAsync(prepared, instance, null);

        Assert.Equal("demo", await File.ReadAllTextAsync(Path.Combine(instance.InstanceDirectory, "config", "settings.txt")));
    }

    private LocalModpackPackageService CreateService(HttpClient? client = null)
    {
        var paths = new LauncherPathProvider(TempRoot);
        return new LocalModpackPackageService(paths, httpClient: client,
            curseForgeApiKeyResolver: new CurseForgeApiKeyResolver(
                paths,
                embeddedApiKeyProvider: _ => Task.FromResult<string?>(null)));
    }

    private static void CreateArchive(string path, Action<ZipArchive> configure)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        configure(archive);
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private sealed class FixedHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) });
    }
}
