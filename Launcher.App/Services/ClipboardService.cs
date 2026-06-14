using System.Threading;
using System.Windows;

namespace Launcher.App.Services;

public sealed class ClipboardService : IClipboardService
{
    private const int RetryCount = 8;
    private const int RetryDelayMilliseconds = 35;

    public void CopyText(string text)
    {
        var thread = new Thread(() => TrySetClipboardText(text));
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
    }

    private static void TrySetClipboardText(string text)
    {
        for (var attempt = 0; attempt < RetryCount; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return;
            }
            catch
            {
                Thread.Sleep(RetryDelayMilliseconds * (attempt + 1));
            }
        }
    }
}
