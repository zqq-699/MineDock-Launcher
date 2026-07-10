/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO.Compression;
using Launcher.Application.Services;
using Launcher.Infrastructure.CurseForge;
using Launcher.Infrastructure.Modpacks;

namespace Launcher.Tests.Infrastructure.Modpacks;

public sealed class ModpackExportServiceTests : TestTempDirectory
{
    [Theory]
    [InlineData(ModpackExportKind.CurseForge, "manifest.json")]
    [InlineData(ModpackExportKind.Modrinth, "modrinth.index.json")]
    public async Task ExportCreatesArchiveForSupportedFormat(ModpackExportKind kind, string manifestName)
    {
        var output = Path.Combine(TempRoot, $"{kind}.zip");
        var result = await CreateService().ExportAsync(CreateRequest(CreateInstance(), output, kind));

        Assert.True(result.IsSuccess);
        using var archive = ZipFile.OpenRead(output);
        Assert.NotNull(archive.GetEntry(manifestName));
    }

    [Fact]
    public async Task ValidationFailureDoesNotCreatePartialArchive()
    {
        var output = Path.Combine(TempRoot, "invalid.zip");

        var result = await CreateService().ExportAsync(
            CreateRequest(CreateInstance(loaderVersion: null), output, ModpackExportKind.CurseForge));

        Assert.False(result.IsSuccess);
        Assert.Equal(ModpackExportFailureReason.MissingLoaderVersion, result.FailureReason);
        Assert.False(File.Exists(output));
    }

    private static ModpackExportService CreateService() => new(
        new EmptyModService(),
        new EmptyResourcePackService(),
        new EmptyShaderPackService(),
        new EmptyKeyResolver());

    private GameInstance CreateInstance(string? loaderVersion = "0.16.10")
    {
        var directory = Path.Combine(TempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new GameInstance
        {
            Id = "instance",
            Name = "Export Test",
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Fabric,
            LoaderVersion = loaderVersion,
            InstanceDirectory = directory
        };
    }

    private static ModpackExportRequest CreateRequest(GameInstance instance, string output, ModpackExportKind kind) =>
        new(instance, kind, "Pack", "Author", "1.0.0", output,
            IncludeMods: false, IncludeDisabledMods: false, IncludeResourcePacks: false,
            IncludeShaderPacks: false, IncludeConfig: false);

    private sealed class EmptyKeyResolver : ICurseForgeApiKeyResolver
    {
        public Task<string?> TryResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class EmptyModService : IModService
    {
        public Task<IReadOnlyList<LocalMod>> GetModsAsync(GameInstance instance, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LocalMod>>([]);
        public Task<LocalMod> ImportAsync(GameInstance instance, string path, bool overwriteExisting = false,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetEnabledAsync(LocalMod mod, bool enabled, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(LocalMod mod, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class EmptyResourcePackService : ILocalResourcePackService
    {
        public Task<IReadOnlyList<LocalResourcePack>> GetResourcePacksAsync(GameInstance instance,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LocalResourcePack>>([]);
        public Task<LocalResourcePackImportResult> ImportAsync(GameInstance instance, string path,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(LocalResourcePack pack, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(IEnumerable<LocalResourcePack> packs, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class EmptyShaderPackService : ILocalShaderPackService
    {
        public Task<IReadOnlyList<LocalShaderPack>> GetShaderPacksAsync(GameInstance instance,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LocalShaderPack>>([]);
        public Task<LocalShaderPackImportResult> ImportAsync(GameInstance instance, string path,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(LocalShaderPack pack, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(IEnumerable<LocalShaderPack> packs, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
