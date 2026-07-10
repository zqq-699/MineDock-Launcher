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
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Modpacks;

internal sealed class ModpackExportCandidateCollector
{
    private readonly IModService modService;
    private readonly ILocalResourcePackService resourcePackService;
    private readonly ILocalShaderPackService shaderPackService;
    private readonly ILogger logger;

    public ModpackExportCandidateCollector(
        IModService modService,
        ILocalResourcePackService resourcePackService,
        ILocalShaderPackService shaderPackService,
        ILogger logger)
    {
        this.modService = modService;
        this.resourcePackService = resourcePackService;
        this.shaderPackService = shaderPackService;
        this.logger = logger;
    }

    public async Task<IReadOnlyList<ModpackExportFileCandidate>> CollectAsync(
        ModpackExportRequest request,
        CancellationToken cancellationToken)
    {
        var candidates = new List<ModpackExportFileCandidate>();
        if (request.IncludeMods)
        {
            var mods = await modService.GetModsAsync(request.Instance, cancellationToken).ConfigureAwait(false);
            foreach (var mod in mods.Where(mod => mod.IsEnabled))
                candidates.Add(await CreateCandidateAsync(mod.FullPath, "mods", cancellationToken).ConfigureAwait(false));

            if (request.IncludeDisabledMods)
            {
                foreach (var mod in mods.Where(mod => !mod.IsEnabled))
                    candidates.Add(CreateOverrideOnlyCandidate(mod.FullPath, "mods"));
            }
        }

        if (request.IncludeResourcePacks)
        {
            var resourcePacks = await resourcePackService.GetResourcePacksAsync(request.Instance, cancellationToken)
                .ConfigureAwait(false);
            foreach (var resourcePack in resourcePacks)
            {
                candidates.Add(await CreateCandidateAsync(
                        resourcePack.FullPath,
                        "resourcepacks",
                        cancellationToken)
                    .ConfigureAwait(false));
            }
        }

        if (request.IncludeShaderPacks)
        {
            var shaderPacks = await shaderPackService.GetShaderPacksAsync(request.Instance, cancellationToken)
                .ConfigureAwait(false);
            foreach (var shaderPack in shaderPacks)
            {
                candidates.Add(await CreateCandidateAsync(
                        shaderPack.FullPath,
                        "shaderpacks",
                        cancellationToken)
                    .ConfigureAwait(false));
            }
        }

        return candidates
            .Where(candidate => File.Exists(candidate.SourcePath))
            .ToArray();
    }

    public static IReadOnlyList<ModpackExportArchiveFile> CollectConfigFiles(string instanceDirectory)
    {
        var configDirectory = Path.Combine(instanceDirectory, "config");
        if (!Directory.Exists(configDirectory))
            return [];

        return Directory.EnumerateFiles(configDirectory, "*", SearchOption.AllDirectories)
            .Select(filePath => CreateConfigFile(configDirectory, filePath))
            .Where(file => file is not null)
            .Cast<ModpackExportArchiveFile>()
            .ToArray();
    }

    private async Task<ModpackExportFileCandidate> CreateCandidateAsync(
        string sourcePath,
        string targetDirectory,
        CancellationToken cancellationToken)
    {
        var normalizedPath = Path.GetFullPath(sourcePath);
        var fileName = Path.GetFileName(normalizedPath);
        long? fingerprint = null;
        try
        {
            fingerprint = await CurseForgeFingerprintUtility.ComputeFileFingerprintAsync(normalizedPath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            logger.LogWarning(
                exception,
                "Failed to fingerprint export file; it will be written to overrides. FilePath={FilePath}",
                normalizedPath);
        }

        return new ModpackExportFileCandidate(
            normalizedPath,
            $"{targetDirectory}/{fileName}",
            fingerprint,
            IsOverrideOnly: false);
    }

    private static ModpackExportFileCandidate CreateOverrideOnlyCandidate(
        string sourcePath,
        string targetDirectory)
    {
        var normalizedPath = Path.GetFullPath(sourcePath);
        return new ModpackExportFileCandidate(
            normalizedPath,
            $"{targetDirectory}/{Path.GetFileName(normalizedPath)}",
            Fingerprint: null,
            IsOverrideOnly: true);
    }

    private static ModpackExportArchiveFile? CreateConfigFile(string configDirectory, string filePath)
    {
        var relativePath = Path.GetRelativePath(configDirectory, filePath)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.StartsWith("../", StringComparison.Ordinal))
            return null;

        return new ModpackExportArchiveFile(Path.GetFullPath(filePath), $"config/{relativePath}");
    }
}

internal sealed record ModpackExportFileCandidate(
    string SourcePath,
    string OverridePath,
    long? Fingerprint,
    bool IsOverrideOnly);

internal sealed record ModpackExportArchiveFile(string SourcePath, string RelativePath);
