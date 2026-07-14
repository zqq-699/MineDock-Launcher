/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Infrastructure.Minecraft;

internal enum DownloadFileActivity
{
    ResolvingAddress,
    Downloading,
    Verifying,
    Publishing
}
