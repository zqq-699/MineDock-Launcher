using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface IModpackPackageService
{
    Task<ModpackRecognitionResult> RecognizeAsync(
        string archivePath,
        CancellationToken cancellationToken = default);

    Task<PreparedModpack> PrepareAsync(
        string archivePath,
        CancellationToken cancellationToken = default,
        IProgress<LauncherProgress>? progress = null);

    Task<IReadOnlyList<ManualModpackDownload>> DownloadFilesAsync(
        PreparedModpack preparedModpack,
        GameInstance instance,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        int downloadSpeedLimitMbPerSecond = 0);

    Task CopyOverridesAsync(
        PreparedModpack preparedModpack,
        GameInstance instance,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default);

    Task<string?> WriteManualDownloadsFileAsync(
        PreparedModpack preparedModpack,
        GameInstance instance,
        IReadOnlyList<ManualModpackDownload> manualDownloads,
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
