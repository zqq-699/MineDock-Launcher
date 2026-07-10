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

public sealed partial class ResourcesProjectVersionsViewModel : ObservableObject
{
    private readonly HashSet<string> loadedVersionIds = new(StringComparer.OrdinalIgnoreCase);

    internal ResourcesProjectVersionsViewModel(ResourcesOnlineProjectPageOptions options)
    {
        Builder = new ResourcesAvailableVersionListBuilder(options);
    }

    internal ResourcesAvailableVersionListBuilder Builder { get; }

    public IReadOnlyList<ResourceProjectVersion> SourceVersions { get; private set; } = [];

    public int NextPageOffset { get; private set; }

    public void SetSourceVersions(IReadOnlyList<ResourceProjectVersion> versions)
    {
        SourceVersions = versions.ToList();
        OnPropertyChanged(nameof(SourceVersions));
    }

    public void AppendSourceVersions(IReadOnlyList<ResourceProjectVersion> versions)
    {
        SourceVersions = SourceVersions.Concat(versions).ToList();
        OnPropertyChanged(nameof(SourceVersions));
    }

    public IReadOnlyList<ResourceProjectVersion> AcceptPage(
        IReadOnlyList<ResourceProjectVersion> versions,
        int requestOffset,
        int pageSize)
    {
        var accepted = new List<ResourceProjectVersion>(versions.Count);
        foreach (var version in versions)
        {
            var key = string.IsNullOrWhiteSpace(version.VersionId)
                ? $"{version.FileName}|{version.VersionNumber}|{version.PublishedAt:O}"
                : version.VersionId;
            if (!string.IsNullOrWhiteSpace(key) && loadedVersionIds.Add(key))
                accepted.Add(version);
        }
        NextPageOffset = requestOffset + pageSize;
        OnPropertyChanged(nameof(NextPageOffset));
        return accepted;
    }

    public void Reset()
    {
        SourceVersions = [];
        NextPageOffset = 0;
        loadedVersionIds.Clear();
        OnPropertyChanged(nameof(SourceVersions));
        OnPropertyChanged(nameof(NextPageOffset));
    }
}
