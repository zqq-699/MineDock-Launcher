using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests;

internal sealed class FakeLoaderProvider : ILoaderProvider
{
    private int installCallCount;

    public LoaderKind Kind => LoaderKind.Vanilla;
    public string DisplayName => "Fake Vanilla";
    public bool IsImplemented => true;
    public string? LastGameDirectory { get; private set; }
    public string? LastIsolatedVersionName { get; private set; }
    public Task? WaitBeforeInstall { get; init; }
    public int InstallCallCount => installCallCount;

    public Task<IReadOnlyList<LoaderVersionInfo>> GetLoaderVersionsAsync(string minecraftVersion, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<LoaderVersionInfo> versions = [new LoaderVersionInfo("fake")];
        return Task.FromResult(versions);
    }

    public async Task<string> InstallAsync(string minecraftVersion, string gameDirectory, string isolatedVersionName, string? loaderVersion, IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default)
    {
        LastGameDirectory = gameDirectory;
        LastIsolatedVersionName = isolatedVersionName;
        Interlocked.Increment(ref installCallCount);

        if (WaitBeforeInstall is not null)
            await WaitBeforeInstall;

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
