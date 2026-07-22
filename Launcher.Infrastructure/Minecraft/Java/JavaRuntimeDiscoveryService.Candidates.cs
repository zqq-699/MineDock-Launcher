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
using Microsoft.Win32;

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

    internal static IReadOnlyList<JavaRuntimeSearchRoot> GetJavaSearchRoots(
        string? minecraftDirectory,
        string programFiles,
        string programFilesX86,
        string applicationData,
        string localApplicationData,
        string userProfile,
        string documents,
        Func<string, string, SearchOption, IEnumerable<string>> enumerateDirectories)
    {
        var roots = new List<JavaRuntimeSearchRoot>();

        AddRoot(roots, minecraftDirectory, "runtime", "MinecraftRuntime");
        AddRoot(roots, applicationData, ".minecraft", "runtime", "OfficialMinecraftRuntime");
        AddRoot(
            roots,
            localApplicationData,
            "Packages",
            "Microsoft.4297127D64EC6_8wekyb3d8bbwe",
            "LocalCache",
            "Local",
            "runtime",
            "OfficialMinecraftRuntime");

        foreach (var programFilesPath in new[] { programFiles, programFilesX86 })
        {
            if (string.IsNullOrWhiteSpace(programFilesPath))
                continue;

            foreach (var vendorDirectory in new[]
                     {
                         "Java",
                         "Eclipse Adoptium",
                         "Amazon Corretto",
                         "AdoptOpenJDK",
                         "Eclipse Foundation",
                         "Semeru",
                         "Zulu",
                         "BellSoft"
                     })
            {
                AddRoot(roots, programFilesPath, vendorDirectory, "ProgramFiles");
            }

            var microsoftDirectory = Path.Combine(programFilesPath, "Microsoft");
            AddRoot(roots, microsoftDirectory, "jdk", "ProgramFiles");
            foreach (var microsoftJdkDirectory in enumerateDirectories(microsoftDirectory, "jdk-*", SearchOption.TopDirectoryOnly))
                AddRoot(roots, microsoftJdkDirectory, "ProgramFiles");

            AddRoot(roots, programFilesPath, "Minecraft Launcher", "runtime", "OfficialMinecraftRuntime");
            AddRoot(roots, programFilesPath, "Minecraft", "runtime", "OfficialMinecraftRuntime");
        }

        AddRoot(roots, userProfile, ".jdks", "UserJava");
        AddRoot(roots, userProfile, ".sdkman", "candidates", "java", "UserJava");

        AddRoot(roots, applicationData, ".hmcl", "java", "ThirdPartyLauncherRuntime");
        AddRoot(roots, applicationData, "ATLauncher", "runtimes", "minecraft", "ThirdPartyLauncherRuntime");
        AddRoot(roots, applicationData, "ModrinthApp", "meta", "java_versions", "ThirdPartyLauncherRuntime");
        AddRoot(roots, applicationData, "PrismLauncher", "java", "ThirdPartyLauncherRuntime");
        AddRoot(roots, userProfile, "curseforge", "minecraft", "Install", "runtime", "ThirdPartyLauncherRuntime");
        AddRoot(roots, localApplicationData, ".ftba", "bin", "runtime", "ThirdPartyLauncherRuntime");
        AddRoot(roots, documents, "Curse", "Minecraft", "Install", "runtime", "ThirdPartyLauncherRuntime");

        return roots
            .Where(root => !string.IsNullOrWhiteSpace(root.Path))
            .DistinctBy(root => NormalizePath(root.Path), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> EnumerateFilesSafely(string path, string searchPattern, SearchOption searchOption)
    {
        try
        {
            return Directory.EnumerateFiles(path, searchPattern, CreateEnumerationOptions(searchOption)).ToList();
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<string> EnumerateDirectoriesSafely(string path, string searchPattern, SearchOption searchOption)
    {
        try
        {
            return Directory.EnumerateDirectories(path, searchPattern, CreateEnumerationOptions(searchOption)).ToList();
        }
        catch
        {
            return [];
        }
    }

    private static EnumerationOptions CreateEnumerationOptions(SearchOption searchOption) => new()
    {
        RecurseSubdirectories = searchOption is SearchOption.AllDirectories,
        IgnoreInaccessible = true,
        ReturnSpecialDirectories = false
    };

    private static IEnumerable<string> EnumerateRegisteredJavaHomes()
    {
        if (!OperatingSystem.IsWindows())
            return [];

        var homes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                foreach (var keyPath in new[]
                         {
                             @"SOFTWARE\JavaSoft\Java Runtime Environment",
                             @"SOFTWARE\JavaSoft\Java Development Kit",
                             @"SOFTWARE\JavaSoft\JRE",
                             @"SOFTWARE\JavaSoft\JDK"
                         })
                {
                    TryCollectRegisteredJavaHomes(hive, view, keyPath, homes);
                }
            }
        }

        return homes;
    }

    private static void TryCollectRegisteredJavaHomes(
        RegistryHive hive,
        RegistryView view,
        string keyPath,
        ISet<string> homes)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var rootKey = baseKey.OpenSubKey(keyPath);
            if (rootKey is null)
                return;

            AddRegisteredJavaHome(rootKey.GetValue("JavaHome") as string, homes);
            foreach (var versionKeyName in rootKey.GetSubKeyNames())
            {
                using var versionKey = rootKey.OpenSubKey(versionKeyName);
                AddRegisteredJavaHome(versionKey?.GetValue("JavaHome") as string, homes);
            }
        }
        catch
        {
        }
    }

    private static void AddRegisteredJavaHome(string? javaHome, ISet<string> homes)
    {
        var normalizedJavaHome = NormalizeConfiguredDirectory(javaHome);
        if (!string.IsNullOrWhiteSpace(normalizedJavaHome))
            homes.Add(normalizedJavaHome);
    }

    private static void AddRoot(List<JavaRuntimeSearchRoot> roots, string? rootPath, string source)
    {
        var normalizedPath = NormalizeConfiguredDirectory(rootPath);
        if (!string.IsNullOrWhiteSpace(normalizedPath))
            roots.Add(new JavaRuntimeSearchRoot(normalizedPath, source));
    }

    private static void AddRoot(
        List<JavaRuntimeSearchRoot> roots,
        string? basePath,
        params string[] pathPartsAndSource)
    {
        if (string.IsNullOrWhiteSpace(basePath) || pathPartsAndSource.Length < 2)
            return;

        var source = pathPartsAndSource[^1];
        var pathParts = pathPartsAndSource[..^1];
        AddRoot(roots, Path.Combine([basePath, .. pathParts]), source);
    }

    private static string? NormalizeConfiguredDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return path.Trim().Trim('"');
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
