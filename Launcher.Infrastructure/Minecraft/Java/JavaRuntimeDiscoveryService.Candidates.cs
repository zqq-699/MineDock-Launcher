/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Minecraft;

public sealed partial class JavaRuntimeDiscoveryService
{
private static void AddCandidate(
        List<JavaRuntimeCandidate> candidates,
        HashSet<string> seenExecutablePaths,
        string executablePath,
        string source,
        Func<string, bool> fileExists,
        Func<string, string> resolveIdentityPath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return;

        var normalizedPath = NormalizePath(executablePath);
        if (!fileExists(normalizedPath) || !seenExecutablePaths.Add(normalizedPath))
            return;

        candidates.Add(new JavaRuntimeCandidate(
            normalizedPath,
            source,
            NormalizePath(resolveIdentityPath(normalizedPath))));
    }

    private static IReadOnlyList<JavaRuntimeCandidate> CollapseDuplicateCandidates(IReadOnlyList<JavaRuntimeCandidate> candidates)
    {
        return candidates
            .GroupBy(candidate => candidate.IdentityPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(candidate => GetSourcePriority(candidate.Source))
                .ThenBy(candidate => candidate.ExecutablePath.Length)
                .First())
            .ToList();
    }

    private static IEnumerable<string> GetCommonJavaRoots(Func<string> getProgramFiles, Func<string> getProgramFilesX86)
    {
        foreach (var programFilesPath in new[] { getProgramFiles(), getProgramFilesX86() })
        {
            if (string.IsNullOrWhiteSpace(programFilesPath))
                continue;

            yield return Path.Combine(programFilesPath, "Java");
            yield return Path.Combine(programFilesPath, "Eclipse Adoptium");
            yield return Path.Combine(programFilesPath, "Microsoft", "jdk");
            yield return Path.Combine(programFilesPath, "Zulu");
            yield return Path.Combine(programFilesPath, "BellSoft");
        }
    }

    private static IEnumerable<string> EnumerateFilesSafely(string path, string searchPattern, SearchOption searchOption)
    {
        try
        {
            return Directory.EnumerateFiles(path, searchPattern, searchOption).ToList();
        }
        catch
        {
            return [];
        }
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path.Trim();
        }
    }

    private static string ResolveJavaExecutableIdentityPath(string executablePath)
    {
        try
        {
            var linkTarget = new FileInfo(executablePath).ResolveLinkTarget(returnFinalTarget: true);
            return linkTarget?.FullName ?? executablePath;
        }
        catch
        {
            return executablePath;
        }
    }
}
