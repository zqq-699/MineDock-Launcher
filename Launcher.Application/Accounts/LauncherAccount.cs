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

namespace Launcher.Application.Accounts;

public sealed partial class LauncherAccount : ObservableObject
{
    public const string DefaultSteveAvatarUrl = "https://minotar.net/avatar/Steve/32.png";

    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string? Uuid { get; init; }
    public OfflineUuidGenerationMode OfflineUuidGenerationMode { get; init; } = OfflineUuidGenerationMode.Standard;
    public string? AvatarSource { get; init; }
    public string? SkinSource { get; init; }
    public MinecraftSkinModel? SkinModel { get; init; }
    public IReadOnlyList<LauncherSkinRecord> SkinLibrary { get; init; } = [];
    public string? ActiveSkinId { get; init; }
    public bool IsOffline { get; init; }
    public bool HasFreshProfile { get; init; }
    public IReadOnlyList<AccountCapeOption> CachedCapeOptions { get; init; } = [];

    public string AvatarUrl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(AvatarSource))
                return AvatarSource;

            if (IsOffline || string.IsNullOrWhiteSpace(Uuid))
                return DefaultSteveAvatarUrl;

            return $"https://crafatar.com/avatars/{Uuid}?size=32&overlay";
        }
    }

    [ObservableProperty]
    private bool isSelected;
}
