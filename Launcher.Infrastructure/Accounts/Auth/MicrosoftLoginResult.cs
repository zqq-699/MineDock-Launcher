/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using CmlLib.Core.Auth.Microsoft.Sessions;

namespace Launcher.Infrastructure.Accounts;

internal sealed record MicrosoftLoginResult(
    JEProfile? Profile,
    string? Username,
    string? Uuid,
    string? AccessToken);
