using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface ILocalModIconEnrichmentService
{
    Task<IReadOnlyDictionary<string, string>> ResolveCachedIconSourcesAsync(
        IReadOnlyList<LocalMod> mods,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, string>> ResolveMissingIconSourcesAsync(
        IReadOnlyList<LocalMod> mods,
        CancellationToken cancellationToken = default,
        IProgress<IReadOnlyDictionary<string, string>>? progress = null);
}
