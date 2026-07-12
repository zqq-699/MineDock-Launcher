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

    private static string Normalize(string path)
    {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
