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
    private const string CredentialNamePattern =
        @"(?:access[_-]?token|client[_-]?token|token|session|password|secret|api[_-]?key)";
    private const string CredentialValuePattern = @"(?:""[^""]*""|'[^']*'|[^\s&,;}]+)";
    private static readonly Regex CredentialArgumentPattern = Create(
        $@"(?i)(?<![\w-])(--?{CredentialNamePattern}(?:=|\s+))({CredentialValuePattern})");
    private static readonly Regex CredentialAssignmentPattern = Create(
        $@"(?i)(?<![\w-])((?:""|')?{CredentialNamePattern}(?:""|')?\s*[:=]\s*)({CredentialValuePattern})");
    private static readonly Regex AuthorizationHeaderPattern = Create(
        @"(?i)\b((?:proxy-)?authorization\s*:\s*(?:bearer|basic)\s+)(\S+)");

    public static string Redact(string text, IReadOnlyList<string> sensitiveValues)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        string redacted;
        try
        {
            redacted = CredentialArgumentPattern.Replace(text, "$1<redacted>");
            redacted = CredentialAssignmentPattern.Replace(redacted, "$1<redacted>");
            redacted = AuthorizationHeaderPattern.Replace(redacted, "$1<redacted>");
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
