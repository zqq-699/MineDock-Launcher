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

using System.IO;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.CurseForge;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Modpacks;

public sealed class ModpackExportService : IModpackExportService
{
    private readonly CurseForgeModpackExporter curseForgeExporter;
    private readonly ModrinthModpackExporter modrinthExporter;

    public ModpackExportService(
        IModService modService,
        ILocalResourcePackService resourcePackService,
        ILocalShaderPackService shaderPackService,
        ICurseForgeApiKeyResolver curseForgeApiKeyResolver,
        CurseForgeApiClient? curseForgeApiClient = null,
        ModrinthApiClient? modrinthApiClient = null,
        ILogger<ModpackExportService>? logger = null)
    {
        var effectiveLogger = logger ?? NullLogger<ModpackExportService>.Instance;
        var candidateCollector = new ModpackExportCandidateCollector(
            modService,
            resourcePackService,
            shaderPackService,
            effectiveLogger);
        var archiveWriter = new ModpackExportArchiveWriter();

        curseForgeExporter = new CurseForgeModpackExporter(
            candidateCollector,
            archiveWriter,
            curseForgeApiKeyResolver,
            curseForgeApiClient ?? new CurseForgeApiClient(),
            effectiveLogger);
        modrinthExporter = new ModrinthModpackExporter(
            candidateCollector,
            archiveWriter,
            modrinthApiClient ?? new ModrinthApiClient(),
            effectiveLogger);
    }

    public Task<ModpackExportResult> ExportAsync(
        ModpackExportRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationFailure = ValidateRequest(request);
        if (validationFailure is not null)
            return Task.FromResult(validationFailure);

        return request.Kind switch
        {
            ModpackExportKind.CurseForge => curseForgeExporter.ExportAsync(request, cancellationToken),
            ModpackExportKind.Modrinth => modrinthExporter.ExportAsync(request, cancellationToken),
            _ => Task.FromResult(Failure(ModpackExportFailureReason.UnsupportedType))
        };
    }

    private static ModpackExportResult? ValidateRequest(ModpackExportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.Version)
            || string.IsNullOrWhiteSpace(request.OutputArchivePath)
            || string.IsNullOrWhiteSpace(request.Instance.MinecraftVersion)
            || string.IsNullOrWhiteSpace(request.Instance.InstanceDirectory)
            || !Directory.Exists(request.Instance.InstanceDirectory))
        {
            return Failure(ModpackExportFailureReason.InvalidRequest);
        }

        if (request.Instance.Loader is not LoaderKind.Vanilla
            && string.IsNullOrWhiteSpace(request.Instance.LoaderVersion))
        {
            return Failure(ModpackExportFailureReason.MissingLoaderVersion);
        }

        return null;
    }

    private static ModpackExportResult Failure(ModpackExportFailureReason reason)
    {
        return new ModpackExportResult(false, reason);
    }
}
