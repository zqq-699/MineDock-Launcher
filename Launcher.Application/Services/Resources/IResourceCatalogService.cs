using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface IResourceCatalogService
{
    Task<ResourceCatalogSearchResult> SearchProjectsAsync(
        ResourceCatalogSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        return SearchModsAsync(request, cancellationToken);
    }

    Task<ResourceCatalogSearchResult> SearchModsAsync(
        ResourceCatalogSearchRequest request,
        CancellationToken cancellationToken = default);

    Task<ResourceProjectVersionsResult> GetProjectVersionsAsync(
        ResourceProjectVersionsRequest request,
        CancellationToken cancellationToken = default);

    Task<string> InstallProjectVersionAsync(
        ResourceProjectVersion version,
        GameInstance instance,
        CancellationToken cancellationToken = default);

    Task<string> DownloadProjectVersionAsync(
        ResourceProjectVersion version,
        string targetDirectory,
        CancellationToken cancellationToken = default);

    Task<bool> ProjectVersionDownloadExistsAsync(
        ResourceProjectVersion version,
        string targetDirectory,
        CancellationToken cancellationToken = default);

    Task<bool> ProjectVersionInstallExistsAsync(
        ResourceProjectVersion version,
        GameInstance instance,
        CancellationToken cancellationToken = default);
}
