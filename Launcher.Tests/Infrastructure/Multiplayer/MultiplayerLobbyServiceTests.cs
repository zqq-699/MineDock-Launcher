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

    [Fact]
    public void GuestOkStateMarksOnlyLocalProfileAsLocalPlayer()
    {
        using var document = JsonDocument.Parse("""
            {
              "state":"guest-ok",
              "profiles":[
                {"machine_id":"host-id","name":"Same Name","vendor":"Terracotta","kind":"HOST"},
                {"machine_id":"local-id","name":"Same Name","vendor":"BlockHelm","kind":"LOCAL"},
                {"machine_id":"guest-id","name":"Guest","vendor":"HMCL","kind":"GUEST"}
              ]
            }
            """);

        var state = MultiplayerLobbyService.ParseState(document.RootElement);

        var localPlayer = Assert.Single(state.Players, player => player.IsLocal);
        Assert.Equal("local-id", localPlayer.MachineId);
        Assert.Equal(MultiplayerLobbyPlayerKind.Guest, localPlayer.Kind);
        Assert.False(state.Players[0].IsLocal);
        Assert.False(state.Players[2].IsLocal);
    }

    [Fact]
    public void ProfilesAreSanitizedBoundedAndDeduplicated()
    {
        using var document = JsonDocument.Parse("""
            {
              "state":"host-ok",
              "room":"U/ROOM",
              "profiles":[
                {"machine_id":"same","name":"A\u0000lice","vendor":"Terracotta","kind":"HOST"},
                {"machine_id":"same","name":"Duplicate","vendor":"HMCL","kind":"GUEST"},
                {"machine_id":"","name":"Invalid","vendor":"HMCL","kind":"GUEST"}
              ]
            }
            """);

        var player = Assert.Single(MultiplayerLobbyService.ParseState(document.RootElement).Players);

        Assert.Equal("Alice", player.DisplayName);
        Assert.Equal("same", player.MachineId);
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

    [Fact]
    public void ScanPathContainsOnlyPlayerAndNeverCustomNodes()
    {
        var path = MultiplayerLobbyService.BuildScanPath("Player Name");

        Assert.Equal("/state/scanning?player=Player%20Name", path);
        Assert.DoesNotContain("public_nodes", path, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GuestPathEscapesRoomAndPlayerWithoutCustomNodes()
    {
        var path = MultiplayerLobbyService.BuildGuestPath("U/ROOM CODE", "Player Name");

        Assert.Equal("/state/guesting?room=U%2FROOM%20CODE&player=Player%20Name", path);
        Assert.DoesNotContain("public_nodes", path, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", path, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("guest-connecting", "GuestConnecting")]
    [InlineData("guest-starting", "GuestStarting")]
    [InlineData("guest-ok", "GuestOk")]
    public void GuestStatesAreRecognized(string stateName, string expected)
    {
        using var document = JsonDocument.Parse($$"""{"state":"{{stateName}}"}""");

        Assert.Equal(expected, MultiplayerLobbyService.ParseState(document.RootElement).Kind.ToString());
    }

    [Fact]
    public void InvalidStateResponseIsRejected()
    {
        using var document = JsonDocument.Parse("{\"profiles\":[]}");

        Assert.Throws<InvalidDataException>(() =>
            MultiplayerLobbyService.ParseState(document.RootElement));
    }

    [Fact]
    public async Task HandoffProcessThatExitsIsClassifiedAsExternalInstance()
    {
        var owned = await MultiplayerLobbyService.ClassifyHandoffProcessOwnershipAsync(
            () => false,
            _ => Task.CompletedTask,
            TimeSpan.FromMilliseconds(10));

        Assert.False(owned);
    }

    [Fact]
    public async Task HandoffProcessThatRemainsAliveIsClassifiedAsOwned()
    {
        var neverExits = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var owned = await MultiplayerLobbyService.ClassifyHandoffProcessOwnershipAsync(
            () => false,
            _ => neverExits.Task,
            TimeSpan.FromMilliseconds(10));

        Assert.True(owned);
    }
}
