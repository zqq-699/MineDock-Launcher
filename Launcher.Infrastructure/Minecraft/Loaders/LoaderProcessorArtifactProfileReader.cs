/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Launcher.Infrastructure.Minecraft;

internal sealed record LoaderProcessorExpectedArtifact(string RelativePath, string? TrustedSha1);

/// <summary>
/// Reads the shared Forge/NeoForge installer profile format and resolves client-side generated artifacts.
/// </summary>
internal static class LoaderProcessorArtifactProfileReader
{
    private static readonly Regex PlaceholderRegex = new(
        "\\{(?<name>[A-Z0-9_]+)\\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> OutputArguments = new(StringComparer.OrdinalIgnoreCase)
    {
        "--output",
        "--slim",
        "--extra"
    };

    public static async Task<IReadOnlyList<LoaderProcessorExpectedArtifact>> ReadAsync(
        string installerJarPath,
        string loaderName,
        CancellationToken cancellationToken,
        Func<string, bool>? includeExternalLibrary = null)
    {
        await using var installerStream = new FileStream(
            installerJarPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(installerStream, ZipArchiveMode.Read, leaveOpen: false);
        var profileEntry = archive.GetEntry("install_profile.json")
            ?? throw new InvalidDataException($"{loaderName} installer does not contain install_profile.json.");
        await using var profileStream = profileEntry.Open();
        var profile = await JsonNode.ParseAsync(profileStream, cancellationToken: cancellationToken).ConfigureAwait(false) as JsonObject
            ?? throw new InvalidDataException($"{loaderName} install_profile.json is empty or invalid.");

        if (profile["processors"] is not JsonArray processors || profile["data"] is not JsonObject data)
            return [];

        var clientData = ReadClientData(data);
        var artifacts = new Dictionary<string, LoaderProcessorExpectedArtifact>(StringComparer.OrdinalIgnoreCase);
        foreach (var processor in processors.OfType<JsonObject>())
        {
            if (!RunsOnClient(processor))
                continue;

            if (processor["outputs"] is JsonObject outputs)
            {
                foreach (var output in outputs)
                    AddArtifact(artifacts, output.Key, output.Value?.GetValue<string>(), clientData);
            }

            if (processor["args"] is not JsonArray arguments)
                continue;

            for (var index = 0; index < arguments.Count - 1; index++)
            {
                var argument = arguments[index]?.GetValue<string>();
                if (argument is null || !OutputArguments.Contains(argument))
                    continue;
                AddArtifact(artifacts, arguments[index + 1]?.GetValue<string>(), hashExpression: null, clientData);
            }
        }

        AddInstallerRuntimeArtifacts(archive, profile, artifacts, includeExternalLibrary);

        return artifacts.Values
            .OrderBy(artifact => artifact.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddInstallerRuntimeArtifacts(
        ZipArchive archive,
        JsonObject profile,
        IDictionary<string, LoaderProcessorExpectedArtifact> artifacts,
        Func<string, bool>? includeExternalLibrary)
    {
        if (profile["libraries"] is not JsonArray libraries)
            return;

        var embeddedArtifacts = archive.Entries
            .Where(entry => entry.Length > 0 && entry.FullName.StartsWith("maven/", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.FullName["maven/".Length..].Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var library in libraries.OfType<JsonObject>())
        {
            if (library["downloads"]?["artifact"] is not JsonObject artifact)
                continue;

            var coordinate = library["name"]?.GetValue<string>()?.Trim();
            var path = artifact["path"]?.GetValue<string>()?.Trim().Replace('\\', '/');
            var sha1 = artifact["sha1"]?.GetValue<string>()?.Trim();
            if (!string.IsNullOrWhiteSpace(path)
                && IsSafeRelativePath(path)
                && MinecraftFileIntegrity.IsSha1(sha1)
                && (embeddedArtifacts.Contains(path)
                    || (!string.IsNullOrWhiteSpace(coordinate)
                        && includeExternalLibrary?.Invoke(coordinate) == true)))
            {
                artifacts[path] = new LoaderProcessorExpectedArtifact(path, sha1);
            }
        }
    }

    private static Dictionary<string, string> ReadClientData(JsonObject data)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in data)
        {
            if (pair.Value is JsonObject sides
                && sides["client"] is JsonValue client
                && client.TryGetValue<string>(out var value)
                && !string.IsNullOrWhiteSpace(value))
            {
                values[pair.Key] = value;
            }
        }
        return values;
    }

    private static bool RunsOnClient(JsonObject processor)
    {
        return processor["sides"] is not JsonArray sides
               || sides.Count == 0
               || sides.Any(side => string.Equals(side?.GetValue<string>(), "client", StringComparison.OrdinalIgnoreCase));
    }

    private static void AddArtifact(
        Dictionary<string, LoaderProcessorExpectedArtifact> artifacts,
        string? pathExpression,
        string? hashExpression,
        IReadOnlyDictionary<string, string> clientData)
    {
        if (string.IsNullOrWhiteSpace(pathExpression))
            return;

        var tokenName = TryReadSinglePlaceholder(pathExpression);
        var resolvedPath = ResolveExpression(pathExpression, clientData);
        if (resolvedPath.Length < 3 || resolvedPath[0] != '[' || resolvedPath[^1] != ']')
            return;

        var coordinate = resolvedPath[1..^1];
        if (!ManagedLibraryArtifactResolver.TryBuildMavenPath(coordinate, classifierOverride: null, out var relativePath)
            || !IsSafeRelativePath(relativePath))
        {
            return;
        }

        var resolvedHash = ResolveExpression(hashExpression, clientData);
        if (!MinecraftFileIntegrity.IsSha1(TrimLiteral(resolvedHash)) && tokenName is not null)
            clientData.TryGetValue($"{tokenName}_SHA", out resolvedHash);

        var trustedSha1 = TrimLiteral(resolvedHash);
        if (!MinecraftFileIntegrity.IsSha1(trustedSha1))
            trustedSha1 = null;

        if (!artifacts.TryGetValue(relativePath, out var existing)
            || (existing.TrustedSha1 is null && trustedSha1 is not null))
        {
            artifacts[relativePath] = new LoaderProcessorExpectedArtifact(relativePath.Replace('\\', '/'), trustedSha1);
        }
    }

    private static string ResolveExpression(string? expression, IReadOnlyDictionary<string, string> values)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return string.Empty;

        return PlaceholderRegex.Replace(
            expression,
            match => values.TryGetValue(match.Groups["name"].Value, out var value) ? value : match.Value);
    }

    private static string? TryReadSinglePlaceholder(string expression)
    {
        var trimmed = expression.Trim();
        var match = PlaceholderRegex.Match(trimmed);
        return match.Success && match.Index == 0 && match.Length == trimmed.Length
            ? match.Groups["name"].Value
            : null;
    }

    private static string? TrimLiteral(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Trim().Trim('\'', '"');
    }

    private static bool IsSafeRelativePath(string relativePath)
    {
        return !string.IsNullOrWhiteSpace(relativePath)
               && !Path.IsPathRooted(relativePath)
               && !relativePath.Split('/', '\\').Any(segment => segment is "" or "." or "..");
    }
}
