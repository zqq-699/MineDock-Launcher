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

using System.Text.Json.Nodes;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Minecraft;

internal static class ManagedLibraryArtifactResolver
{
    public static IEnumerable<ManagedLibraryArtifact> EnumerateDownloads(JsonObject library)
    {
        if (library["downloads"] is JsonObject downloads)
        {
            if (downloads["artifact"] is JsonObject artifact)
            {
                var resolved = TryCreateArtifact(library, artifact);
                if (resolved is not null)
                    yield return resolved;
            }

            if (downloads["classifiers"] is JsonObject classifiers)
            {
                var classifierKey = ResolveNativeClassifierKey(library);
                if (!string.IsNullOrWhiteSpace(classifierKey)
                    && classifiers[classifierKey] is JsonObject classifierArtifact)
                {
                    var resolved = TryCreateArtifact(library, classifierArtifact, classifierKey);
                    if (resolved is not null)
                        yield return resolved;
                    yield break;
                }
            }
        }

        if (library["downloads"] is null)
        {
            var resolved = TryCreateArtifactFromName(library, classifier: null);
            if (resolved is not null)
                yield return resolved;

            var classifierKey = ResolveNativeClassifierKey(library);
            if (!string.IsNullOrWhiteSpace(classifierKey))
            {
                var classifierArtifact = TryCreateArtifactFromName(library, classifierKey);
                if (classifierArtifact is not null)
                    yield return classifierArtifact;
            }
        }
    }

    public static bool IsAllowed(JsonObject library)
    {
        if (library["rules"] is not JsonArray rules || rules.Count == 0)
            return true;

        var allowed = false;
        foreach (var ruleNode in rules)
        {
            if (ruleNode is not JsonObject rule || !DoesRuleMatch(rule))
                continue;

            var action = GetStringProperty(rule, "action");
            allowed = !string.Equals(action, "disallow", StringComparison.OrdinalIgnoreCase);
        }

        return allowed;
    }

    internal static bool TryBuildMavenPath(
        string mavenName,
        string? classifierOverride,
        out string relativePath)
    {
        relativePath = string.Empty;
        var parts = mavenName.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3 || parts.Length > 4)
            return false;

        var extension = "jar";
        var versionAndExtension = parts[2].Split('@', 2, StringSplitOptions.TrimEntries);
        var version = versionAndExtension[0];
        if (versionAndExtension.Length == 2 && !string.IsNullOrWhiteSpace(versionAndExtension[1]))
            extension = versionAndExtension[1];

        var classifier = classifierOverride;
        if (string.IsNullOrWhiteSpace(classifier) && parts.Length == 4)
        {
            var classifierAndExtension = parts[3].Split('@', 2, StringSplitOptions.TrimEntries);
            classifier = classifierAndExtension[0];
            if (classifierAndExtension.Length == 2 && !string.IsNullOrWhiteSpace(classifierAndExtension[1]))
                extension = classifierAndExtension[1];
        }

        var groupPath = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var fileName = string.IsNullOrWhiteSpace(classifier)
            ? $"{artifact}-{version}.{extension}"
            : $"{artifact}-{version}-{classifier}.{extension}";

