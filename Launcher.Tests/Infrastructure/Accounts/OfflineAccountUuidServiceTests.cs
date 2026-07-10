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
using Launcher.Infrastructure.Accounts;

namespace Launcher.Tests.Infrastructure.Accounts;

public sealed class OfflineAccountUuidServiceTests
{
    [Fact]
    public void CreateUuid_StandardUsesOfflinePlayerNameUuid()
    {
        var service = new OfflineAccountUuidService();

        var uuid = service.CreateUuid("Steve", OfflineUuidGenerationMode.Standard);

        Assert.Equal("5627dd98-e6be-3c21-b8a8-e92344183641", uuid);
    }

    [Fact]
    public void CreateUuid_RandomPreservesExistingUuid()
    {
        var service = new OfflineAccountUuidService();

        var uuid = service.CreateUuid(
            "Steve",
            OfflineUuidGenerationMode.Random,
            "00000000000000000000000000000001");

        Assert.Equal("00000000-0000-0000-0000-000000000001", uuid);
    }

    [Fact]
    public void CreateUuid_RandomGeneratesUuidWhenMissing()
    {
        var service = new OfflineAccountUuidService();

        var uuid = service.CreateUuid("Steve", OfflineUuidGenerationMode.Random);

        Assert.True(Guid.TryParse(uuid, out _));
    }

    [Fact]
    public void CreateUuid_ManualPreservesExistingUuid()
    {
        var service = new OfflineAccountUuidService();

        var uuid = service.CreateUuid(
            "Steve",
            OfflineUuidGenerationMode.Manual,
            "00000000000000000000000000000002");

        Assert.Equal("00000000-0000-0000-0000-000000000002", uuid);
    }

    [Fact]
    public void CreateUuid_ManualFallsBackToStandardWhenMissing()
    {
        var service = new OfflineAccountUuidService();

        var uuid = service.CreateUuid("Steve", OfflineUuidGenerationMode.Manual);

        Assert.Equal("5627dd98-e6be-3c21-b8a8-e92344183641", uuid);
    }

    [Fact]
    public void TryNormalizeUuid_AcceptsCompactAndHyphenatedUuid()
    {
        var service = new OfflineAccountUuidService();

        var compactResult = service.TryNormalizeUuid(
            "00000000000000000000000000000003",
            out var compactUuid);
        var hyphenatedResult = service.TryNormalizeUuid(
            "00000000-0000-0000-0000-000000000004",
            out var hyphenatedUuid);

        Assert.True(compactResult);
        Assert.Equal("00000000-0000-0000-0000-000000000003", compactUuid);
        Assert.True(hyphenatedResult);
        Assert.Equal("00000000-0000-0000-0000-000000000004", hyphenatedUuid);
    }

    [Fact]
    public void TryNormalizeUuid_RejectsInvalidUuid()
    {
        var service = new OfflineAccountUuidService();

        var result = service.TryNormalizeUuid("bad-uuid", out var uuid);

        Assert.False(result);
        Assert.Equal(string.Empty, uuid);
    }
}

