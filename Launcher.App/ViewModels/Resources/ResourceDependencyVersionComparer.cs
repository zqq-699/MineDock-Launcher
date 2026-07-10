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

using System.Globalization;

namespace Launcher.App.ViewModels.Resources;

internal static class ResourceDependencyVersionComparer
{
    private static readonly string[] KnownContextTokens =
    [
        "mc",
        "minecraft",
        "fabric",
        "forge",
        "neoforge",
        "quilt"
    ];

    public static bool IsGreaterThanOrEqual(string installedVersion, string minimumVersion)
    {
        if (!TryParse(installedVersion, out var installed)
            || !TryParse(minimumVersion, out var minimum))
        {
            return false;
        }

        return installed.CompareTo(minimum) >= 0;
    }

    private static bool TryParse(string value, out ParsedDependencyVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var tokens = value
            .Trim()
            .Split(['+', '-', '_', ' ', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !IsContextToken(token))
            .ToList();
        var numericToken = tokens
            .LastOrDefault(token => char.IsDigit(token[0]) && token.Contains('.', StringComparison.Ordinal))
            ?? tokens.LastOrDefault(token => char.IsDigit(token[0]));
        if (numericToken is null)
            return false;

        var numbers = numericToken
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => new string(part.TakeWhile(char.IsDigit).ToArray()))
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => int.Parse(part, CultureInfo.InvariantCulture))
            .ToArray();
        if (numbers.Length == 0)
            return false;

        var qualifier = tokens.FirstOrDefault(token =>
            string.Equals(token, "alpha", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "beta", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "rc", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "pre", StringComparison.OrdinalIgnoreCase)
            || token.StartsWith("alpha.", StringComparison.OrdinalIgnoreCase)
            || token.StartsWith("beta.", StringComparison.OrdinalIgnoreCase)
            || token.StartsWith("rc.", StringComparison.OrdinalIgnoreCase)
            || token.StartsWith("pre.", StringComparison.OrdinalIgnoreCase));
        version = new ParsedDependencyVersion(numbers, ResolveQualifierWeight(qualifier));
        return true;
    }

    private static bool IsContextToken(string token)
    {
        if (KnownContextTokens.Any(context => string.Equals(token, context, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (token.StartsWith("mc", StringComparison.OrdinalIgnoreCase)
            && token.Skip(2).Any(char.IsDigit))
        {
            return true;
        }

        return token.Contains("minecraft", StringComparison.OrdinalIgnoreCase);
    }

    private static int ResolveQualifierWeight(string? qualifier)
    {
        if (string.IsNullOrWhiteSpace(qualifier))
            return 3;

        if (qualifier.StartsWith("alpha", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (qualifier.StartsWith("beta", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (qualifier.StartsWith("pre", StringComparison.OrdinalIgnoreCase)
            || qualifier.StartsWith("rc", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 3;
    }

    private readonly record struct ParsedDependencyVersion(
        IReadOnlyList<int> Numbers,
        int QualifierWeight) : IComparable<ParsedDependencyVersion>
    {
        public int CompareTo(ParsedDependencyVersion other)
        {
            var count = Math.Max(Numbers.Count, other.Numbers.Count);
            for (var index = 0; index < count; index++)
            {
                var left = index < Numbers.Count ? Numbers[index] : 0;
                var right = index < other.Numbers.Count ? other.Numbers[index] : 0;
                var comparison = left.CompareTo(right);
                if (comparison != 0)
                    return comparison;
            }

            return QualifierWeight.CompareTo(other.QualifierWeight);
        }
    }
}
