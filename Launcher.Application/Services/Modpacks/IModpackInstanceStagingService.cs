using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface IModpackInstanceStagingService
{
    Task<StagedModpackInstance> StageAsync(
        PreparedModpack preparedModpack,
        string resolvedInstanceName,
        CancellationToken cancellationToken = default);

    Task<GameInstance> FinalizeAsync(
        StagedModpackInstance stagedInstance,
        string finalVersionName,
        CancellationToken cancellationToken = default);

    Task CleanupFailedImportAsync(
        StagedModpackInstance stagedInstance,
        string? finalVersionName,
        CancellationToken cancellationToken = default);
}

public sealed class StagedModpackInstance
{
    public string ResolvedInstanceName { get; init; } = string.Empty;

    public string MinecraftDirectory { get; init; } = string.Empty;

    public string StagingContentDirectory { get; init; } = string.Empty;

    public GameInstance Instance { get; init; } = new();
}
