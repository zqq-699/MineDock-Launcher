/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Text.RegularExpressions;

namespace Launcher.Infrastructure.Minecraft;

internal static class LaunchDiagnosticRedactor
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly Regex AccessTokenArgumentPattern = Create(
        @"(?i)(--?accessToken(?:=|\s+))(""[^""]+""|\S+)");
    private static readonly Regex SessionArgumentPattern = Create(
        @"(?i)(--?session(?:=|\s+))(""[^""]+""|\S+)");
    private static readonly Regex SecretArgumentPattern = Create(
        @"(?i)(--?(?:token|password|secret)(?:=|\s+))(""[^""]+""|\S+)");
    private static readonly Regex SecretAssignmentPattern = Create(
        @"(?i)\b((?:access[_-]?token|session|token|password|secret)\s*=\s*)(""[^""]+""|\S+)");

    public static string Redact(string text, IReadOnlyList<string> sensitiveValues)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        string redacted;
        try
        {
            redacted = AccessTokenArgumentPattern.Replace(text, "$1<redacted>");
            redacted = SessionArgumentPattern.Replace(redacted, "$1<redacted>");
            redacted = SecretArgumentPattern.Replace(redacted, "$1<redacted>");
            redacted = SecretAssignmentPattern.Replace(redacted, "$1<redacted>");
        }
        catch (RegexMatchTimeoutException)
        {
            return "<redacted: diagnostic text exceeded safe processing limits>";
        }

        foreach (var sensitiveValue in sensitiveValues)
        {
            if (string.IsNullOrWhiteSpace(sensitiveValue) || sensitiveValue.Length < 8)
                continue;

            redacted = redacted.Replace(sensitiveValue, "<redacted>", StringComparison.Ordinal);
        }

        return redacted;
    }

    private static Regex Create(string pattern) => new(
        pattern,
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);
}
