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

namespace Launcher.App.ViewModels.Download;

public sealed class DownloadAddonLibraryVersionOption
{
    public DownloadAddonLibraryVersionOption(string title, string? versionId, string versionNumber, bool isInstallable, bool isLatest, bool isStable)
    {
        Title = title;
        VersionId = versionId;
        VersionNumber = versionNumber;
        IsInstallable = isInstallable;
        IsLatest = isLatest;
        IsStable = isStable;
    }

    public string Title { get; }

    public string? VersionId { get; }

    public string VersionNumber { get; }

    public bool IsInstallable { get; }

    public bool IsLatest { get; }

    public bool IsStable { get; }

    public string TagText => !IsInstallable
        ? string.Empty
        : IsLatest
            ? Strings.Download_AddonLibraryLatestTag
            : IsStable
                ? Strings.Download_LoaderVersionStableTag
                : Strings.Download_LoaderVersionPreviewTag;

    public static DownloadAddonLibraryVersionOption None { get; } = new(
        Strings.Download_AddonLibraryNone,
        null,
        string.Empty,
        isInstallable: false,
        isLatest: false,
        isStable: true);

    public static DownloadAddonLibraryVersionOption FromVersion(ModrinthVersionInfo version, bool isLatest)
    {
        var title = string.IsNullOrWhiteSpace(version.VersionNumber)
            ? version.Name
            : version.VersionNumber;
        return new DownloadAddonLibraryVersionOption(
            title,
            version.VersionId,
            version.VersionNumber,
            isInstallable: true,
            isLatest,
            version.IsStable);
    }

    public override string ToString() => Title;
}
