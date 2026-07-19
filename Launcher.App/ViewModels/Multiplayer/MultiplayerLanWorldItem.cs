/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application.Services;

namespace Launcher.App.ViewModels.Multiplayer;

public sealed record MultiplayerLanWorldItem(
    MinecraftLanWorld World,
    string DisplayText)
{
    public override string ToString() => DisplayText;
}
