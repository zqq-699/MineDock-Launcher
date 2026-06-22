namespace Launcher.Application.Services;

public interface IImportConcurrencyLimiter
{
    ValueTask<IAsyncDisposable> AcquireMetadataSlotAsync(CancellationToken cancellationToken = default);

    ValueTask<IAsyncDisposable> AcquireModpackDownloadSlotAsync(CancellationToken cancellationToken = default);

    ValueTask<IAsyncDisposable> AcquireRuntimeDownloadSlotAsync(CancellationToken cancellationToken = default);

    ValueTask<IAsyncDisposable> AcquireHashSlotAsync(CancellationToken cancellationToken = default);
}
