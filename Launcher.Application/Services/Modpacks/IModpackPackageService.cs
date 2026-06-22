using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface IModpackPackageService
{
    Task<ModpackRecognitionResult> RecognizeAsync(
        string archivePath,
        CancellationToken cancellationToken = default);

    Task<PreparedModpack> PrepareAsync(
        string archivePath,
        CancellationToken cancellationToken = default);

    Task InstallContentAsync(
        PreparedModpack preparedModpack,
        GameInstance instance,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        int downloadSpeedLimitMbPerSecond = 0);

    Task CleanupAsync(
        PreparedModpack preparedModpack,
        CancellationToken cancellationToken = default);
}
