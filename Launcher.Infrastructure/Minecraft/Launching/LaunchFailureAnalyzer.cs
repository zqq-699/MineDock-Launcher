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

using System.IO;
using System.Text.RegularExpressions;
using Launcher.Application.Services;

namespace Launcher.Infrastructure.Minecraft;

internal static class LaunchFailureAnalyzer
{
    public static LaunchFailureAnalysis? Analyze(
        LaunchDiagnosticContext context,
        string stdout,
        string stderr,
        string latestLogTail,
        IReadOnlyList<string> crashFiles)
    {
        var crashPreview = string.Join(
            Environment.NewLine,
            crashFiles.Take(2).Select(ReadCrashFilePreview));

        var currentProcessText = string.Join(
            Environment.NewLine,
            stdout,
            stderr,
            crashPreview);

        var currentProcessAnalysis = AnalyzeText(context, currentProcessText);
        if (currentProcessAnalysis is not null)
            return currentProcessAnalysis;

        return AnalyzeText(context, latestLogTail);
    }

    private static LaunchFailureAnalysis? AnalyzeText(LaunchDiagnosticContext context, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return AnalyzeMissingClientJar(context, text)
               ?? AnalyzeMissingMinecraftGameProvider(text)
               ?? AnalyzeJavaVersionMismatch(text)
               ?? AnalyzeModDependencyMissing(text)
               ?? AnalyzeModVersionIncompatible(text)
               ?? AnalyzeMissingFiles(text)
               ?? AnalyzeOutOfMemory(text);
    }

    private static LaunchFailureAnalysis? AnalyzeJavaVersionMismatch(string text)
    {
        var modRequirementMatch = Regex.Match(
            text,
            @"Mod\s+'(?<mod>[^']+)'.*?requires\s+version\s+(?<required>\d+)\s+or\s+later.*?present:\s*(?<current>\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (modRequirementMatch.Success
            && TryGetInt(modRequirementMatch, "required", out var required)
            && TryGetInt(modRequirementMatch, "current", out var current))
        {
            return CreateJavaVersionMismatch(
                required,
                current,
                modRequirementMatch.Groups["mod"].Value);
        }

        var replaceMatch = Regex.Match(
            text,
            @"Replace\s+'[^']*'\s+\(java\)\s+(?<current>\d+)\s+with\s+version\s+(?<required>\d+)\s+or\s+later",
            RegexOptions.IgnoreCase);
        if (replaceMatch.Success
            && TryGetInt(replaceMatch, "required", out required)
            && TryGetInt(replaceMatch, "current", out current))
        {
            return CreateJavaVersionMismatch(required, current, null);
        }

        return null;
    }

    private static LaunchFailureAnalysis? AnalyzeModDependencyMissing(string text)
    {
        var missingDependencyMatch = Regex.Match(
            text,
            @"Mod\s+'(?<mod>[^']+)'.*?(?:requires|depends on).*?'(?<dependency>[^']+)'.*?(?:missing|not installed|not found)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (missingDependencyMatch.Success)
        {
            return new LaunchFailureAnalysis(
                LaunchFailureCategory.ModDependencyMissing,
                "mod_dependency_missing",
                "required_mod_dependency_missing",
                "install_missing_dependency",
                ModName: missingDependencyMatch.Groups["mod"].Value,
                DependencyName: missingDependencyMatch.Groups["dependency"].Value);
        }

        if (text.Contains("missing or unsupported mandatory dependencies", StringComparison.OrdinalIgnoreCase)
            || text.Contains("could not find required mod", StringComparison.OrdinalIgnoreCase))
        {
            return new LaunchFailureAnalysis(
                LaunchFailureCategory.ModDependencyMissing,
                "mod_dependency_missing",
                "required_mod_dependency_missing",
                "install_missing_dependency");
        }

        return null;
    }

    private static LaunchFailureAnalysis? AnalyzeModVersionIncompatible(string text)
    {
        if (!text.Contains("incompatible mods", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("mod resolution failed", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("mod loading has failed", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new LaunchFailureAnalysis(
            LaunchFailureCategory.ModVersionIncompatible,
            "mod_version_incompatible",
            "mod_version_incompatible",
            "check_mod_versions");
    }

    private static LaunchFailureAnalysis? AnalyzeMissingFiles(string text)
    {
        if (!text.Contains("Could not find or load main class", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("Unable to access jarfile", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("The system cannot find the file specified", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("Missing launch target", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("NoClassDefFoundError", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new LaunchFailureAnalysis(
            LaunchFailureCategory.MissingGameFiles,
            "missing_game_files",
            "missing_game_files",
            "repair_or_reinstall_instance");
    }

    private static LaunchFailureAnalysis? AnalyzeMissingClientJar(LaunchDiagnosticContext context, string text)
    {
        if (!text.Contains("missing its client jar", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("missing client jar", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var jarPath = string.IsNullOrWhiteSpace(context.VersionName)
            ? null
            : Path.Combine(context.InstanceDirectory, $"{context.VersionName}.jar");

        return new LaunchFailureAnalysis(
            LaunchFailureCategory.MissingGameFiles,
            "missing_game_files",
            "missing_client_jar",
            "repair_or_reinstall_instance",
            MissingPath: jarPath);
    }

    private static LaunchFailureAnalysis? AnalyzeMissingMinecraftGameProvider(string text)
    {
        var missingClasspathMatch = Regex.Match(
            text,
            @"Class path entries reference missing files:\s*(?<path>.+?)(?:\s+-\s+the game|\r|\n|$)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (missingClasspathMatch.Success)
        {
            return new LaunchFailureAnalysis(
                LaunchFailureCategory.MissingGameFiles,
                "missing_game_files",
                "missing_classpath_entry",
                "repair_or_reinstall_instance",
                MissingPath: CleanMissingPath(missingClasspathMatch.Groups["path"].Value));
        }

        if (!text.Contains("Minecraft game provider couldn't locate the game", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("game provider couldn't locate the game", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new LaunchFailureAnalysis(
            LaunchFailureCategory.MissingGameFiles,
            "missing_game_files",
            "missing_game_provider",
            "repair_or_reinstall_instance");
    }

    private static LaunchFailureAnalysis? AnalyzeOutOfMemory(string text)
    {
        if (!text.Contains("OutOfMemoryError", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("Java heap space", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("GC overhead limit exceeded", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("Could not reserve enough space", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("insufficient memory", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new LaunchFailureAnalysis(
            LaunchFailureCategory.OutOfMemory,
            "out_of_memory",
            "out_of_memory",
            "increase_memory");
    }

    private static LaunchFailureAnalysis CreateJavaVersionMismatch(
        int required,
        int current,
        string? modName)
    {
        return new LaunchFailureAnalysis(
            LaunchFailureCategory.JavaVersionMismatch,
            "java_version_mismatch",
            "java_version_mismatch",
            "select_required_java",
            RequiredJavaMajorVersion: required,
            CurrentJavaMajorVersion: current,
            ModName: string.IsNullOrWhiteSpace(modName) ? null : modName);
    }

    private static bool TryGetInt(Match match, string groupName, out int value)
    {
        return int.TryParse(match.Groups[groupName].Value, out value);
    }

    private static string? CleanMissingPath(string path)
    {
        var cleaned = path.Trim().Trim('"', '\'');
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static string ReadCrashFilePreview(string crashFile)
    {
        try
        {
            var lines = File.ReadAllLines(crashFile);
            return string.Join(Environment.NewLine, lines.Take(120));
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
