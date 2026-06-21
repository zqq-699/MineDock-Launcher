using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface ILocalSaveService
{
    Task<IReadOnlyList<LocalSave>> GetSavesAsync(GameInstance instance, CancellationToken cancellationToken = default);
    Task DeleteAsync(LocalSave save, CancellationToken cancellationToken = default);
    Task DeleteAsync(IEnumerable<LocalSave> saves, CancellationToken cancellationToken = default);
}
