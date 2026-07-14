/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Infrastructure.Minecraft;

/// <summary>Internal storage lifetime for a binary download.</summary>
internal enum DownloadPersistenceMode
{
    LightweightAtomic,
    TaskScopedResumable
}

internal sealed record DownloadFileOptions(
    DownloadPersistenceMode PersistenceMode = DownloadPersistenceMode.TaskScopedResumable,
    MinecraftDownloadOperationContext? OperationContext = null);
