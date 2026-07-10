/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Download;

internal sealed class DownloadInstanceNameTracker
{
    private readonly object syncRoot = new();
    private readonly HashSet<string> existingNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> pendingNames = new(StringComparer.OrdinalIgnoreCase);

    public void ReplaceExisting(IEnumerable<GameInstance> instances)
    {
        lock (syncRoot)
        {
            existingNames.Clear();
            foreach (var instance in instances)
            {
                AddNormalized(existingNames, instance.Name);
                AddNormalized(existingNames, instance.VersionName);
            }
        }
    }

    public void AddExisting(string? name)
    {
        var normalized = Normalize(name);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        lock (syncRoot)
        {
            existingNames.Add(normalized);
        }
    }

    public void AddPending(string? name)
    {
        var normalized = Normalize(name);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        lock (syncRoot)
        {
            pendingNames.Add(normalized);
        }
    }

    public void RemovePending(string? name)
    {
        var normalized = Normalize(name);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        lock (syncRoot)
        {
            pendingNames.Remove(normalized);
        }
    }

    public bool IsUnavailable(string? name)
    {
        var normalized = Normalize(name);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        lock (syncRoot)
        {
            return existingNames.Contains(normalized) || pendingNames.Contains(normalized);
        }
    }

    private static void AddNormalized(ISet<string> names, string? name)
    {
        var normalized = Normalize(name);
        if (!string.IsNullOrWhiteSpace(normalized))
            names.Add(normalized);
    }

    private static string Normalize(string? name)
    {
        return name?.Trim() ?? string.Empty;
    }
}

