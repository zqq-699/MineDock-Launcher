/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using System.Text;

namespace Launcher.Infrastructure.Minecraft;

internal static class BoundedDiagnosticFileReader
{
    public const int DefaultMaxLines = 120;
    public const int DefaultMaxBytes = 256 * 1024;

    public static Task<string> ReadHeadAsync(
        string? path,
        CancellationToken cancellationToken,
        int maxLines = DefaultMaxLines,
        int maxBytes = DefaultMaxBytes) =>
        ReadAsync(path, fromTail: false, maxLines, maxBytes, cancellationToken);

    public static Task<string> ReadTailAsync(
        string? path,
        CancellationToken cancellationToken,
        int maxLines = DefaultMaxLines,
        int maxBytes = DefaultMaxBytes) =>
        ReadAsync(path, fromTail: true, maxLines, maxBytes, cancellationToken);

    private static async Task<string> ReadAsync(
        string? path,
        bool fromTail,
        int maxLines,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || maxLines <= 0 || maxBytes <= 0)
            return string.Empty;

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 81920,
                useAsync: true);
            var start = fromTail ? Math.Max(0, stream.Length - maxBytes) : 0;
            stream.Seek(start, SeekOrigin.Begin);
            var bytesToRead = (int)Math.Min(maxBytes, stream.Length - start);
            if (bytesToRead <= 0)
                return string.Empty;

            var buffer = new byte[bytesToRead];
            var totalRead = 0;
            while (totalRead < buffer.Length)
            {
                var read = await stream
                    .ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                    break;
                totalRead += read;
            }

            var text = Encoding.UTF8.GetString(buffer, 0, totalRead).TrimStart('\uFEFF');
            if (fromTail && start > 0)
            {
                var firstLineBreak = text.IndexOf('\n');
                if (firstLineBreak >= 0)
                    text = text[(firstLineBreak + 1)..];
            }

            var lines = text.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var selected = fromTail ? lines.TakeLast(maxLines) : lines.Take(maxLines);
            return string.Join(Environment.NewLine, selected);
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}
