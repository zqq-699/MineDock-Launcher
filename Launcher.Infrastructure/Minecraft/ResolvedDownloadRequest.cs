using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Minecraft;

internal sealed record ResolvedDownloadRequest(
    string OriginalUrl,
    string ActualUrl,
    DownloadSourcePreference RequestedSourcePreference,
    string ResolvedSourceKind,
    string ResourceCategory);
