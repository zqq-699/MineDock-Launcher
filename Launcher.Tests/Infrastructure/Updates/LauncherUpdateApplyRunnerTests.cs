/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Diagnostics;
using Launcher.Infrastructure.Updates;

namespace Launcher.Tests.Infrastructure.Updates;

public sealed class LauncherUpdateApplyRunnerTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(
        Path.GetTempPath(),
        "launcher-update-apply-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void RunAtomicallyReplacesTargetAndCleansTransactionWithoutRestart()
    {
        var context = CreateContext();

        var exitCode = context.Runner.Run(context.Options with { Restart = false });

        Assert.Equal(0, exitCode);
        Assert.Equal("new", File.ReadAllText(context.Options.TargetPath));
        Assert.False(File.Exists(LauncherUpdateTransaction.GetBackupPath(context.Options.TargetPath)));
        Assert.False(File.Exists(LauncherUpdateTransaction.GetMarkerPath(context.Options.TargetPath)));
        Assert.Empty(context.Processes.StartedProcesses);
        Assert.NotEmpty(Directory.GetFiles(context.Options.LogDirectory, "updater-*.log"));
    }

    [Theory]
    [InlineData(UpdateFileFault.CopyCandidate)]
    [InlineData(UpdateFileFault.HashMismatch)]
    public void FailureBeforeAtomicCommitLeavesCompleteOldTarget(UpdateFileFault fault)
    {
        var context = CreateContext(fault);

        var exitCode = context.Runner.Run(context.Options);

        Assert.Equal(1, exitCode);
        Assert.Equal("old", File.ReadAllText(context.Options.TargetPath));
        Assert.False(File.Exists(LauncherUpdateTransaction.GetBackupPath(context.Options.TargetPath)));
        Assert.False(File.Exists(LauncherUpdateTransaction.GetMarkerPath(context.Options.TargetPath)));
        Assert.Empty(context.Processes.StartedProcesses);
    }

    [Fact]
    public void RollbackFailurePreservesBackupAndPendingMarker()
    {
        var context = CreateContext(UpdateFileFault.RollbackReplace, UpdateProcessFault.StartThrows);

        var exitCode = context.Runner.Run(context.Options);

        Assert.Equal(1, exitCode);
        Assert.Equal("new", File.ReadAllText(context.Options.TargetPath));
        Assert.True(File.Exists(LauncherUpdateTransaction.GetBackupPath(context.Options.TargetPath)));
        Assert.True(File.Exists(LauncherUpdateTransaction.GetMarkerPath(context.Options.TargetPath)));
    }

    [Fact]
    public void RecoveryAtomicallyRestoresBackupLeftAfterInterruptedCommit()
    {
        var context = CreateContext();
        var transaction = LauncherUpdateTransaction.Create(context.Options) with
        {
            Phase = LauncherUpdateTransactionPhase.Committed
        };
        context.Files.CopyCandidateAndFlush(context.Options.SourcePath, transaction.CandidatePath);
        context.Files.WriteTransaction(transaction with { Phase = LauncherUpdateTransactionPhase.Prepared });
        context.Files.Replace(transaction.CandidatePath, transaction.TargetPath, transaction.BackupPath);
        context.Files.WriteTransaction(transaction);

        var exitCode = context.Runner.RunRecovery(new LauncherUpdateRecoveryOptions(
            ProcessId: 0,
            TargetPath: context.Options.TargetPath,
            LogDirectory: context.Options.LogDirectory,
            Restart: true));

        Assert.Equal(0, exitCode);
        Assert.Equal("old", File.ReadAllText(context.Options.TargetPath));
        Assert.False(File.Exists(transaction.BackupPath));
        Assert.False(File.Exists(transaction.MarkerPath));
        Assert.Single(context.Processes.StartedProcesses);
    }

    [Fact]
    public void StartupCoordinatorStartsRecoveryForUnconfirmedInterruptedTransaction()
    {
        var context = CreateContext();
        var transaction = LauncherUpdateTransaction.Create(context.Options) with
        {
            Phase = LauncherUpdateTransactionPhase.Committed
        };
        context.Files.CopyCandidateAndFlush(context.Options.SourcePath, transaction.CandidatePath);
        context.Files.WriteTransaction(transaction with { Phase = LauncherUpdateTransactionPhase.Prepared });
        context.Files.Replace(transaction.CandidatePath, transaction.TargetPath, transaction.BackupPath);
        context.Files.WriteTransaction(transaction);

        ProcessStartInfo? recoveryStartInfo = null;
        var started = LauncherUpdateStartupCoordinator.TryStartPendingRecovery(
            [],
            context.Options.TargetPath,
            Environment.ProcessId,
            context.Files,
            startInfo =>
            {
                recoveryStartInfo = startInfo;
                return true;
            });

        Assert.True(started);
        Assert.NotNull(recoveryStartInfo);
        Assert.Equal(context.Options.SourcePath, recoveryStartInfo.FileName);
        Assert.Contains("--recover-update", recoveryStartInfo.ArgumentList);
        Assert.True(File.Exists(transaction.BackupPath));
        Assert.True(File.Exists(transaction.MarkerPath));
    }

    private TestContext CreateContext(
        UpdateFileFault fileFault = UpdateFileFault.None,
        UpdateProcessFault processFault = UpdateProcessFault.None)
    {
        Directory.CreateDirectory(tempRoot);
        var sourcePath = Path.Combine(tempRoot, "BlockHelm_Launcher_x64.exe");
        var targetPath = Path.Combine(tempRoot, "BlockHelm-Launcher.exe");
        var logDirectory = Path.Combine(tempRoot, "log");
        File.WriteAllText(sourcePath, "new");
        File.WriteAllText(targetPath, "old");
        var files = new FaultInjectingFileOperations(fileFault);
        var processes = new FakeProcessOperations(processFault);
        var runner = new LauncherUpdateApplyRunner(
            files,
            processes,
            TimeSpan.FromMilliseconds(2),
            TimeSpan.FromMilliseconds(1));
        var options = new LauncherUpdateApplyOptions(
            ProcessId: 0,
            SourcePath: sourcePath,
            TargetPath: targetPath,
            LogDirectory: logDirectory,
            Restart: true);
        return new TestContext(runner, options, files, processes);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }

    private sealed record TestContext(
        LauncherUpdateApplyRunner Runner,
        LauncherUpdateApplyOptions Options,
        FaultInjectingFileOperations Files,
        FakeProcessOperations Processes);

    public enum UpdateFileFault
    {
        None,
        CopyCandidate,
        FlushCandidate,
        HashMismatch,
        CommitReplace,
        RollbackReplace,
        ConfirmationWrite
    }

    public enum UpdateProcessFault
    {
        None,
        StartThrows,
        ExitsBeforeConfirmation,
        ConfirmationTimeout
    }

    private sealed class FaultInjectingFileOperations(UpdateFileFault fault) : ILauncherUpdateFileOperations
    {
        private readonly LauncherUpdateFileOperations inner = new();

        public bool Exists(string path) => inner.Exists(path);

        public void CopyCandidateAndFlush(string sourcePath, string candidatePath)
        {
            if (fault == UpdateFileFault.CopyCandidate)
                throw new IOException("Injected candidate copy failure.");
            inner.CopyCandidateAndFlush(sourcePath, candidatePath);
            if (fault == UpdateFileFault.FlushCandidate)
                throw new IOException("Injected candidate flush failure.");
        }

        public long GetLength(string path) => inner.GetLength(path);

        public byte[] ComputeSha256(string path)
        {
            var hash = inner.ComputeSha256(path);
            if (fault == UpdateFileFault.HashMismatch && path.EndsWith(".candidate", StringComparison.Ordinal))
                hash[0] ^= 0xff;
            return hash;
        }

        public void Replace(string sourcePath, string targetPath, string? destinationBackupPath)
        {
            if (fault == UpdateFileFault.CommitReplace && destinationBackupPath is not null)
                throw new IOException("Injected commit failure.");
            if (fault == UpdateFileFault.RollbackReplace
                && sourcePath.EndsWith(".update-backup", StringComparison.Ordinal))
                throw new IOException("Injected rollback failure.");
            inner.Replace(sourcePath, targetPath, destinationBackupPath);
        }

        public void WriteTransaction(LauncherUpdateTransaction transaction) => inner.WriteTransaction(transaction);

        public LauncherUpdateTransaction? ReadTransaction(string markerPath) => inner.ReadTransaction(markerPath);

        public string ReadAllText(string path) => inner.ReadAllText(path);

        public void WriteAllTextAndFlush(string path, string content)
        {
            if (fault == UpdateFileFault.ConfirmationWrite && path.EndsWith(".confirmed", StringComparison.Ordinal))
                throw new IOException("Injected startup confirmation write failure.");
            inner.WriteAllTextAndFlush(path, content);
        }

        public void Delete(string path) => inner.Delete(path);
    }

    private sealed class FakeProcessOperations(UpdateProcessFault fault) : ILauncherUpdateProcessOperations
    {
        public List<ProcessStartInfo> StartedProcesses { get; } = [];
        public Action<ProcessStartInfo>? OnStart { get; set; }
        public FakeUpdateProcess? FirstProcess { get; private set; }
        public int StartAttempts { get; private set; }

        public ILauncherUpdateProcess Start(ProcessStartInfo startInfo)
        {
            StartAttempts++;
            StartedProcesses.Add(startInfo);
            if (fault == UpdateProcessFault.StartThrows && StartAttempts == 1)
                throw new InvalidOperationException("Injected process start failure.");

            OnStart?.Invoke(startInfo);
            var process = new FakeUpdateProcess
            {
                HasExitedValue = fault == UpdateProcessFault.ExitsBeforeConfirmation && StartAttempts == 1
            };
            FirstProcess ??= process;
            return process;
        }

        public void Delay(TimeSpan delay)
        {
        }
    }

    private sealed class FakeUpdateProcess : ILauncherUpdateProcess
    {
        public bool HasExitedValue { get; set; }
        public bool WasKilled { get; private set; }
        public bool HasExited => HasExitedValue;

        public void Kill()
        {
            WasKilled = true;
            HasExitedValue = true;
        }

        public bool WaitForExit(int milliseconds) => HasExitedValue;

        public void Dispose()
        {
        }
    }
}
