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
using Launcher.Infrastructure.Minecraft;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class MinecraftDownloadSourceResolverTests
{
    private const string VersionManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    [Fact]
    public void AutoPrefersBmclApiInChinaStandardTimeZone()
    {
        var candidates = Enumerate(
            DownloadSourcePreference.Auto,
            () => "China Standard Time");

        Assert.Collection(
            candidates,
            candidate => Assert.Equal("BmclApiMojang", candidate.ResolvedSourceKind),
            candidate => Assert.Equal("MojangOfficial", candidate.ResolvedSourceKind));
    }

    [Fact]
    public void AutoPrefersOfficialSourceOutsideChinaStandardTimeZone()
    {
        var candidates = Enumerate(
            DownloadSourcePreference.Auto,
            () => "AUS Eastern Standard Time");

        Assert.Collection(
            candidates,
            candidate => Assert.Equal("MojangOfficial", candidate.ResolvedSourceKind),
            candidate => Assert.Equal("BmclApiMojang", candidate.ResolvedSourceKind));
    }

    [Fact]
    public void AutoPrefersBmclApiWhenTimeZoneDetectionFails()
    {
        var candidates = Enumerate(
            DownloadSourcePreference.Auto,
            () => throw new InvalidOperationException("Time zone unavailable."));

        Assert.Collection(
            candidates,
            candidate => Assert.Equal("BmclApiMojang", candidate.ResolvedSourceKind),
            candidate => Assert.Equal("MojangOfficial", candidate.ResolvedSourceKind));
    }

    [Theory]
    [InlineData(DownloadSourcePreference.Official, "MojangOfficial")]
    [InlineData(DownloadSourcePreference.BmclApi, "BmclApiMojang")]
    public void ExplicitSourceIgnoresTimeZoneDetection(
        DownloadSourcePreference preference,
        string expectedSourceKind)
    {
        var candidates = Enumerate(
            preference,
            () => throw new InvalidOperationException("Time zone detection should not run."));

        var candidate = Assert.Single(candidates);
        Assert.Equal(expectedSourceKind, candidate.ResolvedSourceKind);
    }

    private static IReadOnlyList<ResolvedDownloadRequest> Enumerate(
        DownloadSourcePreference preference,
        Func<string> localTimeZoneIdProvider)
    {
        return MinecraftDownloadSourceResolver
            .EnumerateRequests(
                VersionManifestUrl,
                preference,
                categoryHint: "Mojang",
                localTimeZoneIdProvider: localTimeZoneIdProvider)
            .ToList();
    }
}
