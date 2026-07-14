using Launcher.Infrastructure.Minecraft;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class ComposedVersionInstallRunnerTests : TestTempDirectory
{
    [Fact]
    public async Task StartsIndependentFileInstallBeforeClientJarCompletes()
    {
        var versionDirectory = Path.Combine(TempRoot, "versions", "parallel");
        Directory.CreateDirectory(versionDirectory);
        var clientGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var installStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runTask = ComposedVersionInstallRunner.RunAsync(
            _ => Task.FromResult(new PreparedVersionInstall("parallel", versionDirectory, clientGate.Task)),
            (_, _) =>
            {
                installStarted.TrySetResult();
                return Task.CompletedTask;
            },
            CancellationToken.None);

        await installStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(runTask.IsCompleted);

        clientGate.TrySetResult();
        Assert.Equal("parallel", await runTask);
    }

    [Fact]
    public async Task FailedIndependentInstallCancelsClientDownloadAndCleansVersionDirectory()
    {
        var versionDirectory = Path.Combine(TempRoot, "versions", "failed");
        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(Path.Combine(versionDirectory, "failed.json"), "{}");
        var clientCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ComposedVersionInstallRunner.RunAsync(
                token => Task.FromResult(new PreparedVersionInstall(
                    "failed",
                    versionDirectory,
                    Task.Delay(Timeout.InfiniteTimeSpan, token).ContinueWith(
                        _ => clientCanceled.TrySetResult(),
                        CancellationToken.None))),
                (_, _) => Task.FromException(new InvalidOperationException("install failed")),
                CancellationToken.None));

        Assert.Equal("install failed", exception.Message);
        await clientCanceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(Directory.Exists(versionDirectory));
    }
}
