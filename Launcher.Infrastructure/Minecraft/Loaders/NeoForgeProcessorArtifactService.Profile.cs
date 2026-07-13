/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Infrastructure.Minecraft;

internal sealed partial class NeoForgeProcessorArtifactService
{
    internal static async Task<IReadOnlyList<NeoForgeProcessorExpectedArtifact>> ReadExpectedArtifactsAsync(
        string installerJarPath,
        CancellationToken cancellationToken)
    {
        var artifacts = await LoaderProcessorArtifactProfileReader.ReadAsync(
            installerJarPath,
            "NeoForge",
            cancellationToken,
            IsNeoForgeRuntimeLibrary).ConfigureAwait(false);
        return artifacts
            .Select(artifact => new NeoForgeProcessorExpectedArtifact(artifact.RelativePath, artifact.TrustedSha1))
            .ToList();
    }

    private static bool IsNeoForgeRuntimeLibrary(string coordinate)
    {
        var coordinateWithoutExtension = coordinate.Split('@', 2)[0];
        var parts = coordinateWithoutExtension.Split(
            ':',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 4
               && parts[0].Equals("net.neoforged", StringComparison.OrdinalIgnoreCase)
               && parts[1].Equals("neoforge", StringComparison.OrdinalIgnoreCase)
               && parts[3].Equals("universal", StringComparison.OrdinalIgnoreCase);
    }
}
