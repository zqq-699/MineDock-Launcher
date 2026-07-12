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

namespace Launcher.Application;

public static class LauncherProjectLinks
{
    public const string GitHubOwner = "zqq-699";
    public const string GitHubRepositoryName = "BlockHelm-Launcher";
    public const string GitHubRepositoryUrl = "https://github.com/" + GitHubOwner + "/" + GitHubRepositoryName;
    public const string GitHubFeatureSuggestionsUrl = GitHubRepositoryUrl + "/discussions/categories/%E6%96%B0%E5%8A%9F%E8%83%BD%E5%BB%BA%E8%AE%AE";
    public const string GitHubIssuesUrl = GitHubRepositoryUrl + "/issues";
    public const string GitHubReleasesUrl = GitHubRepositoryUrl + "/releases";
    public const string GitHubReleasesApiUrl = "https://api.github.com/repos/" + GitHubOwner + "/" + GitHubRepositoryName + "/releases";
    public const string GitHubUserAgent = "BlockHelm-Launcher";
    public const string GiteeRepositoryUrl = "https://gitee.com/" + GitHubOwner + "/" + GitHubRepositoryName;
    public const string GiteeUpdateManifestUrlTemplate = GiteeRepositoryUrl + "/raw/update-manifests/update/{0}/latest.json";
    public const string GitHubUpdateManifestUrlTemplate = "https://raw.githubusercontent.com/" + GitHubOwner + "/" + GitHubRepositoryName + "/update-manifests/update/{0}/latest.json";
}
