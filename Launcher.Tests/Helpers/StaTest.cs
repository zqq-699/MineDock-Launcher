using System.Runtime.ExceptionServices;

namespace Launcher.Tests.Helpers;

internal static class StaTest
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    internal static void Run(Action action, TimeSpan? timeout = null)
    {
        ExceptionDispatchInfo? capturedException = null;
        using var completed = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                capturedException = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                completed.Set();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        if (!completed.Wait(timeout ?? DefaultTimeout))
            throw new TimeoutException("STA test did not complete within the configured timeout.");

        capturedException?.Throw();
    }
}
