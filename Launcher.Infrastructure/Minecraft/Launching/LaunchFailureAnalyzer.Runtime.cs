/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using System.Text.RegularExpressions;
using Launcher.Application.Services;

namespace Launcher.Infrastructure.Minecraft;

internal static partial class LaunchFailureAnalyzer
{
private static LaunchFailureAnalysis? AnalyzeMissingFiles(
        LaunchDiagnosticContext context,
        string text)
    {
        var evidence = FindFirstMatchingLine(
            text,
            "Could not find or load main class",
            "Unable to access jarfile",
            "The system cannot find the file specified",
            "Invalid paths argument, contained no existing paths",
            "Missing launch target",
            "NoClassDefFoundError");
        if (evidence is null)
            return null;

        return FinalizeAnalysis(
            new LaunchFailureAnalysis(
                LaunchFailureCategory.MissingGameFiles,
                "missing_game_files",
                "missing_game_files",
                "repair_or_reinstall_instance"),
            context,
            evidence: [new(LaunchFailureEvidenceKind.Reason, evidence)]);
    }

    private static LaunchFailureAnalysis? AnalyzeMissingClientJar(
        LaunchDiagnosticContext context,
        string text)
    {
        var evidence = FindFirstMatchingLine(text, "missing its client jar", "missing client jar");
        if (evidence is null)
            return null;

        var jarPath = string.IsNullOrWhiteSpace(context.VersionName)
            ? null
            : Path.Combine(context.InstanceDirectory, $"{context.VersionName}.jar");
        return FinalizeAnalysis(
            new LaunchFailureAnalysis(
                LaunchFailureCategory.MissingGameFiles,
                "missing_game_files",
                "missing_client_jar",
                "repair_or_reinstall_instance",
                MissingPath: jarPath),
            context,
            evidence: [new(LaunchFailureEvidenceKind.Reason, evidence)]);
    }

    private static LaunchFailureAnalysis? AnalyzeMissingMinecraftGameProvider(
        LaunchDiagnosticContext context,
        string text)
    {
        var missingClasspathMatch = MissingClasspathPattern.Match(text);
        if (missingClasspathMatch.Success)
        {
            return FinalizeAnalysis(
                new LaunchFailureAnalysis(
                    LaunchFailureCategory.MissingGameFiles,
                    "missing_game_files",
                    "missing_classpath_entry",
                    "repair_or_reinstall_instance",
                    MissingPath: CleanMissingPath(missingClasspathMatch.Groups["path"].Value)),
                context,
                evidence: [new(LaunchFailureEvidenceKind.Reason, GetContainingLine(text, missingClasspathMatch.Index))]);
        }

        var evidence = FindFirstMatchingLine(
            text,
            "Minecraft game provider couldn't locate the game",
            "game provider couldn't locate the game");
        if (evidence is null)
            return null;

        return FinalizeAnalysis(
            new LaunchFailureAnalysis(
                LaunchFailureCategory.MissingGameFiles,
                "missing_game_files",
                "missing_game_provider",
                "repair_or_reinstall_instance"),
            context,
            evidence: [new(LaunchFailureEvidenceKind.Reason, evidence)]);
    }

    private static LaunchFailureAnalysis? AnalyzeOutOfMemory(
        LaunchDiagnosticContext context,
        string text)
    {
        var evidence = FindFirstMatchingLine(
            text,
            "OutOfMemoryError",
            "Java heap space",
            "GC overhead limit exceeded",
            "Could not reserve enough space",
            "insufficient memory");
        if (evidence is null)
            return null;

        return FinalizeAnalysis(
            new LaunchFailureAnalysis(
                LaunchFailureCategory.OutOfMemory,
                "out_of_memory",
                "out_of_memory",
                "increase_memory"),
            context,
            evidence: [new(LaunchFailureEvidenceKind.Reason, evidence)]);
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
}
