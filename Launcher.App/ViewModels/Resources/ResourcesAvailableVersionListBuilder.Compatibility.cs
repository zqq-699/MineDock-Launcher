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

using Launcher.App.Resources;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Resources;

internal sealed partial class ResourcesAvailableVersionListBuilder
{
internal static IReadOnlyList<string> NormalizeGameVersionCompatibilityValues(IReadOnlyList<string> values)
    {
        var normalized = values
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Where(IsMinecraftVersionLike)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return normalized.Count == 0 ? [Strings.Resources_ModVersionsUnknown] : normalized;
    }

    internal static IReadOnlyList<string> ResolveCompatibilityLoaders(ResourceProjectVersion version)
    {
        // 优先信任 API 的结构化 Loader；字段缺失时才从版本名称进行保守推断。
        var loaders = NormalizeLoaderCompatibilityValues(version.Loaders);
        if (loaders.Count > 0)
            return loaders;

        loaders = NormalizeLoaderCompatibilityValues(version.GameVersions);
        if (loaders.Count > 0)
            return loaders;

        loaders = InferLoadersFromVersionText(version);
        return loaders.Count == 0 ? [Strings.Resources_ModLoadersUnknown] : loaders;
    }

    private static IReadOnlyList<string> NormalizeLoaderCompatibilityValues(IReadOnlyList<string> values)
    {
        return values
            .Select(TryNormalizeLoaderId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()!;
    }

    private static IReadOnlyList<string> InferLoadersFromVersionText(ResourceProjectVersion version)
    {
        // 文本推断只识别带边界的已知 token，避免把普通单词片段误判为 Loader。
        var text = string.Join(
            ' ',
            version.FileName,
            version.Name,
            version.VersionNumber);

        var loaders = new List<string>();
        AddLoaderIfFound(text, "neoforge", loaders);
        AddLoaderIfFound(text, "fabric", loaders);
        AddLoaderIfFound(text, "forge", loaders);
        AddLoaderIfFound(text, "quilt", loaders);
        return loaders;
    }

    private static void AddLoaderIfFound(string text, string loader, ICollection<string> loaders)
    {
        if (ContainsLoaderToken(text, loader)
            && !loaders.Contains(loader, StringComparer.OrdinalIgnoreCase))
        {
            loaders.Add(loader);
        }
    }

    private static bool ContainsLoaderToken(string text, string loader)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var index = text.IndexOf(loader, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var before = index == 0 ? '\0' : text[index - 1];
            var afterIndex = index + loader.Length;
            var after = afterIndex >= text.Length ? '\0' : text[afterIndex];
            if (!char.IsLetterOrDigit(before) && !char.IsLetterOrDigit(after))
                return true;

            index = text.IndexOf(loader, index + loader.Length, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsMinecraftVersionLike(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || !char.IsDigit(trimmed[0]))
            return false;

        return trimmed.All(character =>
            char.IsLetterOrDigit(character)
            || character is '.' or '-' or '_');
    }

    private static string? TryNormalizeLoaderId(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "fabric" => "fabric",
            "forge" => "forge",
            "neoforge" => "neoforge",
            "quilt" => "quilt",
            _ => null
        };
    }

    internal static bool IsUnknownInstanceVersionTarget(ResourcesModInstallTargetItemViewModel? target)
    {
        // 未知目标不能做严格兼容过滤，否则无法解析的旧实例会被错误显示为没有可用版本。
        return target?.IsLocalDownload is false
            && !target.IsNewInstanceInstall
            && !target.IsServerInstall
            && string.IsNullOrWhiteSpace(target.Instance?.MinecraftVersion);
    }

    private static string GetLoaderId(LoaderKind loader)
    {
        return loader switch
        {
            LoaderKind.Fabric => "fabric",
            LoaderKind.Forge => "forge",
            LoaderKind.NeoForge => "neoforge",
            LoaderKind.Quilt => "quilt",
            _ => "vanilla"
        };
    }

    private sealed class AvailableVersionCompatibilityGroup
    {
        public AvailableVersionCompatibilityGroup(string title)
        {
            Title = title;
        }

        public string Title { get; }

        public List<ResourceProjectVersion> Versions { get; } = [];
    }
}
