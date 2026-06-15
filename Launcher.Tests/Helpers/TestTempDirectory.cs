namespace Launcher.Tests.Helpers;

public abstract class TestTempDirectory : IDisposable
{
    protected string TempRoot { get; } = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(TempRoot))
            Directory.Delete(TempRoot, recursive: true);
    }
}

