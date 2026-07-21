/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Text.Json;
using Launcher.Application.Services;
using Launcher.Infrastructure.Multiplayer;

namespace Launcher.Tests.Infrastructure.Multiplayer;

public sealed class MultiplayerLobbyServiceTests
{
    [Fact]
    public void HostOkStateMapsRoomAndRealProfiles()
    {
        using var document = JsonDocument.Parse("""
            {
              "state":"host-ok",
              "room":"U/1234-5678-9ABC-DEFG",
              "profiles":[
                {"machine_id":"host-id","name":"Host","vendor":"Terracotta 0.4.2, EasyTier v2.5.0-terracotta.2","kind":"HOST"},
                {"machine_id":"guest-id","name":"Guest","vendor":"PCL CE 2.15.0-beta.6, EasyTier 2.5.0","kind":"GUEST"}
              ]
            }
            """);

        var state = MultiplayerLobbyService.ParseState(document.RootElement);

        Assert.Equal(MultiplayerLobbyService.TerracottaStateKind.HostOk, state.Kind);
        Assert.Equal("U/1234-5678-9ABC-DEFG", state.RoomCode);
        Assert.Collection(
            state.Players,
            player =>
            {
                Assert.Equal(MultiplayerLobbyPlayerKind.Host, player.Kind);
                Assert.Equal("Terracotta 0.4.2, EasyTier v2.5.0-terracotta.2", player.Vendor);
                Assert.True(player.IsLocal);
            },
            player =>
            {
                Assert.Equal(MultiplayerLobbyPlayerKind.Guest, player.Kind);
                Assert.Equal("PCL CE 2.15.0-beta.6, EasyTier 2.5.0", player.Vendor);
                Assert.False(player.IsLocal);
            });
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ExceptionStatePreservesTerracottaFailureType(int type)
    {
        using var document = JsonDocument.Parse($$"""
            {"state":"exception","type":{{type}}}
            """);

        var state = MultiplayerLobbyService.ParseState(document.RootElement);

        Assert.Equal(MultiplayerLobbyService.TerracottaStateKind.Exception, state.Kind);
        Assert.Equal(type, state.ExceptionType);
    }

}
