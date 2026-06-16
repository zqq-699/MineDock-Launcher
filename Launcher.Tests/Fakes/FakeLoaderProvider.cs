using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.Fakes;

internal sealed class FakeLoaderProvider : ILoaderProvider
{
    private int installCallCount;

    public LoaderKind Kind { get; init; } = LoaderKind.Vanilla;
    public string DisplayName { get; init; } = "Fake Vanilla";
    public bool IsImplemented { get; init; } = true;
    public IReadOnlyList<LoaderVersionInfo> LoaderVersions { get; init; } = [new LoaderVersionInfo("fake")];
    public Exception? GetLoaderVersionsException { get; init; }
    public Task? WaitBeforeGetLoaderVersions { get; init; }
    public string? LastGameDirectory { get; private set; }
    public string? LastIsolatedVersionName { get; private set; }
    public TaskCompletionSource<bool> InstallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task? WaitBeforeInstall { get; init; }
    public bool WriteJsonBeforeWaiting { get; init; }
    public string? PartialVersionName { get; init; }
    public int InstallCallCount => installCallCount;

    public Task<IReadOnlyList<LoaderVersionInfo>> GetLoaderVersionsAsync(string minecraftVersion, CancellationToken cancellationToken = default)
    {
        if (GetLoaderVersionsException is not null)
            return Task.FromException<IReadOnlyList<LoaderVersionInfo>>(GetLoaderVersionsException);

        return GetLoaderVersionsAsyncCore(cancellationToken);
    }

    private async Task<IReadOnlyList<LoaderVersionInfo>> GetLoaderVersionsAsyncCore(CancellationToken cancellationToken)
    {
        if (WaitBeforeGetLoaderVersions is not null)
            await WaitBeforeGetLoaderVersions.WaitAsync(cancellationToken);

        return LoaderVersions;
    }

    public async Task<string> InstallAsync(string minecraftVersion, string gameDirectory, string isolatedVersionName, string? loaderVersion, IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default)
    {
        LastGameDirectory = gameDirectory;
        LastIsolatedVersionName = isolatedVersionName;
        Interlocked.Increment(ref installCallCount);
        InstallStarted.TrySetResult(true);

        if (WriteJsonBeforeWaiting)
        {
            var partialVersionName = string.IsNullOrWhiteSpace(PartialVersionName)
                ? isolatedVersionName
                : PartialVersionName;
            var partialVersionDirectory = Path.Combine(gameDirectory, "versions", partialVersionName);
            Directory.CreateDirectory(partialVersionDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(partialVersionDirectory, $"{partialVersionName}.json"),
                $$"""
                {
                  "id": "{{partialVersionName}}",
                  "jar": "{{partialVersionName}}"
                }
                """,
                cancellationToken);
        }

        if (WaitBeforeInstall is not null)
            await WaitBeforeInstall.WaitAsync(cancellationToken);

        var versionDirectory = Path.Combine(gameDirectory, "versions", isolatedVersionName);
        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(versionDirectory, $"{isolatedVersionName}.json"),
            $$"""
            {
              "id": "{{isolatedVersionName}}",
              "jar": "{{isolatedVersionName}}"
            }
            """,
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(versionDirectory, $"{isolatedVersionName}.jar"),
            "fake jar",
            cancellationToken);
        return isolatedVersionName;
    }
}

