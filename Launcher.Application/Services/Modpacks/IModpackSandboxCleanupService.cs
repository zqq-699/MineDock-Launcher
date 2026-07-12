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

namespace Launcher.Application.Services;

public enum ModpackSandboxKind
{
    ModpackVersion,
    InstanceVersion
}

public interface IModpackSandboxSession : IAsyncDisposable
{
    string DirectoryPath { get; }

    Task CleanupAsync(bool deferCleanup);
}

public interface IModpackSandboxCleanupService
{
    IModpackSandboxSession CreateSession(ModpackSandboxKind kind);

    Task CleanupStaleAsync(CancellationToken cancellationToken = default);

    Task WaitForPendingCleanupAsync(CancellationToken cancellationToken = default);
}
