/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Application.Accounts;

public sealed record AuthlibInjectorArtifact(
    string FilePath,
    string Version,
    int BuildNumber);
