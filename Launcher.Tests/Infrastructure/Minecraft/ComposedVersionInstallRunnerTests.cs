using Launcher.Infrastructure.Minecraft;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class ComposedVersionInstallRunnerTests : TestTempDirectory
{
    [Fact]
    public async Task StartsFileInstallOnlyAfterVersionPreparationCompletes()
    {
        var versionDirectory = Path.Combine(TempRoot, "versions", "sequential");
        var preparationGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var installStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runTask = ComposedVersionInstallRunner.RunAsync(
            async token =>
            {
                await preparationGate.Task.WaitAsync(token);
                Directory.CreateDirectory(versionDirectory);
                return new PreparedVersionInstall("sequential", versionDirectory);
            },
            (_, _) =>
            {
                installStarted.TrySetResult();
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.False(installStarted.Task.IsCompleted);
        preparationGate.TrySetResult();
        await installStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("sequential", await runTask);
        Assert.True(Directory.Exists(versionDirectory));
    }

    [Fact]
    public async Task FailedFileInstallCleansVersionDirectoryAndPreservesException()
    {
        var versionDirectory = Path.Combine(TempRoot, "versions", "failed");
        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(Path.Combine(versionDirectory, "failed.json"), "{}");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ComposedVersionInstallRunner.RunAsync(
                _ => Task.FromResult(new PreparedVersionInstall("failed", versionDirectory)),
                (_, _) => Task.FromException(new InvalidOperationException("install failed")),
                CancellationToken.None));

        Assert.Equal("install failed", exception.Message);
        Assert.False(Directory.Exists(versionDirectory));
    }

}