        relativePath = $"{groupPath}/{artifact}/{version}/{fileName}";
        return true;
    }

    private static ManagedLibraryArtifact? TryCreateArtifact(
        JsonObject library,
        JsonObject artifact,
        string? classifier = null)
    {
        var libraryName = GetStringProperty(library, "name");
        var url = GetStringProperty(artifact, "url");
        var relativePath = GetStringProperty(artifact, "path");
        if (string.IsNullOrWhiteSpace(relativePath))
            relativePath = TryCreateArtifactFromName(library, classifier)?.RelativePath ?? string.Empty;

        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        if (string.IsNullOrWhiteSpace(url))
            url = TryResolveLibraryUrl(library, relativePath);

        if (string.IsNullOrWhiteSpace(url))
            return null;

        return new ManagedLibraryArtifact(
            url,
            relativePath,
            string.IsNullOrWhiteSpace(libraryName) ? null : libraryName,
            ResolveResourceCategory(url),
            GetStringProperty(artifact, "sha1"),
            GetLongProperty(artifact, "size"));
    }

    private static ManagedLibraryArtifact? TryCreateArtifactFromName(JsonObject library, string? classifier)
    {
        var name = GetStringProperty(library, "name");
        if (string.IsNullOrWhiteSpace(name)
            || !TryBuildMavenPath(name, classifier, out var relativePath))
        {
            return null;
        }

        var baseUrl = ResolveLibraryBaseUrl(library);
        if (string.IsNullOrWhiteSpace(baseUrl))
            return null;

        return new ManagedLibraryArtifact(
            new Uri(new Uri(baseUrl, UriKind.Absolute), relativePath).AbsoluteUri,
            relativePath,
            name,
            ResolveResourceCategory(baseUrl));
    }

    private static string? TryResolveLibraryUrl(JsonObject library, string relativePath)
    {
        var baseUrl = ResolveLibraryBaseUrl(library);
        return string.IsNullOrWhiteSpace(baseUrl)
            ? null
            : new Uri(new Uri(baseUrl, UriKind.Absolute), relativePath).AbsoluteUri;
    }

    private static string? ResolveLibraryBaseUrl(JsonObject library)
    {
        var baseUrl = GetStringProperty(library, "url");
        if (!string.IsNullOrWhiteSpace(baseUrl))
            return EnsureTrailingSlash(baseUrl);

        var name = GetStringProperty(library, "name");
        if (name.StartsWith("net.minecraftforge:", StringComparison.OrdinalIgnoreCase))
            return "https://maven.minecraftforge.net/";
        if (name.StartsWith("net.fabricmc:", StringComparison.OrdinalIgnoreCase))
            return "https://maven.fabricmc.net/";
        if (name.StartsWith("net.neoforged:", StringComparison.OrdinalIgnoreCase))
            return "https://maven.neoforged.net/releases/";
        if (name.StartsWith("org.quiltmc:", StringComparison.OrdinalIgnoreCase))
            return "https://maven.quiltmc.org/repository/release/";
        return "https://libraries.minecraft.net/";
    }

    private static string EnsureTrailingSlash(string baseUrl)
    {
        return baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : $"{baseUrl}/";
    }

    private static string ResolveResourceCategory(string url)
    {
        return MinecraftDownloadSourceResolver.ResolveRequest(
            url,
            DownloadSourcePreference.Official,
            useBmclApi: false).ResourceCategory;
    }

    private static string? ResolveNativeClassifierKey(JsonObject library)
    {
        if (library["natives"] is not JsonObject natives)
            return null;

        var classifier = GetStringProperty(natives, GetCurrentOsName());
        return string.IsNullOrWhiteSpace(classifier)
            ? null
            : classifier.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32", StringComparison.Ordinal);
    }

    private static bool DoesRuleMatch(JsonObject rule)
    {
        if (rule["features"] is JsonObject)
            return false;

        if (rule["os"] is not JsonObject os)
            return true;

        var name = GetStringProperty(os, "name");
        if (!string.IsNullOrWhiteSpace(name)
            && !string.Equals(name, GetCurrentOsName(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var arch = GetStringProperty(os, "arch");
        if (string.IsNullOrWhiteSpace(arch))
            return true;

        return Environment.Is64BitOperatingSystem
            ? arch.Contains("64", StringComparison.Ordinal)
            : !arch.Contains("64", StringComparison.Ordinal);
    }

    private static string GetCurrentOsName()
    {
        if (OperatingSystem.IsWindows())
            return "windows";
        if (OperatingSystem.IsMacOS())
            return "osx";
        return "linux";
    }

    private static string GetStringProperty(JsonObject node, string propertyName)
    {
        return node[propertyName]?.GetValue<string>() ?? string.Empty;
    }

    private static long? GetLongProperty(JsonObject node, string propertyName)
    {
        return node[propertyName]?.GetValue<long?>();
    }
}

internal sealed record ManagedLibraryArtifact(
    string Url,
    string RelativePath,
    string? LibraryName,
    string ResourceCategory,
    string? Sha1 = null,
    long? Size = null);
