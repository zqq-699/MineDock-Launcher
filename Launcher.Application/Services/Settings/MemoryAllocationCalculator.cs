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

namespace Launcher.Application.Services;

public static class MemoryAllocationCalculator
{
    public const int MinimumMemoryMb = 1024;
    public const int ReservedSystemMemoryMb = 2048;
    public const int FallbackMaximumMemoryMb = 32768;
    public const double MaximumMemoryRatio = 0.75d;
    public const double RecordedMemoryPrecisionGb = 0.1d;
    public const int AutomaticSafetyReservedMemoryMb = 1024;

    public static int CalculateMaximumMemoryMb(int totalMemoryMb)
    {
        var reserveBasedMaximum = totalMemoryMb - ReservedSystemMemoryMb;
        var ratioBasedMaximum = (int)Math.Floor(totalMemoryMb * MaximumMemoryRatio);
        var rawMaximum = Math.Min(reserveBasedMaximum, ratioBasedMaximum);
        return Math.Max(MinimumMemoryMb, rawMaximum);
    }

    public static int CalculateAutomaticMemoryMb(SystemMemorySnapshot snapshot)
    {
        return CalculateAutomaticMemoryMb(snapshot, LoaderKind.Vanilla, enabledModCount: 0);
    }

    public static int CalculateAutomaticMemoryMb(
        SystemMemorySnapshot snapshot,
        LoaderKind loader,
        int enabledModCount)
    {
        var requestedMemoryMb = CalculateAutomaticRequestedMemoryMb(loader, enabledModCount);
        var safetyMaximumMb = CalculateAutomaticSafetyMaximumMb(
            BytesToMegabytes(snapshot.TotalMemoryBytes),
            BytesToMegabytes(snapshot.AvailableMemoryBytes));
        return Math.Clamp(requestedMemoryMb, MinimumMemoryMb, safetyMaximumMb);
    }

    public static int CalculateAutomaticRequestedMemoryMb(LoaderKind loader, int enabledModCount)
    {
        var baseMemoryMb = loader switch
        {
            LoaderKind.Fabric or LoaderKind.Quilt => 4 * 1024,
            LoaderKind.Forge or LoaderKind.NeoForge => 4 * 1024,
            _ => 4 * 1024
        };

        if (loader is LoaderKind.Vanilla)
            return baseMemoryMb;

        return baseMemoryMb + CalculateModMemoryAddonMb(enabledModCount);
    }

    public static int CalculateAutomaticSafetyMaximumMb(int totalMemoryMb, int availableMemoryMb)
    {
        var totalRatioMaximum = (int)Math.Floor(totalMemoryMb * MaximumMemoryRatio);
        var totalReservedMaximum = totalMemoryMb - ReservedSystemMemoryMb;
        var availableReservedMaximum = availableMemoryMb - AutomaticSafetyReservedMemoryMb;
        var rawMaximum = Math.Min(Math.Min(totalRatioMaximum, totalReservedMaximum), availableReservedMaximum);
        return Math.Max(MinimumMemoryMb, rawMaximum);
    }

    private static int CalculateModMemoryAddonMb(int enabledModCount)
    {
        if (enabledModCount <= 0)
            return 0;

        return enabledModCount switch
        {
            <= 30 => 512,
            <= 80 => 1024,
            <= 150 => 1536,
            _ => 2048
        };
    }

    public static int NormalizeRecordedMemoryMb(double memoryMb, int maximumMemoryMb)
    {
        var clamped = Math.Clamp(memoryMb, MinimumMemoryMb, maximumMemoryMb);
        var memoryGb = clamped / 1024d;
        var roundedGb = Math.Round(memoryGb / RecordedMemoryPrecisionGb, MidpointRounding.AwayFromZero)
            * RecordedMemoryPrecisionGb;
        var roundedMb = (int)Math.Round(roundedGb * 1024d, MidpointRounding.AwayFromZero);
        return Math.Clamp(roundedMb, MinimumMemoryMb, maximumMemoryMb);
    }

    public static int BytesToMegabytes(long bytes)
    {
        return (int)Math.Max(0, bytes / 1024L / 1024L);
    }
}
