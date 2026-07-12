/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Text.RegularExpressions;

namespace Launcher.Infrastructure.Minecraft;

internal static class LaunchDiagnosticRedactor
{
    public static string Redact(string text, IReadOnlyList<string> sensitiveValues)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var redacted = Regex.Replace(
            text,
            @"(?i)(--?accessToken(?:=|\s+))(""[^""]+""|\S+)",
            "$1<redacted>");
        redacted = Regex.Replace(
            redacted,
            @"(?i)(--?session(?:=|\s+))(""[^""]+""|\S+)",
            "$1<redacted>");
        redacted = Regex.Replace(
            redacted,
            @"(?i)(--?(?:token|password|secret)(?:=|\s+))(""[^""]+""|\S+)",
            "$1<redacted>");
        redacted = Regex.Replace(
            redacted,
            @"(?i)\b((?:access[_-]?token|session|token|password|secret)\s*=\s*)(""[^""]+""|\S+)",
            "$1<redacted>");

        foreach (var sensitiveValue in sensitiveValues)
        {
            if (string.IsNullOrWhiteSpace(sensitiveValue) || sensitiveValue.Length < 8)
                continue;

            redacted = redacted.Replace(sensitiveValue, "<redacted>", StringComparison.Ordinal);
        }

        return redacted;
    }
}
