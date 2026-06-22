using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface ILocalResourcePackService
{
    Task<IReadOnlyList<LocalResourcePack>> GetResourcePacksAsync(
        GameInstance instance,
        CancellationToken cancellationToken = default);

    Task<LocalResourcePackImportResult> ImportAsync(
        GameInstance instance,
        string archivePath,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(LocalResourcePack resourcePack, CancellationToken cancellationToken = default);

    Task DeleteAsync(IEnumerable<LocalResourcePack> resourcePacks, CancellationToken cancellationToken = default);
}
