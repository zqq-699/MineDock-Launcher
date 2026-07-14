/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Infrastructure.Minecraft;

/// <summary>
/// Marks a staged loader that atomically publishes its verified shared content
/// to the real Minecraft directory itself. The modpack installer must then
/// publish only the private version directory.
/// </summary>
internal interface IDirectSharedContentStagedLoaderProvider
{
}
