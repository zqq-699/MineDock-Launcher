/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Resources;
using Launcher.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace Launcher.Tests.Infrastructure.Resources;

public sealed class ResourceProjectInstallationServiceTests
{
    [Fact]
    public async Task ModpackWorkspaceIsRemovedAfterSuccessfulImport()
    {
        var catalog = new RecordingCatalogService();
        var importedInstance = new GameInstance { Id = "imported", Name = "Pack" };
        var instanceService = new FakeGameInstanceService();
        var service = new ResourceProjectInstallationService(
            catalog,
            new StubModpackImportService(ModpackImportResult.Success(importedInstance)),
            instanceService,
            NullLogger<ResourceProjectInstallationService>.Instance);

        var result = await service.ExecuteAsync(new ResourceProjectInstallationRequest(
            CreateVersion(ResourceProjectKind.Modpack),
            ResourceProjectInstallationTargetKind.NewModpackInstance));

        Assert.Same(importedInstance, result.ModpackImportResult?.ImportedInstance);
        Assert.Equal(0, instanceService.SaveCallCount);
        Assert.NotNull(catalog.LastTargetDirectory);
        Assert.False(Directory.Exists(catalog.LastTargetDirectory));
    }

    [Fact]
    public async Task ModpackProjectIconIsCopiedIntoInstanceAndPersisted()
    {
        var root = Path.Combine(Path.GetTempPath(), $"launcher-resource-icon-{Guid.NewGuid():N}");
        var sourcePath = Path.Combine(root, "cached.png");
        var instanceDirectory = Path.Combine(root, "instance");
        Directory.CreateDirectory(instanceDirectory);
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);
        var project = CreateProject("project");
        var catalog = new RecordingCatalogService();
        catalog.ThumbnailSources[project.ProjectId] = $"{new Uri(sourcePath).AbsoluteUri}?v=1";
        var importedInstance = new GameInstance
        {
            Id = "imported",
            Name = "Pack",
            InstanceDirectory = instanceDirectory
        };
        var instanceService = new FakeGameInstanceService();
        var service = new ResourceProjectInstallationService(
            catalog,
            new StubModpackImportService(ModpackImportResult.Success(importedInstance)),
            instanceService,
            NullLogger<ResourceProjectInstallationService>.Instance);

