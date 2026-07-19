/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Resources;

namespace Launcher.App.ViewModels.Multiplayer;

public sealed record MultiplayerLobbyPlayerItem(
    string DisplayName,
    string Subtitle,
    string LatencyText,
    string Role,
    bool IsHost,
    bool IsLocal,
    bool IsFirst,
    bool IsLast)
{
    public IReadOnlyList<string> RoleTags => [Role];

    public IReadOnlyList<string> LocalTags => IsLocal
        ? [Strings.Multiplayer_LobbyPlayerRoleSelf]
        : [];
}
