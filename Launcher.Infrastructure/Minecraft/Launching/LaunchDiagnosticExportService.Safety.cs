/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Launcher.Application.Services;
using Microsoft.Win32.SafeHandles;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Minecraft;

public sealed partial class LaunchDiagnosticExportService
{
private static bool TryValidateSafePath(string path, out string reason)
    {
        reason = "none";
        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root))
            {
                reason = "invalid-path";
                return false;
            }

            var current = root;
            var relative = Path.GetRelativePath(root, fullPath);
            foreach (var component in relative.Split(
                         [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, component);
                if (!File.Exists(current) && !Directory.Exists(current))
                {
                    reason = "missing";
                    return false;
                }

                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    reason = "unsafe-reparse-point";
                    return false;
                }
            }

            if (!File.Exists(fullPath))
            {
                reason = "missing";
                return false;
            }

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            reason = "access-denied";
            return false;
        }
        catch (IOException)
        {
            reason = "io-error";
            return false;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            reason = "invalid-path";
            return false;
        }
    }

    private static string NormalizeIndexValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = string.Join(
            " ",
            value.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return normalized.Length <= 512 ? normalized : normalized[..512] + "…";
    }

    private static bool TryGetFileIdentity(SafeFileHandle handle, out WindowsFileIdentity identity) =>
        WindowsFileSnapshot.TryGetIdentity(handle, out identity);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record ExportOutcome(
        LaunchDiagnosticType Type,
        string FileName,
        string? EntryName,
        bool IsExported,
        string? Reason,
        long SourceBytes);

}
