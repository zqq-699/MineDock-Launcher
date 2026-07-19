/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Application.Services;

public enum MultiplayerLobbyState
{
    Creating,
    Joining,
    Active,
    Stopping
}

public enum MultiplayerLobbyPlayerKind
{
    Host,
    Guest
}

public enum MultiplayerLobbyStopReason
{
    UserRequested,
    MinecraftWorldClosed,
    TerracottaExited,
    TerracottaServiceFailed
}

public enum MultiplayerLobbyCreationFailure
{
    TerracottaUnavailable,
    MinecraftWorldUnavailable,
    TerracottaStartupFailed,
    TerracottaBusy,
    TerracottaProtocolFailed,
    InvalidRoomCode,
    RoomConnectionFailed
}

public sealed record MultiplayerLobbyPlayer(
    string DisplayName,
    string MachineId,
    string Vendor,
    MultiplayerLobbyPlayerKind Kind,
    int? LatencyMilliseconds = null,
    bool IsLocal = false);

public sealed record MultiplayerLobbySnapshot(
    string RoomCode,
    MultiplayerLobbyState State,
    IReadOnlyList<MultiplayerLobbyPlayer> Players);

public sealed record MultiplayerLobbyStopped(
    MultiplayerLobbyStopReason Reason,
    Exception? Exception = null);

public sealed class MultiplayerLobbyCreationException : Exception
{
    public MultiplayerLobbyCreationException(
        MultiplayerLobbyCreationFailure failure,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }

    public MultiplayerLobbyCreationFailure Failure { get; }
}

public interface IMultiplayerLobbyService
{
    MultiplayerLobbySnapshot? Current { get; }

    event Action<MultiplayerLobbySnapshot>? SnapshotChanged;

    event Action<MultiplayerLobbyStopped>? Stopped;

    Task<MultiplayerLobbySnapshot> CreateHostAsync(
        string hostName,
        CancellationToken cancellationToken = default);

    Task<MultiplayerLobbySnapshot> JoinAsync(
        string roomCode,
        string playerName,
        CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
