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

using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.Services;

public sealed class MemoryAllocationCalculatorTests
{
    [Theory]
    [InlineData(LoaderKind.Vanilla, 0, 4096)]
    [InlineData(LoaderKind.Fabric, 0, 4096)]
    [InlineData(LoaderKind.Quilt, 30, 4608)]
    [InlineData(LoaderKind.Fabric, 31, 5120)]
    [InlineData(LoaderKind.Fabric, 81, 5632)]
    [InlineData(LoaderKind.Fabric, 151, 6144)]
    [InlineData(LoaderKind.Forge, 0, 4096)]
    [InlineData(LoaderKind.NeoForge, 30, 4608)]
    [InlineData(LoaderKind.Forge, 31, 5120)]
    [InlineData(LoaderKind.Forge, 81, 5632)]
    [InlineData(LoaderKind.Forge, 151, 6144)]
    public void AutomaticRequestedMemoryUsesLoaderAndModTiers(
        LoaderKind loader,
        int enabledModCount,
        int expectedMemoryMb)
    {
        Assert.Equal(
            expectedMemoryMb,
            MemoryAllocationCalculator.CalculateAutomaticRequestedMemoryMb(loader, enabledModCount));
    }

    [Fact]
    public void AutomaticMemoryIsCappedByCurrentAvailableMemory()
    {
        var snapshot = CreateSnapshot(totalMemoryGb: 16, availableMemoryGb: 4);

        var memoryMb = MemoryAllocationCalculator.CalculateAutomaticMemoryMb(
            snapshot,
            LoaderKind.Forge,
            enabledModCount: 151);

        Assert.Equal(3072, memoryMb);
    }

    [Fact]
    public void AutomaticMemoryIsCappedByTotalReservedMemory()
    {
        var snapshot = CreateSnapshot(totalMemoryGb: 6, availableMemoryGb: 6);

        var memoryMb = MemoryAllocationCalculator.CalculateAutomaticMemoryMb(
            snapshot,
            LoaderKind.Forge,
            enabledModCount: 151);

        Assert.Equal(4096, memoryMb);
    }

    [Fact]
    public void AutomaticSafetyMaximumUsesTotalMemoryRatio()
    {
        Assert.Equal(
            7680,
            MemoryAllocationCalculator.CalculateAutomaticSafetyMaximumMb(
                totalMemoryMb: 10240,
                availableMemoryMb: 12288));
    }

    private static SystemMemorySnapshot CreateSnapshot(int totalMemoryGb, int availableMemoryGb)
    {
        return new SystemMemorySnapshot(
            TotalMemoryBytes: totalMemoryGb * 1024L * 1024L * 1024L,
            AvailableMemoryBytes: availableMemoryGb * 1024L * 1024L * 1024L);
    }
}
