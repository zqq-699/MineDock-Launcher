using Launcher.Application.Services;
using Launcher.Infrastructure.Minecraft;
using Launcher.Infrastructure.Modpacks;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class DownloadConcurrencyTests
{
    [Fact]
    public async Task CategoriesShareOneGlobalDownloadBudget()
    {
        var limiter = new ImportConcurrencyLimiter();
        var leases = new List<IImportConcurrencyLease>();
        for (var index = 0; index < 12; index++)
        {
            leases.Add((index % 3) switch
            {
                0 => await limiter.AcquireMetadataSlotAsync(),
                1 => await limiter.AcquireRuntimeDownloadSlotAsync(),
                _ => await limiter.AcquireModpackDownloadSlotAsync()
            });
        }

        try
        {
            Assert.Equal(12, limiter.DownloadSnapshot.ActiveCount);
            var queued = limiter.AcquireRuntimeDownloadSlotAsync().AsTask();
            Assert.False(queued.IsCompleted);

            var firstLease = leases[0];
            leases.RemoveAt(0);
            firstLease.Dispose();
            var releasedLease = await queued.WaitAsync(TimeSpan.FromSeconds(2));
            releasedLease.Dispose();
        }
        finally
        {
            foreach (var lease in leases)
                lease.Dispose();
        }
    }
}
