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
}
