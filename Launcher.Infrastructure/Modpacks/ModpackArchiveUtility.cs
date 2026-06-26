using System.IO;
using System.IO.Compression;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Modpacks;

internal static class ModpackArchiveUtility
{
    public const long MaxManifestBytes = 8L * 1024 * 1024;
    public const long MaxEmbeddedModpackBytes = 256L * 1024 * 1024;
    public const long MaxOverrideEntryBytes = 256L * 1024 * 1024;
    public const long MaxOverrideTotalBytes = 1024L * 1024 * 1024;

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

    public static async Task ExtractZipEntryAsync(
        ZipArchiveEntry entry,
        string targetDirectory,
        string relativePath,
        ZipExtractionBudget extractionBudget,
        CancellationToken cancellationToken)
    {
        if (entry.Length > MaxOverrideEntryBytes)
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.InvalidManifest,
                $"Archive entry is too large: {entry.FullName}");
        }

        extractionBudget.Reserve(entry.Length);
        var targetPath = GetValidatedTargetPath(targetDirectory, relativePath);
        var parentDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
            Directory.CreateDirectory(parentDirectory);

        cancellationToken.ThrowIfCancellationRequested();
        await using var source = entry.Open();
        await using var destination = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await CopyToAsync(
            source,
            destination,
            MaxOverrideEntryBytes,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<MemoryStream> CopyZipEntryToMemoryAsync(
        ZipArchiveEntry entry,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        if (entry.Length > maxBytes)
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.InvalidManifest,
                $"Archive entry is too large: {entry.FullName}");
        }

        var capacity = entry.Length is > 0 and <= int.MaxValue ? (int)entry.Length : 0;
        var destination = new MemoryStream(capacity);
        await using var source = entry.Open();
        await CopyToAsync(source, destination, maxBytes, cancellationToken).ConfigureAwait(false);
        destination.Position = 0;
        return destination;
    }

    private static async Task CopyToAsync(
        Stream source,
        Stream destination,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long copiedBytes = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                return;

            copiedBytes += read;
            if (copiedBytes > maxBytes)
            {
                throw new ModpackImportException(
                    ModpackImportFailureReason.InvalidManifest,
                    "Archive entry exceeded the allowed size.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
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

internal sealed class ZipExtractionBudget(long maxTotalBytes)
{
    private long extractedBytes;

    public void Reserve(long bytes)
    {
        if (bytes < 0)
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.InvalidManifest,
                "Archive entry has an invalid size.");
        }

        var totalBytes = Interlocked.Add(ref extractedBytes, bytes);
        if (totalBytes > maxTotalBytes)
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.InvalidManifest,
                "Archive contents exceed the allowed total size.");
        }
    }
}
