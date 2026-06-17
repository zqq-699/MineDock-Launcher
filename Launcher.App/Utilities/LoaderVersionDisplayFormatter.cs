using Launcher.Domain.Models;

namespace Launcher.App.Utilities;

internal static class LoaderVersionDisplayFormatter
{
    public static string Format(LoaderKind loader, string? loaderVersion)
    {
        if (string.IsNullOrWhiteSpace(loaderVersion))
            return string.Empty;

        var normalized = loaderVersion.Trim();
        if (loader is not LoaderKind.Fabric)
            return normalized;

        var mixinIndex = normalized.IndexOf("+mixin", StringComparison.OrdinalIgnoreCase);
        if (mixinIndex >= 0)
            normalized = normalized[..mixinIndex];

        var separatorIndex = normalized.IndexOfAny([' ', '/', ',', '(']);
        if (separatorIndex > 0 && normalized.Contains("mixin", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..separatorIndex];

        return normalized.Trim();
    }
}
