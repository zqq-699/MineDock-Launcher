/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.GameSettings;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.ViewModels.GameSettings;

public sealed class LocalResourceCategoryTagTests
{
    [Fact]
    public void LocalItemViewModelsUseExistingLocalizedCategoryTitles()
    {
        var mod = new ModManagementModItemViewModel(new LocalMod
        {
            Name = "Mod",
            FileName = "mod.jar",
            FullPath = "mod.jar",
            Categories = [ResourceProjectCategory.Optimization, ResourceProjectCategory.Audio]
        });
        var resourcePack = new ResourcePackManagementItemViewModel(new LocalResourcePack
        {
            Name = "Pack",
            FileName = "pack.zip",
            FullPath = "pack.zip",
            Categories = [ResourceProjectCategory.Themed, ResourceProjectCategory.Realistic]
        });
        var shaderPack = new ShaderPackManagementItemViewModel(new LocalShaderPack
        {
            Name = "Shader",
            FileName = "shader.zip",
            FullPath = "shader.zip",
            Categories = [ResourceProjectCategory.Fantasy, ResourceProjectCategory.Utility]
        });

        Assert.Equal([Strings.Resources_ModFilterTypeOptimization], mod.TitleTags);
        Assert.Equal(
            [Strings.Resources_ResourcePackFilterTypeThemed, Strings.Resources_ResourcePackFilterTypeRealistic],
            resourcePack.TitleTags);
        Assert.Equal([Strings.Resources_ShaderPackFilterTypeFantasy], shaderPack.TitleTags);
        Assert.True(mod.HasTitleTags);
        Assert.True(resourcePack.HasTitleTags);
        Assert.True(shaderPack.HasTitleTags);
    }

    [Fact]
    public void LocalItemInfoAvailabilityRequiresRecognizedProjectReference()
    {
        var recognized = new ResourceProjectReference(
            ResourceProjectKind.Mod,
            ResourceProjectSource.Modrinth,
            "recognized-project");

        var recognizedMod = new ModManagementModItemViewModel(new LocalMod { ProjectReference = recognized });
        var unknownResourcePack = new ResourcePackManagementItemViewModel(new LocalResourcePack());
        var unknownShaderPack = new ShaderPackManagementItemViewModel(new LocalShaderPack());

        Assert.True(recognizedMod.HasProjectDetails);
        Assert.False(unknownResourcePack.HasProjectDetails);
        Assert.False(unknownShaderPack.HasProjectDetails);
    }

    [Fact]
    public async Task ShaderPackEnrichmentAppliesWebsiteIconWithoutReplacingExistingIcon()
    {
        var matched = new LocalShaderPack { FullPath = "matched.zip" };
        var embedded = new LocalShaderPack { FullPath = "embedded.zip", IconSource = "file:///embedded.png" };
        LocalShaderPack[] items = [matched, embedded];
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var coordinator = new LocalResourceCategoryEnrichmentCoordinator<LocalShaderPack>(
            new ShaderMetadataService(),
            ResourceProjectKind.ShaderPack,
            shaderPack => shaderPack.FullPath,
            shaderPack => shaderPack.Categories,
            static (shaderPack, categories) => shaderPack.Categories = categories,
            () => items,
            () => changed.TrySetResult(),
            ImmediateUiDispatcher.Instance,
            NullLogger.Instance,
            shaderPack => shaderPack.IconSource,
            static (shaderPack, iconSource) => shaderPack.IconSource = iconSource,
            shaderPack => shaderPack.ProjectReference,
            static (shaderPack, reference) => shaderPack.ProjectReference = reference);

        coordinator.Queue(items);
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("file:///website.png", matched.IconSource);
        Assert.Equal("file:///embedded.png", embedded.IconSource);
        Assert.Equal("shader-project", matched.ProjectReference?.ProjectId);
    }

    private sealed class ShaderMetadataService : ILocalResourceCategoryEnrichmentService
    {
        public Task<IReadOnlyDictionary<string, LocalResourceEnrichmentResult>> ResolveCachedMetadataAsync(
            IReadOnlyList<LocalResourceCategoryCandidate> resources,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, LocalResourceEnrichmentResult>>(
                new Dictionary<string, LocalResourceEnrichmentResult>());

        public Task<IReadOnlyDictionary<string, LocalResourceEnrichmentResult>> ResolveMetadataAsync(
            IReadOnlyList<LocalResourceCategoryCandidate> resources,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, LocalResourceEnrichmentResult>>(
                resources.ToDictionary(
                    resource => resource.FullPath,
                    _ => new LocalResourceEnrichmentResult(
                        [],
                        "file:///website.png",
                        new ResourceProjectReference(
                            ResourceProjectKind.ShaderPack,
                            ResourceProjectSource.Modrinth,
                            "shader-project")),
                    StringComparer.OrdinalIgnoreCase));

        public Task<IReadOnlyDictionary<string, IReadOnlyList<ResourceProjectCategory>>> ResolveCachedCategoriesAsync(
            IReadOnlyList<LocalResourceCategoryCandidate> resources,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<ResourceProjectCategory>>>(
                new Dictionary<string, IReadOnlyList<ResourceProjectCategory>>());

        public Task<IReadOnlyDictionary<string, IReadOnlyList<ResourceProjectCategory>>> ResolveCategoriesAsync(
            IReadOnlyList<LocalResourceCategoryCandidate> resources,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<ResourceProjectCategory>>>(
                new Dictionary<string, IReadOnlyList<ResourceProjectCategory>>());
    }
}
