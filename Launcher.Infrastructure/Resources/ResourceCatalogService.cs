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

using System.Diagnostics;
using System.Net.Http;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.CurseForge;
using Launcher.Infrastructure.FileSystem;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Resources;

public sealed class ResourceCatalogService : IResourceCatalogService
{
    private readonly IReadOnlyDictionary<ResourceProjectSource, IResourceProviderClient> providers;
    private readonly ResourceProjectStorage storage;
    private readonly ILogger<ResourceCatalogService> logger;

    public ResourceCatalogService(
        HttpClient? httpClient = null,
        LauncherPathProvider? pathProvider = null,
        ISettingsService? settingsService = null,
        ILogger<ResourceCatalogService>? logger = null,
        ICurseForgeApiKeyResolver? curseForgeApiKeyResolver = null,
        ILocalSaveService? localSaveService = null)
    {
        var resolvedPathProvider = pathProvider ?? new LauncherPathProvider();
        var resolvedHttpClient = httpClient ?? new HttpClient();
        this.logger = logger ?? NullLogger<ResourceCatalogService>.Instance;
        var keyResolver = curseForgeApiKeyResolver
            ?? new CurseForgeApiKeyResolver(resolvedPathProvider, settingsService);
        var resolvedLocalSaveService = localSaveService ?? new LocalSaveService(resolvedPathProvider);

        if (!resolvedHttpClient.DefaultRequestHeaders.UserAgent.Any())
            resolvedHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BHL/0.1 (BlockHelm-Launcher)");

        var clients = new IResourceProviderClient[]
        {
            new ModrinthResourceClient(resolvedHttpClient),
            new CurseForgeResourceClient(resolvedHttpClient, keyResolver, this.logger)
        };
        providers = clients.ToDictionary(client => client.Source);
        storage = new ResourceProjectStorage(resolvedHttpClient, resolvedLocalSaveService, this.logger);
    }

    public Task<ResourceCatalogSearchResult> SearchModsAsync(
        ResourceCatalogSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        return SearchProjectsAsync(new ResourceCatalogSearchRequest
        {
            Kind = ResourceProjectKind.Mod,
            Query = request.Query,
            MinecraftVersion = request.MinecraftVersion,
            MinecraftVersions = request.MinecraftVersions,
            Loader = request.Loader,
            Source = request.Source,
            Category = request.Category,
            Offset = request.Offset,
            PageSize = request.PageSize
        }, cancellationToken);
    }

    public async Task<ResourceCatalogSearchResult> SearchProjectsAsync(
        ResourceCatalogSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Searching resource projects. Kind={Kind} Query={Query} Source={Source} Offset={Offset} PageSize={PageSize}",
            request.Kind,
            request.Query,
            request.Source,
            request.Offset,
            request.PageSize);

        var totalStopwatch = Stopwatch.StartNew();
        var selectedProviders = request.Source is { } source
            ? providers.TryGetValue(source, out var selectedProvider) ? [selectedProvider] : []
            : providers.Values.ToArray();
        var supportedProviders = selectedProviders
            .Where(value => value.Supports(request.Kind))
            .ToArray();
        var projects = new List<ResourceProject>();
        var hasMore = false;
        var curseForgeUnavailable = false;
        var curseForgeApiKeyMissing = false;

        var providerResults = await Task.WhenAll(supportedProviders.Select(provider =>
            SearchProviderAsync(provider, request, cancellationToken))).ConfigureAwait(false);
        foreach (var (providerSource, result) in providerResults)
        {
            projects.AddRange(result.Projects);
            hasMore |= result.HasMore;
            if (providerSource is ResourceProjectSource.CurseForge)
            {
                curseForgeUnavailable |= result.IsUnavailable;
                curseForgeApiKeyMissing |= result.IsApiKeyMissing;
            }
        }

        var searchResult = new ResourceCatalogSearchResult
        {
            Projects = projects
                .OrderByDescending(project => project.Downloads)
                .ThenBy(project => project.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            IsCurseForgeUnavailable = curseForgeUnavailable,
            IsCurseForgeApiKeyMissing = curseForgeApiKeyMissing,
            HasMore = hasMore
        };
        logger.LogInformation(
            "Resource project search completed. Kind={Kind} ProviderCount={ProviderCount} ResultCount={ResultCount} ElapsedMilliseconds={ElapsedMilliseconds}",
            request.Kind,
            supportedProviders.Length,
            searchResult.Projects.Count,
            totalStopwatch.ElapsedMilliseconds);
        return searchResult;
    }

    private async Task<(ResourceProjectSource Source, ResourceProviderSearchResult Result)> SearchProviderAsync(
        IResourceProviderClient provider,
        ResourceCatalogSearchRequest request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await provider.SearchAsync(request, cancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "Resource provider search completed. Kind={Kind} Source={Source} ResultCount={ResultCount} ElapsedMilliseconds={ElapsedMilliseconds}",
                request.Kind,
                provider.Source,
                result.Projects.Count,
                stopwatch.ElapsedMilliseconds);
            return (provider.Source, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Resource provider search failed. Kind={Kind} Source={Source} ElapsedMilliseconds={ElapsedMilliseconds}",
                request.Kind,
                provider.Source,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    public async Task<ResourceProjectVersionsResult> GetProjectVersionsAsync(
        ResourceProjectVersionsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Kind is ResourceProjectKind.Mod
            && !request.IncludeAllVersions
            && request.Loader is LoaderKind.Vanilla)
        {
            return new ResourceProjectVersionsResult();
        }

        return providers.TryGetValue(request.Source, out var provider)
            ? await provider.GetVersionsAsync(request, cancellationToken).ConfigureAwait(false)
            : new ResourceProjectVersionsResult();
    }

    public async Task<ResourceProjectDependenciesResult> GetProjectDependenciesAsync(
        ResourceProjectDependenciesRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Kind is not ResourceProjectKind.Mod)
            return new ResourceProjectDependenciesResult();
        return providers.TryGetValue(request.Source, out var provider)
            ? await provider.GetDependenciesAsync(request, cancellationToken).ConfigureAwait(false)
            : new ResourceProjectDependenciesResult();
    }

    public async Task<string> InstallProjectVersionAsync(
        ResourceProjectVersion version,
        GameInstance instance,
        CancellationToken cancellationToken = default)
    {
        var target = await storage.InstallAsync(version, instance, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Resource project version installed. VersionId={VersionId} Target={Target}", version.VersionId, target);
        return target;
    }

    public async Task<string> DownloadProjectVersionAsync(
        ResourceProjectVersion version,
        string targetDirectory,
        CancellationToken cancellationToken = default)
    {
        var target = await storage.DownloadAsync(version, targetDirectory, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Resource project version downloaded. VersionId={VersionId} Target={Target}", version.VersionId, target);
        return target;
    }

    public Task<bool> ProjectVersionDownloadExistsAsync(
        ResourceProjectVersion version,
        string targetDirectory,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(storage.DownloadExists(version, targetDirectory));

    public Task<bool> ProjectVersionInstallExistsAsync(
        ResourceProjectVersion version,
        GameInstance instance,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(storage.InstallExists(version, instance));
}
