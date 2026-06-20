namespace Launcher.Application.Services;

public interface IDownloadSpeedLimitState
{
    int DownloadSpeedLimitMbPerSecond { get; }

    void SetDownloadSpeedLimitMbPerSecond(int downloadSpeedLimitMbPerSecond);
}
