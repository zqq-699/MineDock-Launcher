/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Infrastructure.Resources;

public interface IMcresBhlApiKeyResolver
{
    Task<string?> TryResolveAsync(CancellationToken cancellationToken = default);
}
