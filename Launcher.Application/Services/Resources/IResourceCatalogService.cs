using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface IResourceCatalogService
{
    Task<ResourceCatalogSearchResult> SearchModsAsync(
        ResourceCatalogSearchRequest request,
        CancellationToken cancellationToken = default);
}
