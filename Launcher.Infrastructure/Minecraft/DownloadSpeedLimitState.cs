using System.Threading;
using Launcher.Application.Services;

namespace Launcher.Infrastructure.Minecraft;

internal sealed class DownloadSpeedLimitState : IDownloadSpeedLimitState
{
    private int downloadSpeedLimitMbPerSecond;

    public int DownloadSpeedLimitMbPerSecond => Volatile.Read(ref downloadSpeedLimitMbPerSecond);

    public void SetDownloadSpeedLimitMbPerSecond(int downloadSpeedLimitMbPerSecond)
    {
        Interlocked.Exchange(ref this.downloadSpeedLimitMbPerSecond, Math.Max(downloadSpeedLimitMbPerSecond, 0));
    }
}
