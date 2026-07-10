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

namespace Launcher.App.ViewModels.GameSettings;

internal static class GameSettingsDetailSectionFactory
{
    public static IReadOnlyList<GameSettingsDetailSectionItem> Create()
    {
        return
        [
            new GameSettingsDetailSectionItem("general", Strings.GameSettings_DetailGeneral, "instance_setting_page/general_setting"),
            new GameSettingsDetailSectionItem("launch", Strings.GameSettings_DetailLaunch, "instance_setting_page/launch"),
            new GameSettingsDetailSectionItem("java", Strings.GameSettings_DetailJava, "instance_setting_page/java"),
            new GameSettingsDetailSectionItem("mod_management", Strings.GameSettings_DetailModManagement, "instance_setting_page/mod"),
            new GameSettingsDetailSectionItem("saves", Strings.GameSettings_DetailSaves, "instance_setting_page/saves"),
            new GameSettingsDetailSectionItem("resource_packs", Strings.GameSettings_DetailResourcePacks, "main_menu_library"),
            new GameSettingsDetailSectionItem("shaders", Strings.GameSettings_DetailShaders, "instance_setting_page/shader"),
            new GameSettingsDetailSectionItem("backup", Strings.GameSettings_DetailBackup, "instance_setting_page/backup"),
            new GameSettingsDetailSectionItem("export", Strings.GameSettings_DetailExport, "instance_setting_page/export")
        ];
    }
}
