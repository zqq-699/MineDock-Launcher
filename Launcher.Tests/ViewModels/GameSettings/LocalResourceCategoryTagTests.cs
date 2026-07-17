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
            static (shaderPack, iconSource) => shaderPack.IconSource = iconSource);

        coordinator.Queue(items);
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("file:///website.png", matched.IconSource);
        Assert.Equal("file:///embedded.png", embedded.IconSource);
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
                    _ => new LocalResourceEnrichmentResult([], "file:///website.png"),
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
