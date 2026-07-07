using System.Diagnostics;

namespace Launcher.App.Services;

public sealed class ExternalLinkService : IExternalLinkService
{
    public bool TryOpen(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
