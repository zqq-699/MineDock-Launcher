/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;

namespace Launcher.Infrastructure.Persistence;

internal static class PendingInstanceDeletionDirectory
{
    public const string Prefix = ".bhl-delete-pending-";

    public static bool IsPending(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
    }
}
