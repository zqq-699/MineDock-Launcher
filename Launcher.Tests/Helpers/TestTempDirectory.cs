namespace Launcher.Tests.Helpers;

public abstract class TestTempDirectory : IDisposable
{
    protected string TempRoot { get; } = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (!Directory.Exists(TempRoot))
            return;

        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                Directory.Delete(TempRoot, recursive: true);
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(50 * attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts)
            {
                Thread.Sleep(50 * attempt);
            }
        }

        if (Directory.Exists(TempRoot))
            Directory.Delete(TempRoot, recursive: true);
    }
}
