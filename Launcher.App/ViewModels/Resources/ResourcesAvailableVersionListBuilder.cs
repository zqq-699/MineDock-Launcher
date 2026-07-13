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

/// <summary>
/// 把资源版本按兼容性、Loader 和搜索条件投影为带分组标题的可增量 UI 列表。
/// </summary>
internal sealed partial class ResourcesAvailableVersionListBuilder
{
    // Builder 不持有页面状态，只封装多个资源页面必须一致使用的筛选、分组和插入规则。
    private readonly ResourcesOnlineProjectPageOptions options;

    public ResourcesAvailableVersionListBuilder(ResourcesOnlineProjectPageOptions options)
    {
        this.options = options;
    }

    public ResourcesFilterOptionItem CreateAllVersionFilterOption()
    {
        return new ResourcesFilterOptionItem { Id = "all", Title = options.AllVersionsText };
    }

    public List<ResourcesFilterOptionItem> CreateDefaultLoaderFilterOptions()
    {
        return
        [
            new ResourcesFilterOptionItem { Id = "all", Title = options.AllLoadersText }
        ];
    }

    public IReadOnlyList<ResourcesFilterOptionItem> CreateVersionFilterOptions(IReadOnlyList<ResourceProjectVersion> versions)
    {
        // 只展示数据中实际存在的兼容版本，并使用 Minecraft 版本语义而非普通字符串排序。
        return versions
            .SelectMany(version => NormalizeGameVersionCompatibilityValues(version.GameVersions))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(version => new ResourcesFilterOptionItem { Id = version, Title = version })
            .ToList();
    }

    public IReadOnlyList<ResourcesFilterOptionItem> CreateLoaderFilterOptions(IReadOnlyList<ResourceProjectVersion> versions)
    {
        // Loader 可能来自结构化字段或旧数据的版本名推断，统一规范化后再去重。
        var loaderOptions = CreateDefaultLoaderFilterOptions();
        if (!options.ShowsLoaderFilters)
            return loaderOptions;

        var loaderIds = versions
            .SelectMany(ResolveCompatibilityLoaders)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(loader => !string.Equals(loader, "all", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var loaderId in loaderIds)
            loaderOptions.Add(new ResourcesFilterOptionItem { Id = loaderId, Title = GetLoaderTitle(loaderId) });

        return loaderOptions
            .DistinctBy(option => option.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string ResolveDefaultVersionFilterId(ResourcesModInstallTargetItemViewModel? target)
    {
        if (target?.IsLocalDownload != false || IsUnknownInstanceVersionTarget(target))
            return "all";

        var minecraftVersion = target.Instance?.MinecraftVersion?.Trim();
        return string.IsNullOrWhiteSpace(minecraftVersion) ? "all" : minecraftVersion;
    }

    public string ResolveDefaultLoaderFilterId(ResourcesModInstallTargetItemViewModel? target)
    {
        if (target?.IsLocalDownload != false || IsUnknownInstanceVersionTarget(target) || target.Instance is null)
            return "all";

        return target.Instance.Loader switch
        {
            LoaderKind.Fabric => "fabric",
            LoaderKind.Forge => "forge",
            LoaderKind.NeoForge => "neoforge",
            LoaderKind.Quilt => "quilt",
            _ => "all"
        };
    }

    public string FormatTitle(ResourcesModInstallTargetItemViewModel? target)
    {
        if (target?.IsLocalDownload != false || IsUnknownInstanceVersionTarget(target))
            return options.VersionsAllTitleText;

        if (target.Instance is not { } instance)
            return options.VersionsAllTitleText;

        var minecraftVersion = instance.MinecraftVersion?.Trim();
        if (string.IsNullOrWhiteSpace(minecraftVersion))
            return options.VersionsAllTitleText;

        return options.ShowsLoaderFilters
            ? $"{minecraftVersion}-{GetLoaderId(instance.Loader)}"
            : minecraftVersion;
    }
}
