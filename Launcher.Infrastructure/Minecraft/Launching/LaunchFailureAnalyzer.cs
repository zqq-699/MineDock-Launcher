/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using System.Text.RegularExpressions;
using Launcher.Application.Services;

namespace Launcher.Infrastructure.Minecraft;

internal static class LaunchFailureAnalyzer
{
    private const int MaxEvidenceCount = 24;
    private const int MaxEvidenceLineLength = 800;
    private const int MaxEvidenceTotalLength = 6000;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly Regex FabricDependencyPattern = new(
        @"Mod\s+'(?<mod>[^']+)'\s*(?:\([^)]+\))?\s*(?<modVersion>\S+)?\s+requires\s+(?<required>.+?)\s+of\s+(?:mod\s+)?(?:'(?<dependencyQuoted>[^']+)'(?:\s+\([^)]+\))?|(?<dependencyBare>[A-Za-z0-9_.-]+)),\s+(?:(?<missing>which\s+is\s+missing)|but\s+only\s+the\s+wrong\s+version\s+is\s+present:\s*(?<current>.+?))!?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex ForgeFailureLinePattern = new(
        @"^(?:Failure\s+message:\s*)?(?:Mod\s+)?(?<mod>[A-Za-z0-9_.-]+)\s+(?:requires|(?:only\s+)?supports)\s+(?<dependency>[A-Za-z0-9_.-]+)\s+(?<required>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex ForgeCurrentLinePattern = new(
        @"^Currently,\s*(?<dependency>[A-Za-z0-9_.-]+)\s+is\s+(?<current>[^|]+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex JavaRequirementPattern = new(
        @"Mod\s+'(?<mod>[^']+)'[^\r\n]*?requires\s+version\s+(?<required>\d+)\s+or\s+later\s+of\s+(?:'[^'\r\n]+'\s*)?\(java\),\s+but\s+only\s+the\s+wrong\s+version\s+is\s+present:\s*(?<current>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex JavaReplacementPattern = new(
        @"Replace\s+'[^']*'\s+\(java\)\s+(?<current>\d+)\s+with\s+version\s+(?<required>\d+)\s+or\s+later",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex SimpleMissingDependencyPattern = new(
        @"Mod\s+'(?<mod>[^']+)'[^\r\n]*?(?:requires|depends on)[^\r\n]*?'(?<dependency>[^']+)'[^\r\n]*?(?:missing|not installed|not found)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex MissingClasspathPattern = new(
        @"Class path entries reference missing files:\s*(?<path>.+?)(?:\s+-\s+the game|\r|\n|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex DependencyKindSeparatorPattern = new(
        @"[\s_-]",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex RequirementVersionPrefixPattern = new(
        @"^version\s+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex AnyVersionPattern = new(
        @"^any\s+(.+?)\s+version$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex StackFrameTailPattern = new(
        @"^\.\.\.\s+\d+\s+more$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex WhitespacePattern = new(
        @"\s+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    public static LaunchFailureAnalysis? Analyze(
        LaunchDiagnosticContext context,
        string stdout,
        string stderr,
        string latestLogTail,
        IReadOnlyList<string> crashPreviews)
    {
        var crashPreview = string.Join(
            Environment.NewLine,
            crashPreviews.Take(2));

        var currentProcessText = string.Join(
            Environment.NewLine,
            stdout,
            stderr,
            crashPreview);

        var currentProcessAnalysis = AnalyzeText(context, currentProcessText);
        var latestLogAnalysis = AnalyzeText(context, latestLogTail);
        return SelectBestAnalysis(currentProcessAnalysis, latestLogAnalysis);
    }

    private static LaunchFailureAnalysis? AnalyzeText(LaunchDiagnosticContext context, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return TryAnalyze(() => AnalyzeMissingClientJar(context, text))
               ?? TryAnalyze(() => AnalyzeMissingMinecraftGameProvider(context, text))
               ?? TryAnalyze(() => AnalyzeJavaVersionMismatch(context, text))
               ?? TryAnalyze(() => AnalyzeModCompatibility(context, text))
               ?? TryAnalyze(() => AnalyzeMissingFiles(context, text))
               ?? TryAnalyze(() => AnalyzeOutOfMemory(context, text));
    }

    private static LaunchFailureAnalysis? SelectBestAnalysis(
        LaunchFailureAnalysis? currentProcessAnalysis,
        LaunchFailureAnalysis? latestLogAnalysis)
    {
        if (currentProcessAnalysis is null)
            return latestLogAnalysis;
        if (latestLogAnalysis is null)
            return currentProcessAnalysis;

        if (currentProcessAnalysis.Category == latestLogAnalysis.Category)
        {
            var preferred = IsGenericAnalysis(currentProcessAnalysis)
                && !IsGenericAnalysis(latestLogAnalysis)
                ? latestLogAnalysis
                : currentProcessAnalysis;
            var secondary = ReferenceEquals(preferred, currentProcessAnalysis)
                ? latestLogAnalysis
                : currentProcessAnalysis;
            return MergeAnalyses(preferred, secondary);
        }

        return IsGenericAnalysis(currentProcessAnalysis) && !IsGenericAnalysis(latestLogAnalysis)
            ? latestLogAnalysis
            : currentProcessAnalysis;
    }

    private static LaunchFailureAnalysis MergeAnalyses(
        LaunchFailureAnalysis preferred,
        LaunchFailureAnalysis secondary)
    {
        var details = preferred.Details
            .Concat(secondary.Details)
            .DistinctBy(GetDetailKey)
            .ToArray();
        var evidence = new List<LaunchFailureEvidence>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var totalLength = 0;
        foreach (var item in preferred.Evidence.Concat(secondary.Evidence))
        {
            if (!seen.Add($"{item.Kind}:{item.Text}"))
                continue;
            if (evidence.Count >= MaxEvidenceCount || totalLength + item.Text.Length > MaxEvidenceTotalLength)
                continue;

            evidence.Add(item);
            totalLength += item.Text.Length;
        }

        return preferred with
        {
            ModName = preferred.ModName ?? secondary.ModName,
            DependencyName = preferred.DependencyName ?? secondary.DependencyName,
            Details = details,
            Evidence = evidence
        };
    }

    private static bool IsGenericAnalysis(LaunchFailureAnalysis analysis)
    {
        if (analysis.Details.Count > 0)
            return false;
        if (analysis.Evidence.Count == 0)
            return true;

        return analysis.Category == LaunchFailureCategory.ModVersionIncompatible
               && analysis.Evidence.All(evidence => IsGenericCompatibilityMarker(evidence.Text));
    }

    private static bool IsGenericCompatibilityMarker(string text)
    {
        var normalized = TrimBullet(text).TrimEnd('!', '.').Trim();
        return normalized.EndsWith("incompatible mods found", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith("incompatible mod set", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith("mod resolution failed", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith("mod loading has failed", StringComparison.OrdinalIgnoreCase);
    }

    private static LaunchFailureAnalysis? TryAnalyze(Func<LaunchFailureAnalysis?> analyzer)
    {
        try
        {
            return analyzer();
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

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

    private static LaunchFailureAnalysis? AnalyzeMissingFiles(
        LaunchDiagnosticContext context,
        string text)
    {
        var evidence = FindFirstMatchingLine(
            text,
            "Could not find or load main class",
            "Unable to access jarfile",
            "The system cannot find the file specified",
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

    private static LaunchFailureAnalysis FinalizeAnalysis(
        LaunchFailureAnalysis analysis,
        LaunchDiagnosticContext context,
        IEnumerable<LaunchFailureDetail>? details = null,
        IEnumerable<LaunchFailureEvidence>? evidence = null)
    {
        var sanitizedDetails = (details ?? [])
            .Select(detail => detail with
            {
                ModName = SanitizeValue(context, detail.ModName, 200),
                ModVersion = SanitizeValue(context, detail.ModVersion, 120),
                DependencyName = SanitizeValue(context, detail.DependencyName, 200),
                RequiredVersion = SanitizeValue(context, detail.RequiredVersion, 160),
                CurrentVersion = SanitizeValue(context, detail.CurrentVersion, 160),
                OriginalReason = SanitizeEvidenceLine(context, detail.OriginalReason),
                OriginalSuggestion = SanitizeEvidenceLine(context, detail.OriginalSuggestion)
            })
            .DistinctBy(GetDetailKey)
            .ToArray();
        var collectedEvidence = (evidence ?? [])
            .Concat(sanitizedDetails.SelectMany(detail => new[]
            {
                string.IsNullOrWhiteSpace(detail.OriginalReason)
                    ? null
                    : new LaunchFailureEvidence(LaunchFailureEvidenceKind.Reason, detail.OriginalReason),
                string.IsNullOrWhiteSpace(detail.OriginalSuggestion)
                    ? null
                    : new LaunchFailureEvidence(LaunchFailureEvidenceKind.Suggestion, detail.OriginalSuggestion)
            }).Where(item => item is not null).Cast<LaunchFailureEvidence>());
        var sanitizedEvidence = new List<LaunchFailureEvidence>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var totalLength = 0;
        foreach (var item in collectedEvidence)
        {
            var text = SanitizeEvidenceLine(context, item.Text);
            if (string.IsNullOrWhiteSpace(text) || !seen.Add($"{item.Kind}:{text}"))
                continue;
            if (totalLength + text.Length > MaxEvidenceTotalLength || sanitizedEvidence.Count >= MaxEvidenceCount)
                break;

            sanitizedEvidence.Add(item with { Text = text });
            totalLength += text.Length;
        }

        return analysis with
        {
            ModName = SanitizeValue(context, analysis.ModName, 200),
            DependencyName = SanitizeValue(context, analysis.DependencyName, 200),
            Details = sanitizedDetails,
            Evidence = sanitizedEvidence
        };
    }

    private static IReadOnlyList<string> ExtractSuggestionLines(string text)
    {
        var lines = SplitLines(text);
        var suggestions = new List<string>();
        var inSuggestions = false;
        foreach (var line in lines)
        {
            if (line.Contains("potential solution", StringComparison.OrdinalIgnoreCase))
            {
                inSuggestions = true;
                continue;
            }

            if (line.Contains("More details", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Unmet dependency listing", StringComparison.OrdinalIgnoreCase))
            {
                inSuggestions = false;
                continue;
            }

            if (inSuggestions && IsBulletLine(line))
                suggestions.Add(TrimBullet(line));
        }

        return suggestions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<string> ExtractHighSignalLines(string text)
    {
        return SplitLines(text)
            .Select(TrimBullet)
            .Where(line => !IsStackFrame(line)
                && (line.Contains("requires", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("missing", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("incompatible", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("currently", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("Install ", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("Replace ", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("Remove ", StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5);
    }

    private static string? FindRelatedSuggestion(
        IReadOnlyList<string> suggestions,
        string? dependencyName,
        string? modName)
    {
        return suggestions.FirstOrDefault(suggestion =>
                   !string.IsNullOrWhiteSpace(dependencyName)
                   && ContainsIdentifier(suggestion, dependencyName))
               ?? suggestions.FirstOrDefault(suggestion =>
                   !string.IsNullOrWhiteSpace(modName)
                   && ContainsIdentifier(suggestion, modName));
    }

    private static bool ContainsIdentifier(string text, string identifier)
    {
        var pattern = $@"(?<![A-Za-z0-9_.-]){Regex.Escape(identifier)}(?![A-Za-z0-9_.-])";
        return Regex.IsMatch(
            text,
            pattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            RegexTimeout);
    }

    private static LaunchFailureDetailKind ResolveDependencyDetailKind(string? dependencyName)
    {
        var normalizedDependency = DependencyKindSeparatorPattern
            .Replace(dependencyName ?? string.Empty, string.Empty)
            .ToLowerInvariant();
        if (normalizedDependency == "minecraft")
            return LaunchFailureDetailKind.IncompatibleMinecraftVersion;
        if (normalizedDependency is "fabricloader" or "quiltloader" or "forge" or "neoforge")
        {
            return LaunchFailureDetailKind.IncompatibleLoaderVersion;
        }

        return LaunchFailureDetailKind.IncompatibleDependencyVersion;
    }

    private static string NormalizeRequirement(string requirement)
    {
        var normalized = CleanTerminalPunctuation(requirement.Trim());
        normalized = RequirementVersionPrefixPattern.Replace(normalized, string.Empty);
        normalized = AnyVersionPattern.Replace(normalized, "$1");
        return normalized;
    }

    private static string CleanTerminalPunctuation(string value)
    {
        var markerIndex = value.IndexOf("Mod Issue URL:", StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
            value = value[..markerIndex];
        return value.Trim().TrimEnd('!', '.', '|').Trim();
    }

    private static string? FindFirstMatchingLine(string text, params string[] markers)
    {
        return SplitLines(text).FirstOrDefault(line => markers.Any(marker =>
            line.Contains(marker, StringComparison.OrdinalIgnoreCase)));
    }

    private static string GetContainingLine(string text, int index)
    {
        var start = text.LastIndexOfAny(['\r', '\n'], Math.Max(0, index - 1));
        var end = text.IndexOfAny(['\r', '\n'], index);
        start = start < 0 ? 0 : start + 1;
        end = end < 0 ? text.Length : end;
        return text[start..end].Trim();
    }

    private static IReadOnlyList<string> SplitLines(string text)
    {
        return text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool IsBulletLine(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("- ", StringComparison.Ordinal)
               || trimmed.StartsWith("* ", StringComparison.Ordinal);
    }

    private static string TrimBullet(string line)
    {
        var trimmed = line.Trim();
        return IsBulletLine(trimmed) ? trimmed[2..].Trim() : trimmed;
    }

    private static bool IsStackFrame(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("at ", StringComparison.Ordinal)
               || StackFrameTailPattern.IsMatch(trimmed);
    }

    private static string? SanitizeValue(LaunchDiagnosticContext context, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var redacted = LaunchDiagnosticRedactor.Redact(value.Trim(), context.SensitiveValues);
        return redacted.Length <= maxLength ? redacted : redacted[..maxLength] + "…";
    }

    private static string? SanitizeEvidenceLine(LaunchDiagnosticContext context, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = WhitespacePattern.Replace(value.Trim(), " ");
        if (IsStackFrame(normalized))
            return null;
        var redacted = LaunchDiagnosticRedactor.Redact(normalized, context.SensitiveValues);
        return redacted.Length <= MaxEvidenceLineLength
            ? redacted
            : redacted[..MaxEvidenceLineLength] + "…";
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

    private static string? CleanOptionalValue(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : CleanTerminalPunctuation(value);

    private static string NormalizeDetailKeyValue(string? value) =>
        value?.Trim().ToUpperInvariant() ?? string.Empty;

    private static (
        LaunchFailureDetailKind Kind,
        string ModName,
        string ModVersion,
        string DependencyName,
        string RequiredVersion,
        string CurrentVersion) GetDetailKey(LaunchFailureDetail detail) =>
        (
            detail.Kind,
            NormalizeDetailKeyValue(detail.ModName),
            NormalizeDetailKeyValue(detail.ModVersion),
            NormalizeDetailKeyValue(detail.DependencyName),
            NormalizeDetailKeyValue(detail.RequiredVersion),
            NormalizeDetailKeyValue(detail.CurrentVersion));

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}
