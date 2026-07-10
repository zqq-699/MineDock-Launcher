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

using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Resources;

public sealed partial class ResourcesProjectListViewModel : ObservableObject
{
    private readonly ResourcesOnlineProjectPageOptions options;

    public ResourcesProjectListViewModel(ResourcesOnlineProjectPageOptions options)
    {
        this.options = options;
    }

    [ObservableProperty]
    private int nextPageOffset;

    public ResourceCatalogSearchRequest CreateSearchRequest(
        string query,
        ResourcesFilterOptionItem? versionOption,
        ResourcesFilterOptionItem? loaderOption,
        ResourcesFilterOptionItem? sourceOption,
        ResourcesFilterOptionItem? typeOption,
        int offset,
        int pageSize)
    {
        var minecraftVersions = ResolveMinecraftVersions(versionOption);
        return new ResourceCatalogSearchRequest
        {
            Kind = options.Kind,
            Query = query,
            MinecraftVersion = minecraftVersions.Count == 1 ? minecraftVersions[0] : string.Empty,
            MinecraftVersions = minecraftVersions,
            Loader = !options.ShowsLoaderFilters ? LoaderKind.Vanilla : loaderOption?.Id switch
            {
                "fabric" => LoaderKind.Fabric,
                "forge" => LoaderKind.Forge,
                "neoforge" => LoaderKind.NeoForge,
                "quilt" => LoaderKind.Quilt,
                _ => LoaderKind.Vanilla
            },
            Source = sourceOption?.Id switch
            {
                "modrinth" => ResourceProjectSource.Modrinth,
                "curseforge" => ResourceProjectSource.CurseForge,
                _ => null
            },
            Category = ResolveCategory(typeOption),
            Offset = offset,
            PageSize = pageSize
        };
    }

    public void ResetPagination()
    {
        NextPageOffset = 0;
    }

    public void AdvancePagination(int resultOffset, int pageSize)
    {
        NextPageOffset = resultOffset + pageSize;
    }

    private static IReadOnlyList<string> ResolveMinecraftVersions(ResourcesFilterOptionItem? option)
    {
        if (option is null || option.Id == "all")
            return [];
        return option.MinecraftVersions.Count > 0 ? option.MinecraftVersions : [option.Id];
    }

    private ResourceProjectCategory? ResolveCategory(ResourcesFilterOptionItem? typeOption)
    {
        var selectedId = typeOption?.Id;
        if (string.IsNullOrWhiteSpace(selectedId)
            || string.Equals(selectedId, "all", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return options.TypeOptions.FirstOrDefault(option =>
            string.Equals(option.Id, selectedId, StringComparison.OrdinalIgnoreCase))?.Category;
    }
}
