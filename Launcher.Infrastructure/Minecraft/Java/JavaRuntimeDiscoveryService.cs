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

public sealed class JavaRuntimeDiscoveryService : IJavaRuntimeDiscoveryService
{
    private const int VersionProbeTimeoutMilliseconds = 1500;
    private const string UnknownArchitecture = "unknown";
    private static readonly Regex VersionRegex = new("\"(?<version>[0-9]+(?:\\.[0-9]+)*(?:[_+\\-][0-9A-Za-z.\\-]+)?)\"", RegexOptions.Compiled);

    public async Task<IReadOnlyList<JavaRuntimeInfo>> DiscoverAsync(
        string? minecraftDirectory,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(async () =>
        {
            var candidates = CollectCandidatePaths(minecraftDirectory);
            var runtimes = new List<JavaRuntimeInfo>();

            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                runtimes.Add(await CreateRuntimeInfoAsync(candidate, cancellationToken));
            }

            return (IReadOnlyList<JavaRuntimeInfo>)CollapseDuplicateRuntimes(runtimes)
                .OrderByDescending(runtime => runtime.MajorVersion ?? 0)
                .ThenBy(runtime => runtime.ExecutablePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, cancellationToken);
    }

    public async Task<JavaRuntimeInfo> DiscoverExecutableAsync(
        string executablePath,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(async () =>
        {
            var normalizedPath = NormalizePath(executablePath);
            if (!File.Exists(normalizedPath))
                throw new FileNotFoundException("Java executable was not found.", normalizedPath);

            return await CreateRuntimeInfoAsync(
                new JavaRuntimeCandidate(
                    normalizedPath,
                    "ManualImport",
                    NormalizePath(ResolveJavaExecutableIdentityPath(normalizedPath))),
                cancellationToken);
        }, cancellationToken);
    }

    internal static IReadOnlyList<JavaRuntimeCandidate> CollectCandidatePaths(
        string? minecraftDirectory,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? directoryExists = null,
        Func<string, string, SearchOption, IEnumerable<string>>? enumerateFiles = null,
        Func<string, string?>? getEnvironmentVariable = null,
        Func<string>? getProgramFiles = null,
        Func<string>? getProgramFilesX86 = null,
        Func<string, string>? resolveIdentityPath = null)
    {
        fileExists ??= File.Exists;
        directoryExists ??= Directory.Exists;
        enumerateFiles ??= EnumerateFilesSafely;
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        getProgramFiles ??= () => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        getProgramFilesX86 ??= () => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        resolveIdentityPath ??= ResolveJavaExecutableIdentityPath;

        var candidates = new List<JavaRuntimeCandidate>();
        var seenExecutablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var environmentVariable in new[] { "JAVA_HOME", "JDK_HOME", "JRE_HOME" })
        {
            var home = getEnvironmentVariable(environmentVariable);
            if (string.IsNullOrWhiteSpace(home))
                continue;

            AddCandidate(candidates, seenExecutablePaths, Path.Combine(home, "bin", "java.exe"), environmentVariable, fileExists, resolveIdentityPath);
        }

        var pathValue = getEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathValue))
        {
            foreach (var pathEntry in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                AddCandidate(candidates, seenExecutablePaths, Path.Combine(pathEntry, "java.exe"), "PATH", fileExists, resolveIdentityPath);
        }

        foreach (var root in GetCommonJavaRoots(getProgramFiles, getProgramFilesX86))
        {
            if (!directoryExists(root))
                continue;

            foreach (var executablePath in enumerateFiles(root, "java.exe", SearchOption.AllDirectories))
                AddCandidate(candidates, seenExecutablePaths, executablePath, "ProgramFiles", fileExists, resolveIdentityPath);
        }

        if (!string.IsNullOrWhiteSpace(minecraftDirectory))
        {
            var runtimeRoot = Path.Combine(minecraftDirectory, "runtime");
            if (directoryExists(runtimeRoot))
            {
                foreach (var executablePath in enumerateFiles(runtimeRoot, "java.exe", SearchOption.AllDirectories))
                    AddCandidate(candidates, seenExecutablePaths, executablePath, "MinecraftRuntime", fileExists, resolveIdentityPath);
            }
        }

        return CollapseDuplicateCandidates(candidates);
    }

    internal static JavaVersionProbeResult ParseVersionOutput(string output)
    {
        var version = VersionRegex.Match(output).Groups["version"].Value;
        if (string.IsNullOrWhiteSpace(version))
            return new JavaVersionProbeResult(null, null, UnknownArchitecture);

        return new JavaVersionProbeResult(
            version,
            ParseMajorVersion(version),
            ParseArchitecture(output));
    }

    internal static int? ParseMajorVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;

        var parts = version.Split(['.', '_', '+', '-'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2
            && parts[0] == "1"
            && int.TryParse(parts[1], out var legacyMajorVersion))
            return legacyMajorVersion;

        return int.TryParse(parts[0], out var majorVersion) ? majorVersion : null;
    }

    internal static IReadOnlyList<JavaRuntimeInfo> CollapseDuplicateRuntimes(IReadOnlyList<JavaRuntimeInfo> runtimes)
    {
        return runtimes
            .GroupBy(GetRuntimeIdentityKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(runtime => GetSourcePriority(runtime.Source))
                .ThenBy(runtime => runtime.ExecutablePath.Length)
                .First())
            .ToList();
    }

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

    private static async Task<JavaRuntimeInfo> CreateRuntimeInfoAsync(
        JavaRuntimeCandidate candidate,
        CancellationToken cancellationToken)
    {
        var probeResult = await ProbeVersionAsync(candidate.ExecutablePath, cancellationToken);
        var installationDirectory = GetInstallationDirectory(candidate.ExecutablePath);
        var displayName = probeResult.MajorVersion is int majorVersion
            ? $"Java {majorVersion}"
            : "Java";

        return new JavaRuntimeInfo(
            displayName,
            probeResult.Version,
            probeResult.MajorVersion,
            probeResult.Architecture,
            candidate.ExecutablePath,
            installationDirectory,
            candidate.Source);
    }

    private static async Task<JavaVersionProbeResult> ProbeVersionAsync(
        string executablePath,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var exitTask = process.WaitForExitAsync(cancellationToken);
            var completedTask = await Task.WhenAny(exitTask, Task.Delay(VersionProbeTimeoutMilliseconds, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();

            if (completedTask != exitTask)
            {
                TryKill(process);
                return new JavaVersionProbeResult(null, null, UnknownArchitecture);
            }

            var output = await outputTask;
            var error = await errorTask;
            return ParseVersionOutput(string.Concat(error, Environment.NewLine, output));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new JavaVersionProbeResult(null, null, UnknownArchitecture);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    private static string ParseArchitecture(string output)
    {
        if (output.Contains("64-Bit", StringComparison.OrdinalIgnoreCase)
            || output.Contains("x86_64", StringComparison.OrdinalIgnoreCase)
            || output.Contains("amd64", StringComparison.OrdinalIgnoreCase))
            return "x64";

        if (output.Contains("32-Bit", StringComparison.OrdinalIgnoreCase)
            || output.Contains("x86", StringComparison.OrdinalIgnoreCase))
            return "x86";

        return UnknownArchitecture;
    }

    private static string GetInstallationDirectory(string executablePath)
    {
        var binDirectory = Path.GetDirectoryName(executablePath);
        var installationDirectory = binDirectory is null ? null : Directory.GetParent(binDirectory);

        if (installationDirectory is not null
            && string.Equals(installationDirectory.Name, "jre", StringComparison.OrdinalIgnoreCase)
            && installationDirectory.Parent is not null)
            return installationDirectory.Parent.FullName;

        return installationDirectory?.FullName ?? string.Empty;
    }

    private static string GetRuntimeIdentityKey(JavaRuntimeInfo runtime)
    {
        var installationDirectory = NormalizePath(runtime.InstallationDirectory);
        if (!string.IsNullOrWhiteSpace(runtime.Version))
            return string.Join('|', installationDirectory, runtime.Version, runtime.Architecture);

        return NormalizePath(runtime.ExecutablePath);
    }

    private static int GetSourcePriority(string source)
    {
        return source.ToUpperInvariant() switch
        {
            "JAVA_HOME" => 0,
            "JDK_HOME" => 1,
            "JRE_HOME" => 2,
            "PROGRAMFILES" => 3,
            "MINECRAFTRUNTIME" => 4,
            "PATH" => 5,
            _ => 10
        };
    }
}

internal sealed record JavaRuntimeCandidate(string ExecutablePath, string Source, string IdentityPath);

internal sealed record JavaVersionProbeResult(string? Version, int? MajorVersion, string Architecture);
