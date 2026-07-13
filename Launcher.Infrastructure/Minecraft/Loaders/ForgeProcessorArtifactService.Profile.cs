/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Infrastructure.Minecraft;

internal sealed partial class ForgeProcessorArtifactService
{
    internal static async Task<IReadOnlyList<ForgeProcessorExpectedArtifact>> ReadExpectedArtifactsAsync(
        string installerJarPath,
        CancellationToken cancellationToken)
    {
        var artifacts = await LoaderProcessorArtifactProfileReader.ReadAsync(
            installerJarPath,
            "Forge",
            cancellationToken).ConfigureAwait(false);
        return artifacts
            .Select(artifact => new ForgeProcessorExpectedArtifact(artifact.RelativePath, artifact.TrustedSha1))
            .ToList();
    }
}
