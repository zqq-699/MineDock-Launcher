/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Diagnostics;
using System.IO;
using Launcher.Application.Services;

namespace Launcher.Infrastructure.Minecraft;

internal sealed class LaunchSessionDiagnosticCollector
{
    private static readonly TimeSpan DefaultMaximumWait = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan DefaultStableDuration = TimeSpan.FromMilliseconds(300);

    private readonly string minecraftDirectory;
    private readonly string instanceDirectory;
    private readonly TimeSpan maximumWait;
    private readonly TimeSpan pollInterval;
    private readonly TimeSpan stableDuration;
    private readonly IReadOnlyDictionary<string, FileState> initialSnapshot;

    public LaunchSessionDiagnosticCollector(
        string minecraftDirectory,
        string instanceDirectory,
        TimeSpan? maximumWait = null,
        TimeSpan? pollInterval = null,
        TimeSpan? stableDuration = null)
    {
        this.minecraftDirectory = minecraftDirectory;
        this.instanceDirectory = instanceDirectory;
        this.maximumWait = maximumWait ?? DefaultMaximumWait;
        this.pollInterval = pollInterval ?? DefaultPollInterval;
        this.stableDuration = stableDuration ?? DefaultStableDuration;
        initialSnapshot = CaptureSnapshot();
    }

    public async Task<IReadOnlyList<LaunchDiagnosticReference>> CollectAsync(
        DateTimeOffset processStartedAt,
        string? capturedOutputPath,
        CancellationToken cancellationToken)
    {
        var finalSnapshot = await WaitForStableSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var candidates = new List<DiagnosticCandidate>();

        foreach (var state in finalSnapshot.Values)
        {
            if (state.Length <= 0 || !BelongsToCurrentSession(state, processStartedAt))
                continue;

            candidates.Add(new DiagnosticCandidate(state.Type, state.Path, state.LastWriteTimeUtc, state.Length));
        }

        if (TryReadFileState(capturedOutputPath, LaunchDiagnosticType.CapturedOutput, out var capturedState)
            && capturedState.Length > 0)
        {
            candidates.Add(new DiagnosticCandidate(
                capturedState.Type,
                capturedState.Path,
                capturedState.LastWriteTimeUtc,
                capturedState.Length));
        }

        return candidates
            .OrderBy(candidate => GetPriority(candidate.Type))
            .ThenByDescending(candidate => candidate.LastWriteTimeUtc)
            .ThenByDescending(candidate => candidate.Length)
            .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => new LaunchDiagnosticReference(candidate.Type, candidate.Path))
            .ToArray();
    }

    private async Task<IReadOnlyDictionary<string, FileState>> WaitForStableSnapshotAsync(
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var previous = CaptureSnapshot();
        var stableSince = stopwatch.Elapsed;

        while (stopwatch.Elapsed < maximumWait)
        {
            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
            var current = CaptureSnapshot();
            if (SnapshotsMatch(previous, current))
            {
                if (stopwatch.Elapsed - stableSince >= stableDuration)
                    return current;
            }
            else
            {
                previous = current;
                stableSince = stopwatch.Elapsed;
            }
        }

        return CaptureSnapshot();
    }

    private bool BelongsToCurrentSession(FileState current, DateTimeOffset processStartedAt)
    {
        if (!initialSnapshot.TryGetValue(current.Path, out var before))
            return current.LastWriteTimeUtc >= processStartedAt.UtcDateTime.AddSeconds(-1);

        return current.LastWriteTimeUtc != before.LastWriteTimeUtc || current.Length != before.Length;
    }

    private IReadOnlyDictionary<string, FileState> CaptureSnapshot()
    {
        var states = new Dictionary<string, FileState>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, type) in EnumerateCandidatePaths())
        {
            if (TryReadFileState(path, type, out var state))
                states[state.Path] = state;
        }

        return states;
    }

    private IEnumerable<(string Path, LaunchDiagnosticType Type)> EnumerateCandidatePaths()
    {
        foreach (var root in EnumerateRoots())
        {
            var crashReportsDirectory = Path.Combine(root, "crash-reports");
            foreach (var path in EnumerateFilesSafely(crashReportsDirectory, "*.txt"))
                yield return (path, LaunchDiagnosticType.MinecraftCrashReport);

            foreach (var path in EnumerateFilesSafely(root, "hs_err_pid*.log"))
                yield return (path, LaunchDiagnosticType.JvmCrashReport);
        }

        var latestLogPath = Path.Combine(instanceDirectory, "logs", "latest.log");
        if (File.Exists(latestLogPath))
            yield return (latestLogPath, LaunchDiagnosticType.MinecraftLatestLog);
    }

    private IEnumerable<string> EnumerateRoots()
    {
        return new[] { instanceDirectory, minecraftDirectory }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateFilesSafely(string directory, string pattern)
    {
        if (!Directory.Exists(directory))
            return [];

        try
        {
            return Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool TryReadFileState(
        string? path,
        LaunchDiagnosticType type,
        out FileState state)
    {
        state = default!;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        try
        {
            var file = new FileInfo(path);
            state = new FileState(
                Path.GetFullPath(path),
                type,
                file.Length,
                file.LastWriteTimeUtc);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool SnapshotsMatch(
        IReadOnlyDictionary<string, FileState> left,
        IReadOnlyDictionary<string, FileState> right)
    {
        if (left.Count != right.Count)
            return false;

        return left.All(pair => right.TryGetValue(pair.Key, out var state)
            && state.Length == pair.Value.Length
            && state.LastWriteTimeUtc == pair.Value.LastWriteTimeUtc
            && state.Type == pair.Value.Type);
    }

    private static int GetPriority(LaunchDiagnosticType type)
    {
        return type switch
        {
            LaunchDiagnosticType.MinecraftCrashReport => 0,
            LaunchDiagnosticType.JvmCrashReport => 1,
            LaunchDiagnosticType.MinecraftLatestLog => 2,
            LaunchDiagnosticType.CapturedOutput => 3,
            _ => 4
        };
    }

    private sealed record FileState(
        string Path,
        LaunchDiagnosticType Type,
        long Length,
        DateTime LastWriteTimeUtc);

    private sealed record DiagnosticCandidate(
        LaunchDiagnosticType Type,
        string Path,
        DateTime LastWriteTimeUtc,
        long Length);
}
