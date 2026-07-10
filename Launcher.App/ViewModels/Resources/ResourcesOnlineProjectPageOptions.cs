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

using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Resources;

public sealed record ResourcesOnlineProjectTypeOption(
    string Id,
    string Title,
    ResourceProjectCategory Category);

public sealed record ResourcesOnlineProjectPageOptions(
    ResourceProjectKind Kind,
    string Title,
    string FallbackIconKey,
    bool ShowsLoaderFilters,
    string AllVersionsText,
    string AllLoadersText,
    string ProjectsLoadingText,
    string ProjectsEmptyText,
    string ProjectsLoadErrorText,
    string ProjectsLoadingMoreText,
    string ProjectsNoMoreText,
    string ProjectsLoadMoreErrorText,
    string CurseForgeMissingApiKeyText,
    string DetailsInfoSectionText,
    string InstallTargetSectionText,
    string InstallTargetLocalText,
    string InstallTargetsLoadingText,
    string InstallTargetsLoadErrorText,
    string VersionsLoadingText,
    string VersionsEmptyText,
    string VersionsEmptyLocalText,
    string VersionsFilterEmptyText,
    string VersionsLoadErrorText,
    string VersionsLoadingMoreText,
    string VersionsNoMoreText,
    string VersionsLoadMoreErrorText,
    string VersionsAllTitleText,
    string DownloadDirectoryPickerTitle,
    string DownloadingText,
    string DownloadingFormat,
    string DownloadedFormat,
    string DownloadFailedText,
    string InstalledFormat,
    string InstallFailedText,
    string FileExistsMessageFormat,
    IReadOnlyList<ResourcesOnlineProjectTypeOption> TypeOptions,
    IReadOnlyList<ResourcesFilterOptionItem>? SourceOptions = null,
    ResourcesOnlineProjectInstallTargetMode InstallTargetMode = ResourcesOnlineProjectInstallTargetMode.ExistingInstance,
    string? InstallTargetNewInstanceText = null);
