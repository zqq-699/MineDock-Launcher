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

using Launcher.Application.Services;
using Launcher.App.Resources;
using Launcher.App.Utilities;
using Launcher.Domain.Models;

namespace Launcher.App.Models;

internal static class NavigationCatalog
{
    public const string AccountPage = "Account";
    public const string HomePage = "Home";
    public const string DownloadPage = "Download";
    public const string InstallPage = "Install";
    public const string GameSettingsPage = "GameSettings";
    public const string ResourcesPage = "Resources";
    public const string SettingsPage = "Settings";

    public static NavigationItem CreateDownloadTasksItem()
    {
        return new NavigationItem
        {
            Page = InstallPage,
            Title = Strings.Page_Install,
            Icon = "\uE896",
            IconKey = "main_menu_install"
        };
    }

    public static IEnumerable<NavigationItem> CreatePrimaryItems()
    {
        return
        [
            new NavigationItem { Page = AccountPage, Title = Strings.Page_Account, Icon = "\uE77B", IconKey = "main_menu_account" },
            new NavigationItem { Page = HomePage, Title = Strings.Page_Home, Icon = "\uE80F", IconKey = "main_menu_launch" },
            new NavigationItem { Page = DownloadPage, Title = Strings.Page_Download, Icon = "\uE896", IconKey = "main_menu_instance_download" },
            new NavigationItem { Page = GameSettingsPage, Title = Strings.Page_GameSettings, Icon = "\uE713", IconKey = "main_menu_instance_setting" },
            new NavigationItem { Page = ResourcesPage, Title = Strings.Page_Resources, Icon = "\uE8F1", IconKey = "main_menu_library" },
            new NavigationItem { Page = SettingsPage, Title = Strings.Page_Settings, Icon = "\uE713", IconKey = "main_menu_setting" }
        ];
    }

    public static IEnumerable<NavigationItem> CreateSecondaryItems(string currentPage)
    {
        return currentPage switch
        {
            GameSettingsPage =>
            [
                new NavigationItem { Page = GameSettingsPage, Title = Strings.Nav_GameInstanceList, Icon = "\uE8A5" },
                new NavigationItem { Page = GameSettingsPage, Title = Strings.Nav_JavaMemory, Icon = "\uE950" },
                new NavigationItem { Page = GameSettingsPage, Title = Strings.Nav_DirectoryManagement, Icon = "\uE8B7" }
            ],
            ResourcesPage =>
            [
                new NavigationItem { Page = ResourcesPage, Title = Strings.Nav_Mod, Icon = "\uE8F1" },
                new NavigationItem { Page = ResourcesPage, Title = Strings.Nav_ResourcePacks, Icon = "\uE8A5" },
                new NavigationItem { Page = ResourcesPage, Title = Strings.Nav_ShaderPacks, Icon = "\uE790" },
                new NavigationItem { Page = ResourcesPage, Title = Strings.Nav_Worlds, Icon = "\uE707" },
                new NavigationItem { Page = ResourcesPage, Title = Strings.Nav_Modpacks, Icon = "\uE8F1" }
            ],
            SettingsPage =>
            [
                new NavigationItem { Page = SettingsPage, Title = Strings.Settings_SectionGeneral, Icon = "\uE713" },
                new NavigationItem { Page = SettingsPage, Title = Strings.Settings_SectionLaunchMemory, Icon = "\uE768" },
                new NavigationItem { Page = SettingsPage, Title = Strings.Settings_SectionJava, Icon = "\uE950" },
                new NavigationItem { Page = SettingsPage, Title = Strings.Settings_SectionTheme, Icon = "\uE790" },
                new NavigationItem { Page = SettingsPage, Title = Strings.Settings_SectionInfo, Icon = "\uE946" }
            ],
            _ => []
        };
    }

    public static NavigationItem CreateLoaderItem(ILoaderProvider provider)
    {
        return new NavigationItem
        {
            Page = provider.Kind.ToString(),
            Title = LoaderDisplayNameProvider.GetDisplayName(provider.Kind),
            Icon = provider.Kind is LoaderKind.Vanilla ? "\uE7C3" : "\uE8B7",
            Loader = provider.Kind
        };
    }

    public static bool IsPage(string? left, string? right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
