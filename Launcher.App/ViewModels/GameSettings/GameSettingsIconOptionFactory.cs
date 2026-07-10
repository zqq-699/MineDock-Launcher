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
using Launcher.App.ViewModels.Shared;

namespace Launcher.App.ViewModels.GameSettings;

internal static class GameSettingsIconOptionFactory
{
    public static IReadOnlyList<GameSettingsIconOption> Create()
    {
        return
        [
            new GameSettingsIconOption(Strings.GameSettings_IconGrassBlock, MinecraftVersionIconResolver.DefaultReleaseIconSource),
            new GameSettingsIconOption(Strings.GameSettings_IconDirtBlock, MinecraftVersionIconResolver.DefaultSnapshotIconSource),
            new GameSettingsIconOption(Strings.GameSettings_IconCraftingTable, MinecraftVersionIconResolver.DefaultBetaIconSource),
            new GameSettingsIconOption(Strings.GameSettings_IconStoneBlock, MinecraftVersionIconResolver.DefaultAlphaIconSource),
            new GameSettingsIconOption(Strings.GameSettings_IconFabric, MinecraftVersionIconResolver.DefaultFabricIconSource),
            new GameSettingsIconOption(Strings.GameSettings_IconAnvil, MinecraftVersionIconResolver.DefaultForgeIconSource),
            new GameSettingsIconOption(Strings.GameSettings_IconNeoForge, MinecraftVersionIconResolver.DefaultNeoForgeIconSource),
            new GameSettingsIconOption(Strings.GameSettings_IconQuilt, MinecraftVersionIconResolver.DefaultQuiltIconSource),
            new GameSettingsIconOption(Strings.GameSettings_IconDiamondBlock, "/Assets/Icons/block/diamond_block.png"),
            new GameSettingsIconOption(Strings.GameSettings_IconBeacon, "/Assets/Icons/block/Beacon_block.png"),
            new GameSettingsIconOption(Strings.GameSettings_IconFurnace, "/Assets/Icons/block/Furnace_block.png"),
            new GameSettingsIconOption(Strings.GameSettings_IconTnt, "/Assets/Icons/block/TNT_block.png")
        ];
    }
}
