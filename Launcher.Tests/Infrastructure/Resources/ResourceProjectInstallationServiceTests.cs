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
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.Infrastructure.Resources;

public sealed class ResourceProjectInstallationServiceTests
{
    [Fact]
    public async Task ModpackWorkspaceIsRemovedAfterSuccessfulImport()
    {
        var catalog = new RecordingCatalogService();
        var importedInstance = new GameInstance { Id = "imported", Name = "Pack" };
        var service = new ResourceProjectInstallationService(
            catalog,
            new StubModpackImportService(ModpackImportResult.Success(importedInstance)),
            NullLogger<ResourceProjectInstallationService>.Instance);

        var result = await service.ExecuteAsync(new ResourceProjectInstallationRequest(
            CreateVersion(ResourceProjectKind.Modpack),
            ResourceProjectInstallationTargetKind.NewModpackInstance));

        Assert.Same(importedInstance, result.ModpackImportResult?.ImportedInstance);
        Assert.NotNull(catalog.LastTargetDirectory);
        Assert.False(Directory.Exists(catalog.LastTargetDirectory));
    }

    [Fact]
    public async Task ModpackWorkspaceIsRemovedWhenImportThrows()
    {
        var catalog = new RecordingCatalogService();
        var service = new ResourceProjectInstallationService(
            catalog,
            new StubModpackImportService(exception: new InvalidDataException("invalid")),
            NullLogger<ResourceProjectInstallationService>.Instance);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.ExecuteAsync(
            new ResourceProjectInstallationRequest(
                CreateVersion(ResourceProjectKind.Modpack),
                ResourceProjectInstallationTargetKind.NewModpackInstance)));

        Assert.NotNull(catalog.LastTargetDirectory);
        Assert.False(Directory.Exists(catalog.LastTargetDirectory));
    }

    [Fact]
    public async Task PreparationRoutesExistingInstanceCheckToCatalog()
    {
        var catalog = new RecordingCatalogService { InstallExists = true };
        var service = new ResourceProjectInstallationService(
            catalog,
            new StubModpackImportService(ModpackImportResult.Failure(ModpackImportFailureReason.UnexpectedError)),
            NullLogger<ResourceProjectInstallationService>.Instance);
        var instance = new GameInstance { Id = "target", InstanceDirectory = "instance" };

        var result = await service.PrepareAsync(new ResourceProjectInstallationRequest(
            CreateVersion(ResourceProjectKind.Mod),
            ResourceProjectInstallationTargetKind.ExistingInstance,
            Instance: instance));

        Assert.True(result.TargetExists);
        Assert.Same(instance, catalog.LastInstallExistsInstance);
    }

    private static ResourceProjectVersion CreateVersion(ResourceProjectKind kind)
    {
        return new ResourceProjectVersion
        {
            Kind = kind,
            VersionId = "version",
            FileName = kind is ResourceProjectKind.Modpack ? "pack.mrpack" : "mod.jar"
        };
    }

    private sealed class RecordingCatalogService : IResourceCatalogService
    {
        public string? LastTargetDirectory { get; private set; }
        public GameInstance? LastInstallExistsInstance { get; private set; }
        public bool InstallExists { get; init; }

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
            File.WriteAllText(archive, "archive");
            return Task.FromResult(archive);
        }

        public Task<bool> ProjectVersionDownloadExistsAsync(ResourceProjectVersion version, string targetDirectory, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> ProjectVersionInstallExistsAsync(ResourceProjectVersion version, GameInstance instance, CancellationToken cancellationToken = default)
        {
            LastInstallExistsInstance = instance;
            return Task.FromResult(InstallExists);
        }
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
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            return exception is null
                ? Task.FromResult(result!)
                : Task.FromException<ModpackImportResult>(exception);
        }
    }
}
