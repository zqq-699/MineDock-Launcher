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
            cancellationToken).ConfigureAwait(false);
        return artifacts
            .Select(artifact => new NeoForgeProcessorExpectedArtifact(artifact.RelativePath, artifact.TrustedSha1))
            .ToList();
    }
}
