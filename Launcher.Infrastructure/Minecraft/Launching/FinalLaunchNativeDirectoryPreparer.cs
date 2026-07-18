/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Minecraft;

internal static class FinalLaunchNativeDirectoryPreparer
{
    public static void Prepare(
        ProcessStartInfo startInfo,
        string nativeRoot,
        string versionName,
        ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(nativeRoot))
            return;

        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var preparedPaths = new HashSet<string>(comparer);
        var workingDirectory = string.IsNullOrWhiteSpace(startInfo.WorkingDirectory)
            ? Environment.CurrentDirectory
            : startInfo.WorkingDirectory;

        foreach (var reference in FinalLaunchCommandPathReader.Read(startInfo)
                     .Where(reference => reference.Category == "NativeDirectory"))
        {
            string? relativePath = null;
            try
            {
                var normalizedRoot = Path.GetFullPath(nativeRoot);
                var targetPath = Path.GetFullPath(reference.Path, workingDirectory);
                if (!preparedPaths.Add(targetPath)
                    || Directory.Exists(targetPath)
                    || !MinecraftPathGuard.IsWithin(targetPath, normalizedRoot))
                {
                    continue;
                }

                relativePath = Path.GetRelativePath(normalizedRoot, targetPath);
                MinecraftPathGuard.EnsureSafeDirectory(
                    targetPath,
                    normalizedRoot,
                    "Final launch native directory");
                logger.LogInformation(
                    "Prepared final launch native directory. VersionName={VersionName} NativeSubdirectory={NativeSubdirectory}",
                    versionName,
                    relativePath);
            }
            catch (Exception exception) when (exception is IOException
                                               or InvalidDataException
                                               or UnauthorizedAccessException
                                               or ArgumentException
                                               or NotSupportedException)
            {
                logger.LogWarning(
                    "Could not prepare final launch native directory; final validation will decide whether launch can continue. VersionName={VersionName} NativeSubdirectory={NativeSubdirectory} ErrorType={ErrorType}",
                    versionName,
                    relativePath ?? "<invalid>",
                    exception.GetType().Name);
            }
        }
    }
}
