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
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using CmlLib.Core.ProcessBuilder;

namespace Launcher.Infrastructure.Minecraft;

internal enum AutoJoinServerAddressParseStatus
{
    Disabled,
    Invalid,
    Valid
}

internal sealed record ParsedAutoJoinServerAddress(string Host, int Port, bool IsIpv6)
{
    public string Endpoint => IsIpv6 ? $"[{Host}]:{Port}" : $"{Host}:{Port}";
}

internal static class AutoJoinServerAddressParser
{
    public static AutoJoinServerAddressParseStatus Parse(
        string? configuredAddress,
        out ParsedAutoJoinServerAddress? address)
    {
        address = null;
        if (string.IsNullOrWhiteSpace(configuredAddress))
            return AutoJoinServerAddressParseStatus.Disabled;

        var value = configuredAddress.Trim();
        if (!string.Equals(value, configuredAddress, StringComparison.Ordinal))
            return AutoJoinServerAddressParseStatus.Invalid;
        if (value.Any(character => char.IsWhiteSpace(character) || character is '"' or '\''))
            return AutoJoinServerAddressParseStatus.Invalid;

        string host;
        string portText;
        var isIpv6 = value.StartsWith("[", StringComparison.Ordinal);
        if (isIpv6)
        {
            var closingBracket = value.IndexOf(']');
            if (closingBracket <= 1
                || closingBracket + 1 >= value.Length
                || value[closingBracket + 1] != ':')
            {
                return AutoJoinServerAddressParseStatus.Invalid;
            }

            host = value[1..closingBracket];
            portText = value[(closingBracket + 2)..];
            if (!IPAddress.TryParse(host, out var ipAddress)
                || ipAddress.AddressFamily is not AddressFamily.InterNetworkV6)
            {
                return AutoJoinServerAddressParseStatus.Invalid;
            }

            host = ipAddress.ToString();
        }
        else
        {
            var separator = value.IndexOf(':');
            if (separator <= 0 || separator != value.LastIndexOf(':'))
                return AutoJoinServerAddressParseStatus.Invalid;

            host = value[..separator];
            portText = value[(separator + 1)..];
            if (!IsValidHost(host))
                return AutoJoinServerAddressParseStatus.Invalid;
        }

        if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || port is < 1 or > 65535)
        {
            return AutoJoinServerAddressParseStatus.Invalid;
        }

        address = new ParsedAutoJoinServerAddress(host, port, isIpv6);
        return AutoJoinServerAddressParseStatus.Valid;
    }

    private static bool IsValidHost(string host)
    {
        if (string.IsNullOrEmpty(host)
            || host.Contains('/')
            || host.Contains('\\')
            || host.Contains('[')
            || host.Contains(']'))
        {
            return false;
        }

        if (host.All(character => char.IsDigit(character) || character == '.'))
        {
            return IPAddress.TryParse(host, out var ipAddress)
                && ipAddress.AddressFamily is AddressFamily.InterNetwork;
        }

        return Uri.CheckHostName(host) is UriHostNameType.Dns;
    }
}

internal static class MinecraftQuickPlaySupport
{
    private static readonly Regex SnapshotVersionPattern = new(
        @"^(?<year>\d{2})w(?<week>\d{2})(?<revision>[a-z])$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex ReleaseVersionPattern = new(
        @"^(?<base>\d+\.\d+(?:\.\d+)?)(?:(?:-(?:pre|rc)\d+)|(?: (?:pre-release|release candidate) \d+))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static bool IsSupported(string? minecraftVersion)
    {
        if (string.IsNullOrWhiteSpace(minecraftVersion))
            return false;

        var value = minecraftVersion.Trim();
        var snapshotMatch = SnapshotVersionPattern.Match(value);
        if (snapshotMatch.Success)
        {
            var year = int.Parse(snapshotMatch.Groups["year"].Value, CultureInfo.InvariantCulture);
            var week = int.Parse(snapshotMatch.Groups["week"].Value, CultureInfo.InvariantCulture);
            var revision = char.ToLowerInvariant(snapshotMatch.Groups["revision"].Value[0]);
            return (year, week, revision).CompareTo((23, 14, 'a')) >= 0;
        }

        var releaseMatch = ReleaseVersionPattern.Match(value);
        if (!releaseMatch.Success)
            return false;

        var parts = releaseMatch.Groups["base"].Value.Split('.');
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor))
        {
            return false;
        }

        return major > 1 || major == 1 && minor >= 20;
    }
}

internal enum MinecraftAutoJoinArgumentStatus
{
    Disabled,
    InvalidAddress,
    Created
}

internal sealed record MinecraftAutoJoinArgumentResult(
    MinecraftAutoJoinArgumentStatus Status,
    MArgument? Argument,
    bool UsesQuickPlay);

internal static class MinecraftAutoJoinArgumentBuilder
{
    public static MinecraftAutoJoinArgumentResult Create(
        string? configuredAddress,
        string? minecraftVersion)
    {
        var status = AutoJoinServerAddressParser.Parse(configuredAddress, out var address);
        if (status is AutoJoinServerAddressParseStatus.Disabled)
            return new MinecraftAutoJoinArgumentResult(MinecraftAutoJoinArgumentStatus.Disabled, null, false);
        if (status is AutoJoinServerAddressParseStatus.Invalid || address is null)
            return new MinecraftAutoJoinArgumentResult(MinecraftAutoJoinArgumentStatus.InvalidAddress, null, false);

        var usesQuickPlay = MinecraftQuickPlaySupport.IsSupported(minecraftVersion);
        var values = usesQuickPlay
            ? new[] { "--quickPlayMultiplayer", address.Endpoint }
            : new[]
            {
                "--server",
                address.Host,
                "--port",
                address.Port.ToString(CultureInfo.InvariantCulture)
            };
        return new MinecraftAutoJoinArgumentResult(
            MinecraftAutoJoinArgumentStatus.Created,
            new MArgument(values),
            usesQuickPlay);
    }
}
