/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Modpacks;

internal static class ServerLog4ShellMitigation
{
    internal const string JvmArguments =
        "-Dlog4j2.formatMsgNoLookups=true -Dlog4j.configurationFile=log4j2.xml";

    private const string ResourcePrefix = "Launcher.Infrastructure.Modpacks.Log4Shell.";
    private static readonly DateTimeOffset Release17 = ParseBoundary("2013-10-22T15:04:05+00:00");
    private static readonly DateTimeOffset Release1112 = ParseBoundary("2016-12-21T09:29:12+00:00");
    private static readonly DateTimeOffset Release112 = ParseBoundary("2017-06-02T13:50:27+00:00");
    private static readonly DateTimeOffset Release1122 = ParseBoundary("2017-09-18T08:39:46+00:00");
    private static readonly DateTimeOffset Release113 = ParseBoundary("2018-07-18T15:11:46+00:00");
    private static readonly DateTimeOffset Release1163 = ParseBoundary("2020-09-10T13:42:37+00:00");
    private static readonly DateTimeOffset Release1181 = ParseBoundary("2021-12-10T08:23:00+00:00");

    public static string Apply(JsonObject versionJson, LoaderKind loader, string targetDirectory)
    {
        var releaseTime = GetReleaseTime(versionJson);
        if (!IsVulnerable(releaseTime))
            return string.Empty;

        WriteLoggingConfiguration(targetDirectory, SelectConfiguration(releaseTime, loader));
        if (loader is LoaderKind.Forge or LoaderKind.NeoForge)
            AppendArgumentsToForgeScriptConfiguration(targetDirectory);

        return JvmArguments;
    }

    internal static bool IsVulnerable(DateTimeOffset releaseTime) =>
        releaseTime >= Release17 && releaseTime < Release1181;

    internal static string SelectConfiguration(DateTimeOffset releaseTime, LoaderKind loader)
    {
        if (loader != LoaderKind.Forge)
            return releaseTime < Release1112 ? "vanilla-1.7.xml" : "vanilla-1.12.xml";

        if (releaseTime < Release112)
            return "forge-1.7.xml";
        if (releaseTime <= Release1122)
            return "forge-1.12.xml";
        if (releaseTime >= Release113 && releaseTime < Release1163)
            return "forge-1.13.xml";
        return "forge-1.16.4.xml";
    }

    private static DateTimeOffset GetReleaseTime(JsonObject versionJson)
    {
        var value = versionJson["releaseTime"] is JsonValue releaseTimeValue
            && releaseTimeValue.TryGetValue<string>(out var releaseTimeText)
                ? releaseTimeText
                : null;
        if (value is null
            || !DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var releaseTime))
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.InvalidManifest,
                "Minecraft version metadata is missing a valid release time.");
        }

        return releaseTime;
    }

    private static void WriteLoggingConfiguration(string targetDirectory, string resourceFileName)
    {
        var resourceName = ResourcePrefix + resourceFileName;
        using var source = typeof(ServerLog4ShellMitigation).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded Log4Shell mitigation resource: {resourceFileName}");
        using var destination = new FileStream(
            Path.Combine(targetDirectory, "log4j2.xml"),
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);
        source.CopyTo(destination);
    }

    private static void AppendArgumentsToForgeScriptConfiguration(string targetDirectory)
    {
        var path = Path.Combine(targetDirectory, "user_jvm_args.txt");
        if (!File.Exists(path))
            return;

        var existing = File.ReadAllText(path);
        if (existing.Contains(JvmArguments, StringComparison.Ordinal))
            return;

        var prefix = existing.Length == 0 || existing.EndsWith('\n') || existing.EndsWith('\r')
            ? string.Empty
            : Environment.NewLine;
        File.AppendAllText(path, prefix + JvmArguments + Environment.NewLine, new UTF8Encoding(false));
    }

    private static DateTimeOffset ParseBoundary(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
