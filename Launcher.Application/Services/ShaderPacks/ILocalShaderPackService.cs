using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface ILocalShaderPackService
{
    Task<IReadOnlyList<LocalShaderPack>> GetShaderPacksAsync(
        GameInstance instance,
        CancellationToken cancellationToken = default);

    Task<LocalShaderPackImportResult> ImportAsync(
        GameInstance instance,
        string archivePath,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(LocalShaderPack shaderPack, CancellationToken cancellationToken = default);

    Task DeleteAsync(IEnumerable<LocalShaderPack> shaderPacks, CancellationToken cancellationToken = default);
}