        try
        {
            var result = await service.ExecuteAsync(new ResourceProjectInstallationRequest(
                CreateVersion(ResourceProjectKind.Modpack),
                ResourceProjectInstallationTargetKind.NewModpackInstance,
                Project: project));

            var destinationPath = Path.Combine(instanceDirectory, "BHL", "resource-project-icon.png");
            Assert.True(result.ModpackImportResult?.IsSuccess);
            Assert.Equal([1, 2, 3, 4], await File.ReadAllBytesAsync(destinationPath));
            Assert.Equal(new Uri(destinationPath).AbsoluteUri, importedInstance.IconSource);
            Assert.Same(importedInstance, instanceService.LastSavedInstance);
            File.Delete(sourcePath);
            Assert.True(File.Exists(destinationPath));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ModpackIconPersistenceFailureKeepsImportSuccessfulAndRestoresFallback()
    {
        var root = Path.Combine(Path.GetTempPath(), $"launcher-resource-icon-{Guid.NewGuid():N}");
        var sourcePath = Path.Combine(root, "cached.png");
        var instanceDirectory = Path.Combine(root, "instance");
        Directory.CreateDirectory(instanceDirectory);
        await File.WriteAllBytesAsync(sourcePath, [5, 6, 7]);
        var project = CreateProject("project");
        var catalog = new RecordingCatalogService();
        catalog.ThumbnailSources[project.ProjectId] = new Uri(sourcePath).AbsoluteUri;
        var importedInstance = new GameInstance
        {
            Id = "imported",
            Name = "Pack",
            InstanceDirectory = instanceDirectory
        };
        var instanceService = new FakeGameInstanceService
        {
            SaveException = new IOException("save failed")
        };
        var service = new ResourceProjectInstallationService(
            catalog,
            new StubModpackImportService(ModpackImportResult.Success(importedInstance)),
            instanceService,
            NullLogger<ResourceProjectInstallationService>.Instance);

        try
        {
            var result = await service.ExecuteAsync(new ResourceProjectInstallationRequest(
                CreateVersion(ResourceProjectKind.Modpack),
                ResourceProjectInstallationTargetKind.NewModpackInstance,
                Project: project));

            Assert.True(result.ModpackImportResult?.IsSuccess);
            Assert.Null(importedInstance.IconSource);
            Assert.False(File.Exists(Path.Combine(instanceDirectory, "BHL", "resource-project-icon.png")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MissingModpackThumbnailKeepsImportSuccessfulWithDefaultIcon()
    {
        var root = Path.Combine(Path.GetTempPath(), $"launcher-resource-icon-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var importedInstance = new GameInstance
        {
            Id = "imported",
            Name = "Pack",
            InstanceDirectory = root
        };
        var instanceService = new FakeGameInstanceService();
        var service = new ResourceProjectInstallationService(
            new RecordingCatalogService(),
            new StubModpackImportService(ModpackImportResult.Success(importedInstance)),
            instanceService,
            NullLogger<ResourceProjectInstallationService>.Instance);

        try
        {
            var result = await service.ExecuteAsync(new ResourceProjectInstallationRequest(
                CreateVersion(ResourceProjectKind.Modpack),
                ResourceProjectInstallationTargetKind.NewModpackInstance,
                Project: CreateProject("missing")));

            Assert.True(result.ModpackImportResult?.IsSuccess);
            Assert.Null(importedInstance.IconSource);
            Assert.Equal(0, instanceService.SaveCallCount);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ConcurrentModpackInstallsKeepProjectIconsIsolated()
    {
        var root = Path.Combine(Path.GetTempPath(), $"launcher-resource-icons-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var firstSource = Path.Combine(root, "first.png");
        var secondSource = Path.Combine(root, "second.png");
        await File.WriteAllBytesAsync(firstSource, [1]);
        await File.WriteAllBytesAsync(secondSource, [2]);
        var firstProject = CreateProject("first");
        var secondProject = CreateProject("second");
        var catalog = new RecordingCatalogService();
        catalog.ThumbnailSources[firstProject.ProjectId] = new Uri(firstSource).AbsoluteUri;
        catalog.ThumbnailSources[secondProject.ProjectId] = new Uri(secondSource).AbsoluteUri;
        var instanceService = new FakeGameInstanceService();
        var service = new ResourceProjectInstallationService(
            catalog,
            new ArchiveMappedModpackImportService(root),
            instanceService,
            NullLogger<ResourceProjectInstallationService>.Instance);

        try
        {
            var first = service.ExecuteAsync(new ResourceProjectInstallationRequest(
                CreateVersion(ResourceProjectKind.Modpack, "first"),
                ResourceProjectInstallationTargetKind.NewModpackInstance,
                Project: firstProject));
            var second = service.ExecuteAsync(new ResourceProjectInstallationRequest(
                CreateVersion(ResourceProjectKind.Modpack, "second"),
                ResourceProjectInstallationTargetKind.NewModpackInstance,
                Project: secondProject));
            var results = await Task.WhenAll(first, second);

            Assert.Equal([1], await ReadInstalledIconAsync(results[0]));
            Assert.Equal([2], await ReadInstalledIconAsync(results[1]));
            Assert.NotEqual(
                results[0].ModpackImportResult?.ImportedInstance?.IconSource,
                results[1].ModpackImportResult?.ImportedInstance?.IconSource);
            Assert.Equal(2, instanceService.SaveCallCount);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ModpackWorkspaceIsRemovedWhenImportThrows()
    {
        var catalog = new RecordingCatalogService();
        var service = new ResourceProjectInstallationService(
            catalog,
            new StubModpackImportService(exception: new InvalidDataException("invalid")),
            new FakeGameInstanceService(),
            NullLogger<ResourceProjectInstallationService>.Instance);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.ExecuteAsync(
            new ResourceProjectInstallationRequest(
                CreateVersion(ResourceProjectKind.Modpack),
                ResourceProjectInstallationTargetKind.NewModpackInstance)));

        Assert.NotNull(catalog.LastTargetDirectory);
        Assert.False(Directory.Exists(catalog.LastTargetDirectory));
    }

    [Fact]
    public async Task StartupCleanupDeletesOnlyOwnedInactiveResourceInstallWorkspaces()
    {
        var service = new ResourceProjectInstallationService(
            new RecordingCatalogService(),
            new StubModpackImportService(ModpackImportResult.Failure(ModpackImportFailureReason.UnexpectedError)),
            new FakeGameInstanceService(),
            NullLogger<ResourceProjectInstallationService>.Instance);
        var staleId = Guid.NewGuid().ToString("N");
        var activeId = Guid.NewGuid().ToString("N");
        var invalidId = Guid.NewGuid().ToString("N");
        var tempRoot = Path.GetTempPath();
        var stale = Path.Combine(tempRoot, $"launcher-modpack-install-{staleId}");
        var active = Path.Combine(tempRoot, $"launcher-modpack-install-{activeId}");
        var invalid = Path.Combine(tempRoot, $"launcher-modpack-install-{invalidId}");
        Directory.CreateDirectory(stale);
        Directory.CreateDirectory(active);
        Directory.CreateDirectory(invalid);
        await File.WriteAllTextAsync(
            Path.Combine(stale, ".launcher-resource-install.json"),
            JsonSerializer.Serialize(new { schemaVersion = 1, transactionId = staleId }));
        await File.WriteAllTextAsync(
            Path.Combine(active, ".launcher-resource-install.json"),
            JsonSerializer.Serialize(new { schemaVersion = 1, transactionId = activeId }));
        await File.WriteAllTextAsync(Path.Combine(invalid, ".launcher-resource-install.json"), "{}");
        await using var activeLock = new FileStream(
            Path.Combine(active, ".launcher-resource-install.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
        try
        {
            await service.CleanupStaleWorkspacesAsync();

            Assert.False(Directory.Exists(stale));
            Assert.True(Directory.Exists(active));
            Assert.True(Directory.Exists(invalid));
        }
        finally
        {
            await activeLock.DisposeAsync();
            if (Directory.Exists(active))
                Directory.Delete(active, recursive: true);
            if (Directory.Exists(invalid))
                Directory.Delete(invalid, recursive: true);
            if (Directory.Exists(stale))
                Directory.Delete(stale, recursive: true);
        }
    }

    [Fact]
    public async Task PreparationRoutesExistingInstanceCheckToCatalog()
    {
        var catalog = new RecordingCatalogService { InstallExists = true };
        var service = new ResourceProjectInstallationService(
            catalog,
            new StubModpackImportService(ModpackImportResult.Failure(ModpackImportFailureReason.UnexpectedError)),
            new FakeGameInstanceService(),
            NullLogger<ResourceProjectInstallationService>.Instance);
        var instance = new GameInstance { Id = "target", InstanceDirectory = "instance" };

        var result = await service.PrepareAsync(new ResourceProjectInstallationRequest(
            CreateVersion(ResourceProjectKind.Mod),
            ResourceProjectInstallationTargetKind.ExistingInstance,
            Instance: instance));

        Assert.True(result.TargetExists);
        Assert.Same(instance, catalog.LastInstallExistsInstance);
    }

    [Fact]
    public async Task DirectInstallForwardsObservableDownloadProgressAndReservesFinalization()
    {
        var catalog = new RecordingCatalogService();
        var service = new ResourceProjectInstallationService(
            catalog,
            new StubModpackImportService(ModpackImportResult.Failure(ModpackImportFailureReason.UnexpectedError)),
            new FakeGameInstanceService(),
            NullLogger<ResourceProjectInstallationService>.Instance);
        var reports = new List<LauncherProgress>();

        await service.ExecuteAsync(
            new ResourceProjectInstallationRequest(
                CreateVersion(ResourceProjectKind.Mod),
                ResourceProjectInstallationTargetKind.ExistingInstance,
                Instance: new GameInstance { Id = "target", InstanceDirectory = "instance" }),
            new InlineProgress(reports));

        Assert.Contains(reports, report => report.Stage is ModProgressStages.DownloadingFile && report.Percent == 50);
        Assert.Equal(InstallProgressStages.CompletingFiles, reports[^1].Stage);
        Assert.Equal(99, reports[^1].Percent);
    }

    private static ResourceProjectVersion CreateVersion(ResourceProjectKind kind, string versionId = "version")
    {
        return new ResourceProjectVersion
        {
            Kind = kind,
            VersionId = versionId,
            FileName = kind is ResourceProjectKind.Modpack ? "pack.mrpack" : "mod.jar"
        };
    }

    private static ResourceProject CreateProject(string projectId) => new()
    {
        Kind = ResourceProjectKind.Modpack,
        Source = ResourceProjectSource.Modrinth,
        ProjectId = projectId,
        Title = projectId,
        IconUrl = $"https://example.invalid/{projectId}.png"
    };

    private static async Task<byte[]> ReadInstalledIconAsync(ResourceProjectInstallationResult result)
    {
        var source = result.ModpackImportResult?.ImportedInstance?.IconSource;
        Assert.NotNull(source);
        return await File.ReadAllBytesAsync(new Uri(source).LocalPath);
    }

    private sealed class RecordingCatalogService :
        IResourceCatalogService,
        IResourceCatalogProgressReporter,
        IResourceThumbnailService
    {
        public string? LastTargetDirectory { get; private set; }
        public GameInstance? LastInstallExistsInstance { get; private set; }
        public bool InstallExists { get; init; }
        public Dictionary<string, string?> ThumbnailSources { get; } = [];

        public Task<ResourceCatalogSearchResult> SearchModsAsync(ResourceCatalogSearchRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ResourceCatalogSearchResult());

        public Task<ResourceProjectVersionsResult> GetProjectVersionsAsync(ResourceProjectVersionsRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ResourceProjectVersionsResult());

        public Task<string> InstallProjectVersionAsync(ResourceProjectVersion version, GameInstance instance, CancellationToken cancellationToken = default) =>
            Task.FromResult("installed");

        public Task<string> DownloadProjectVersionAsync(ResourceProjectVersion version, string targetDirectory, CancellationToken cancellationToken = default)
        {
            LastTargetDirectory = targetDirectory;
            Directory.CreateDirectory(targetDirectory);
            var archive = Path.Combine(targetDirectory, version.FileName);
            File.WriteAllText(archive, version.VersionId);
            return Task.FromResult(archive);
        }

        public Task<string> InstallProjectVersionWithProgressAsync(
            ResourceProjectVersion version,
            GameInstance instance,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken)
        {
            progress?.Report(new LauncherProgress(ModProgressStages.DownloadingFile, version.FileName, 50));
            return Task.FromResult("installed");
        }

        public Task<string> DownloadProjectVersionWithProgressAsync(
            ResourceProjectVersion version,
            string targetDirectory,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken)
        {
            progress?.Report(new LauncherProgress(ModProgressStages.DownloadingFile, version.FileName, 50));
            return DownloadProjectVersionAsync(version, targetDirectory, cancellationToken);
        }

        public Task<bool> ProjectVersionDownloadExistsAsync(ResourceProjectVersion version, string targetDirectory, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> ProjectVersionInstallExistsAsync(ResourceProjectVersion version, GameInstance instance, CancellationToken cancellationToken = default)
        {
            LastInstallExistsInstance = instance;
            return Task.FromResult(InstallExists);
        }

        public string? TryGetCachedThumbnailSource(ResourceProject project) =>
            ThumbnailSources.GetValueOrDefault(project.ProjectId);

        public Task<string?> GetOrCreateThumbnailSourceAsync(
            ResourceProject project,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(TryGetCachedThumbnailSource(project));
    }

    private sealed class InlineProgress(List<LauncherProgress> reports) : IProgress<LauncherProgress>
    {
        public void Report(LauncherProgress value) => reports.Add(value);
    }

    private sealed class StubModpackImportService : ILocalModpackImportService
    {
        private readonly ModpackImportResult? result;
        private readonly Exception? exception;

        public StubModpackImportService(ModpackImportResult? result = null, Exception? exception = null)
        {
            this.result = result;
            this.exception = exception;
        }

        public Task<ModpackRecognitionResult> RecognizeArchiveAsync(string archivePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(ModpackRecognitionResult.Failure(ModpackRecognitionFailureReason.UnsupportedArchive));

        public Task<ModpackImportResult> ImportFromArchiveAsync(
            string archivePath,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default,
            DownloadSourcePreference downloadSourcePreference = LauncherDefaults.DefaultDownloadSourcePreference,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            return exception is null
                ? Task.FromResult(result!)
                : Task.FromException<ModpackImportResult>(exception);
        }
    }

    private sealed class ArchiveMappedModpackImportService(string root) : ILocalModpackImportService
    {
        public Task<ModpackRecognitionResult> RecognizeArchiveAsync(
            string archivePath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ModpackRecognitionResult.Failure(ModpackRecognitionFailureReason.UnsupportedArchive));

        public async Task<ModpackImportResult> ImportFromArchiveAsync(
            string archivePath,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default,
            DownloadSourcePreference downloadSourcePreference = LauncherDefaults.DefaultDownloadSourcePreference,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            var id = await File.ReadAllTextAsync(archivePath, cancellationToken);
            var instanceDirectory = Path.Combine(root, $"instance-{id}");
            Directory.CreateDirectory(instanceDirectory);
            return ModpackImportResult.Success(new GameInstance
            {
                Id = id,
                Name = id,
                InstanceDirectory = instanceDirectory
            });
        }
    }
}
