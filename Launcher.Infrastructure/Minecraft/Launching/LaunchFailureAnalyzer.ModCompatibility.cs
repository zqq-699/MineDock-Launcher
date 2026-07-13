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
private static LaunchFailureAnalysis? AnalyzeJavaVersionMismatch(
        LaunchDiagnosticContext context,
        string text)
    {
        var modRequirementMatch = JavaRequirementPattern.Match(text);
        if (modRequirementMatch.Success
            && TryGetInt(modRequirementMatch, "required", out var required)
            && TryGetInt(modRequirementMatch, "current", out var current))
        {
            return FinalizeAnalysis(
                CreateJavaVersionMismatch(required, current, modRequirementMatch.Groups["mod"].Value),
                context,
                evidence: [new(LaunchFailureEvidenceKind.Reason, GetContainingLine(text, modRequirementMatch.Index))]);
        }

        var replaceMatch = JavaReplacementPattern.Match(text);
        if (replaceMatch.Success
            && TryGetInt(replaceMatch, "required", out required)
            && TryGetInt(replaceMatch, "current", out current))
        {
            return FinalizeAnalysis(
                CreateJavaVersionMismatch(required, current, null),
                context,
                evidence: [new(LaunchFailureEvidenceKind.Suggestion, GetContainingLine(text, replaceMatch.Index))]);
        }

        return null;
    }

    private static LaunchFailureAnalysis? AnalyzeModCompatibility(
        LaunchDiagnosticContext context,
        string text)
    {
        var suggestions = ExtractSuggestionLines(text);
        var details = ParseFabricDetails(text, suggestions)
            .Concat(ParseForgeDetails(text))
            .ToList();

        if (details.Count == 0)
        {
            foreach (var line in SplitLines(text))
            {
                var evidenceLine = TrimBullet(line);
                var simpleMissingDependency = SimpleMissingDependencyPattern.Match(evidenceLine);
                if (!simpleMissingDependency.Success)
                    continue;

                details.Add(new LaunchFailureDetail(
                    LaunchFailureDetailKind.MissingDependency,
                    ModName: simpleMissingDependency.Groups["mod"].Value,
                    DependencyName: simpleMissingDependency.Groups["dependency"].Value,
                    OriginalReason: evidenceLine,
                    OriginalSuggestion: FindRelatedSuggestion(
                        suggestions,
                        simpleMissingDependency.Groups["dependency"].Value,
                        simpleMissingDependency.Groups["mod"].Value)));
                break;
            }
        }

        var hasCompatibilityMarker = text.Contains("incompatible mods", StringComparison.OrdinalIgnoreCase)
                                     || text.Contains("incompatible mod set", StringComparison.OrdinalIgnoreCase)
                                     || text.Contains("mod resolution failed", StringComparison.OrdinalIgnoreCase)
                                     || text.Contains("mod loading has failed", StringComparison.OrdinalIgnoreCase)
                                     || text.Contains("missing or unsupported mandatory dependencies", StringComparison.OrdinalIgnoreCase)
                                     || text.Contains("could not find required mod", StringComparison.OrdinalIgnoreCase)
                                     || text.Contains("Failure message: Mod", StringComparison.OrdinalIgnoreCase);
        if (details.Count == 0 && !hasCompatibilityMarker)
            return null;

        var evidence = details
            .SelectMany(detail => new[]
            {
                string.IsNullOrWhiteSpace(detail.OriginalReason)
                    ? null
                    : new LaunchFailureEvidence(LaunchFailureEvidenceKind.Reason, detail.OriginalReason),
                string.IsNullOrWhiteSpace(detail.OriginalSuggestion)
                    ? null
                    : new LaunchFailureEvidence(LaunchFailureEvidenceKind.Suggestion, detail.OriginalSuggestion)
            })
            .Where(item => item is not null)
            .Cast<LaunchFailureEvidence>()
            .Concat(suggestions.Select(line => new LaunchFailureEvidence(LaunchFailureEvidenceKind.Suggestion, line)))
            .ToList();
        if (evidence.Count == 0)
        {
            evidence.AddRange(ExtractHighSignalLines(text)
                .Select(line => new LaunchFailureEvidence(LaunchFailureEvidenceKind.Reason, line)));
        }

        var missingDetail = details.FirstOrDefault(detail => detail.Kind == LaunchFailureDetailKind.MissingDependency);
        var primaryDetail = missingDetail ?? details.FirstOrDefault();
        var category = missingDetail is not null
            ? LaunchFailureCategory.ModDependencyMissing
            : LaunchFailureCategory.ModVersionIncompatible;
        var analysis = new LaunchFailureAnalysis(
            category,
            category == LaunchFailureCategory.ModDependencyMissing
                ? "mod_dependency_missing"
                : "mod_version_incompatible",
            category == LaunchFailureCategory.ModDependencyMissing
                ? "required_mod_dependency_missing"
                : "mod_version_incompatible",
            category == LaunchFailureCategory.ModDependencyMissing
                ? "install_missing_dependency"
                : "check_mod_versions",
            ModName: primaryDetail?.ModName,
            DependencyName: primaryDetail?.DependencyName);

        return FinalizeAnalysis(analysis, context, details, evidence);
    }

    private static IEnumerable<LaunchFailureDetail> ParseFabricDetails(
        string text,
        IReadOnlyList<string> suggestions)
    {
        foreach (var line in SplitLines(text))
        {
            var match = FabricDependencyPattern.Match(TrimBullet(line));
            if (!match.Success)
                continue;

            var dependency = FirstNonEmpty(
                match.Groups["dependencyQuoted"].Value,
                match.Groups["dependencyBare"].Value);
            var current = CleanTerminalPunctuation(match.Groups["current"].Value);
            var missing = match.Groups["missing"].Success;
            var kind = missing
                ? LaunchFailureDetailKind.MissingDependency
                : ResolveDependencyDetailKind(dependency);
            yield return new LaunchFailureDetail(
                kind,
                ModName: match.Groups["mod"].Value,
                ModVersion: CleanOptionalValue(match.Groups["modVersion"].Value),
                DependencyName: dependency,
                RequiredVersion: NormalizeRequirement(match.Groups["required"].Value),
                CurrentVersion: missing ? null : current,
                OriginalReason: TrimBullet(line),
                OriginalSuggestion: FindRelatedSuggestion(
                    suggestions,
                    dependency,
                    match.Groups["mod"].Value));
        }
    }

    private static IEnumerable<LaunchFailureDetail> ParseForgeDetails(string text)
    {
        var lines = SplitLines(text);
        for (var index = 0; index < lines.Count; index++)
        {
            var failureLine = TrimBullet(lines[index]);
            var failureMatch = ForgeFailureLinePattern.Match(failureLine);
            if (!failureMatch.Success)
                continue;

            Match? currentMatch = null;
            string? currentLine = null;
            for (var lookAhead = index + 1;
                 lookAhead < lines.Count && lookAhead <= index + 6;
                 lookAhead++)
            {
                var candidateLine = TrimBullet(lines[lookAhead]);
                if (ForgeFailureLinePattern.IsMatch(candidateLine))
                    break;

                var candidateMatch = ForgeCurrentLinePattern.Match(candidateLine);
                if (!candidateMatch.Success
                    || !string.Equals(
                        candidateMatch.Groups["dependency"].Value,
                        failureMatch.Groups["dependency"].Value,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                currentMatch = candidateMatch;
                currentLine = candidateLine;
                break;
            }

            if (currentMatch is null || currentLine is null)
                continue;

            var current = CleanTerminalPunctuation(currentMatch.Groups["current"].Value);
            var missing = current.StartsWith("not installed", StringComparison.OrdinalIgnoreCase);
            var dependency = failureMatch.Groups["dependency"].Value;
            yield return new LaunchFailureDetail(
                missing ? LaunchFailureDetailKind.MissingDependency : ResolveDependencyDetailKind(dependency),
                ModName: failureMatch.Groups["mod"].Value,
                DependencyName: dependency,
                RequiredVersion: NormalizeRequirement(failureMatch.Groups["required"].Value),
                CurrentVersion: missing ? null : current,
                OriginalReason: $"{failureLine} {currentLine}");
        }
    }
}
