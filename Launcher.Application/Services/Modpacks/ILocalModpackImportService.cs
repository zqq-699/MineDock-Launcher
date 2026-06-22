using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface ILocalModpackImportService
{
    Task<ModpackRecognitionResult> RecognizeArchiveAsync(
        string archivePath,
        CancellationToken cancellationToken = default);

    Task<ModpackImportResult> ImportFromArchiveAsync(
        string archivePath,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        int downloadSpeedLimitMbPerSecond = 0);
}
