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

namespace Launcher.Application.Accounts;

public sealed class LauncherAccount
{
    public const string DefaultSteveAvatarUrl = "https://minotar.net/avatar/Steve/32.png";

    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public LauncherAccountKind Kind { get; init; } = LauncherAccountKind.Offline;
    public string? Uuid { get; init; }
    public string? AuthenticationServerUrl { get; init; }
    public string? ThirdPartyLoginUsername { get; init; }
    public OfflineUuidGenerationMode OfflineUuidGenerationMode { get; init; } = OfflineUuidGenerationMode.Standard;
    public string? AvatarSource { get; init; }
    public string? SkinSource { get; init; }
    public MinecraftSkinModel? SkinModel { get; init; }
    public IReadOnlyList<LauncherSkinRecord> SkinLibrary { get; init; } = [];
    public string? ActiveSkinId { get; init; }
    public bool IsOffline => Kind == LauncherAccountKind.Offline;
    public bool IsMicrosoft => Kind == LauncherAccountKind.Microsoft;
    public bool IsThirdParty => Kind == LauncherAccountKind.ThirdParty;
    public bool HasFreshProfile { get; init; }
    public IReadOnlyList<AccountCapeOption> CachedCapeOptions { get; init; } = [];

    public string AvatarUrl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(AvatarSource))
                return AvatarSource;

            if (!IsMicrosoft || string.IsNullOrWhiteSpace(Uuid))
                return DefaultSteveAvatarUrl;

            return $"https://crafatar.com/avatars/{Uuid}?size=32&overlay";
        }
    }
}
