/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using CmlLib.Core;

namespace Launcher.Infrastructure.Minecraft;

internal static class MinecraftPathGuard
{
    public static string EnsureWithin(string candidate, string parent, string description)
    {
        var normalizedCandidate = Normalize(candidate);
        var normalizedParent = Normalize(parent);
        if (!string.Equals(normalizedCandidate, normalizedParent, StringComparison.OrdinalIgnoreCase)
            && !normalizedCandidate.StartsWith(
                normalizedParent + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"{description} escaped its allowed directory: {candidate}");
        }

        return normalizedCandidate;
    }

    public static string EnsureInstallFilePath(MinecraftPath path, string candidate)
    {
        foreach (var root in new[] { path.Versions, path.Library, path.Assets, path.Resource, path.Runtime })
        {
            try
            {
                return EnsureWithin(candidate, root, "Minecraft install file");
            }
            catch (InvalidDataException)
            {
            }
        }

        throw new InvalidDataException($"Minecraft install file escaped every allowed directory: {candidate}");
    }

    public static string EnsureSafeFileDestination(string candidate, string parent, string description)
    {
        var normalizedCandidate = EnsureWithin(candidate, parent, description);
        var normalizedParent = Normalize(parent);
        EnsureNoReparsePoints(normalizedParent, normalizedCandidate, description);
        if (Directory.Exists(normalizedCandidate))
            throw new InvalidDataException($"{description} destination is a directory: {normalizedCandidate}");
        return normalizedCandidate;
    }

    public static string EnsureSafeDirectory(string directory, string parent, string description)
    {
        var normalizedDirectory = EnsureWithin(directory, parent, description);
        var normalizedParent = Normalize(parent);
        EnsureNoReparsePoints(normalizedParent, normalizedDirectory, description);
        Directory.CreateDirectory(normalizedDirectory);
        EnsureNoReparsePoints(normalizedParent, normalizedDirectory, description);
        return normalizedDirectory;
    }

    public static void EnsureNoReparsePoints(string parent, string candidate, string description)
    {
        var normalizedParent = Normalize(parent);
        var normalizedCandidate = EnsureWithin(candidate, normalizedParent, description);
        Inspect(normalizedParent);
        var relative = Path.GetRelativePath(normalizedParent, normalizedCandidate);
        if (relative == ".")
            return;

        var current = normalizedParent;
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            Inspect(current);
        }

        void Inspect(string path)
        {
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"{description} contains a reparse point: {path}");
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }
        }
    }

    private static string Normalize(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
