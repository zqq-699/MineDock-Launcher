/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Minecraft;

internal interface ILoaderFileManifestContributor
{
    LoaderKind Kind { get; }

    Task<LoaderFileManifestContribution> ResolveAsync(
        string versionDirectory,
        GameFileLoaderIdentity identity,
        CancellationToken cancellationToken);
}

internal sealed record LoaderFileManifestContribution(
    bool RequiresManifest,
    string? ManifestPath,
    LoaderArtifactManifest? Manifest,
    string? Error)
{
    public static LoaderFileManifestContribution Empty { get; } = new(
        RequiresManifest: false,
        ManifestPath: null,
        Manifest: null,
        Error: null);
}

internal sealed class ResolvedVersionLoaderFileManifestContributor(LoaderKind kind)
    : ILoaderFileManifestContributor
{
    public LoaderKind Kind { get; } = kind;

    public Task<LoaderFileManifestContribution> ResolveAsync(
        string versionDirectory,
        GameFileLoaderIdentity identity,
        CancellationToken cancellationToken) =>
        Task.FromResult(LoaderFileManifestContribution.Empty);
}

internal sealed class InstallerProfileLoaderFileManifestContributor(LoaderKind kind)
    : ILoaderFileManifestContributor
{
    public LoaderKind Kind { get; } = kind;

    public async Task<LoaderFileManifestContribution> ResolveAsync(
        string versionDirectory,
        GameFileLoaderIdentity identity,
        CancellationToken cancellationToken)
    {
        var path = LoaderArtifactManifestStore.GetPath(versionDirectory);
        var result = await LoaderArtifactManifestStore.ReadAsync(versionDirectory, identity, cancellationToken)
            .ConfigureAwait(false);
        return new LoaderFileManifestContribution(
            RequiresManifest: true,
            ManifestPath: path,
            Manifest: result.Manifest,
            Error: result.Error);
    }
}

internal static class LoaderFileManifestContributors
{
    public static IReadOnlyList<ILoaderFileManifestContributor> CreateDefault() =>
    [
        new ResolvedVersionLoaderFileManifestContributor(LoaderKind.Vanilla),
        new ResolvedVersionLoaderFileManifestContributor(LoaderKind.Fabric),
        new InstallerProfileLoaderFileManifestContributor(LoaderKind.Forge),
        new InstallerProfileLoaderFileManifestContributor(LoaderKind.NeoForge),
        new ResolvedVersionLoaderFileManifestContributor(LoaderKind.Quilt)
    ];
}
