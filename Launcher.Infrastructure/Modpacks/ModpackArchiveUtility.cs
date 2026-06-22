using System.IO;
using System.IO.Compression;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Modpacks;

internal static class ModpackArchiveUtility
{
    public static string NormalizeArchivePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var normalized = path.Replace('\\', '/').Trim();
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];

        normalized = normalized.TrimStart('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join("/", segments);
    }

    public static string GetValidatedTargetPath(string targetDirectory, string relativePath)
    {
        var normalizedRoot = Path.GetFullPath(targetDirectory);
        var relativePathForDisk = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(normalizedRoot, relativePathForDisk));
        var comparisonRoot = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(comparisonRoot, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullPath, normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.InvalidManifest,
                "Archive entry resolved outside the target directory.");
        }

        return fullPath;
    }

    public static void ExtractZipEntry(ZipArchiveEntry entry, string targetDirectory, string relativePath, CancellationToken cancellationToken)
    {
        var targetPath = GetValidatedTargetPath(targetDirectory, relativePath);
        var parentDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
            Directory.CreateDirectory(parentDirectory);

        cancellationToken.ThrowIfCancellationRequested();
        using var source = entry.Open();
        using var destination = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
        source.CopyTo(destination);
    }

    public static bool IsSupportedHttpUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }

    public static string RemovePrefix(string path, string prefix)
    {
        if (!path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        return path[(prefix.Length + 1)..];
    }
}
