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

namespace Launcher.Domain.Models;

public sealed class LauncherAccountRecord
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public LauncherAccountKind? Kind { get; set; }
    public string? Uuid { get; set; }
    public string? AuthenticationServerUrl { get; set; }
    public string? ThirdPartyLoginUsername { get; set; }
    public OfflineUuidGenerationMode OfflineUuidGenerationMode { get; set; } = OfflineUuidGenerationMode.Standard;
    public string? AvatarSource { get; set; }
    public string? SkinSource { get; set; }
    public MinecraftSkinModel? SkinModel { get; set; }
    public List<LauncherSkinRecord> Skins { get; set; } = [];
    public string? ActiveSkinId { get; set; }
    public bool IsOffline { get; set; } = true;
    public List<LauncherCapeRecord> Capes { get; set; } = [];
}
