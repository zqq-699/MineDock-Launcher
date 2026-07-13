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
