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
using System.IO;

namespace Launcher.Infrastructure.Updates;

public static class LauncherUpdateStartupCoordinator
{
    internal const string ConfirmationArgument = "--confirm-update";

    public static bool TryStartPendingRecovery(
        string[] args,
        string? currentExecutablePath,
        int currentProcessId)
    {
        var files = new LauncherUpdateFileOperations();
        return TryStartPendingRecovery(
            args,
            currentExecutablePath,
            currentProcessId,
            files,
            startInfo =>
            {
                using var process = Process.Start(startInfo);
                return process is not null;
            });
    }

    internal static bool TryStartPendingRecovery(
        string[] args,
        string? currentExecutablePath,
        int currentProcessId,
        ILauncherUpdateFileOperations files,
        Func<ProcessStartInfo, bool> startProcess)
    {
        if (TryGetConfirmationId(args, out _) || string.IsNullOrWhiteSpace(currentExecutablePath))
            return false;

        var markerPath = LauncherUpdateTransaction.GetMarkerPath(currentExecutablePath);
        var transaction = files.ReadTransaction(markerPath);
        if (transaction is null)
            return false;
        if (!string.Equals(
                Path.GetFullPath(transaction.TargetPath),
                Path.GetFullPath(currentExecutablePath),
                StringComparison.OrdinalIgnoreCase)
            || !transaction.HasValidDerivedPaths())
        {
            throw new InvalidOperationException("Pending launcher update transaction is invalid.");
        }

        if (IsConfirmed(files, transaction))
        {
            TryDelete(files, transaction.BackupPath);
            TryDelete(files, transaction.CandidatePath);
            TryDelete(files, transaction.ConfirmationPath);
            TryDelete(files, transaction.MarkerPath);
            return false;
        }

        if (!files.Exists(transaction.BackupPath))
        {
            TryDelete(files, transaction.CandidatePath);
            TryDelete(files, transaction.ConfirmationPath);
            TryDelete(files, transaction.MarkerPath);
            return false;
        }

        if (!files.Exists(transaction.UpdaterPath))
            throw new FileNotFoundException("Launcher update recovery executable was not found.", transaction.UpdaterPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = transaction.UpdaterPath,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--recover-update");
        startInfo.ArgumentList.Add("--pid");
        startInfo.ArgumentList.Add(currentProcessId.ToString());
        startInfo.ArgumentList.Add("--target");
        startInfo.ArgumentList.Add(Path.GetFullPath(currentExecutablePath));
        startInfo.ArgumentList.Add("--log-dir");
        startInfo.ArgumentList.Add(transaction.LogDirectory);
        startInfo.ArgumentList.Add("--restart");
        if (!startProcess(startInfo))
            throw new InvalidOperationException("Failed to start launcher update recovery process.");
        return true;
    }

    public static bool TryConfirmStartup(string[] args, string? currentExecutablePath)
    {
        return TryConfirmStartup(args, currentExecutablePath, new LauncherUpdateFileOperations());
    }

    internal static bool TryConfirmStartup(
        string[] args,
        string? currentExecutablePath,
        ILauncherUpdateFileOperations files)
    {
        if (!TryGetConfirmationId(args, out var confirmationId)
            || string.IsNullOrWhiteSpace(currentExecutablePath))
        {
            return false;
        }

        var transaction = files.ReadTransaction(LauncherUpdateTransaction.GetMarkerPath(currentExecutablePath));
        if (transaction is null
            || !transaction.HasValidDerivedPaths()
            || !string.Equals(transaction.TransactionId, confirmationId, StringComparison.Ordinal)
            || !string.Equals(
                Path.GetFullPath(transaction.TargetPath),
                Path.GetFullPath(currentExecutablePath),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        files.WriteAllTextAndFlush(transaction.ConfirmationPath, transaction.TransactionId);
        return true;
    }

    private static void TryDelete(ILauncherUpdateFileOperations files, string path)
    {
        try
        {
            files.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A confirmed update must remain launchable even when stale transaction files cannot be pruned yet.
        }
    }

    private static bool IsConfirmed(
        ILauncherUpdateFileOperations files,
        LauncherUpdateTransaction transaction)
    {
        return transaction.Phase == LauncherUpdateTransactionPhase.Confirmed
            || (files.Exists(transaction.ConfirmationPath)
                && string.Equals(
                    files.ReadAllText(transaction.ConfirmationPath).Trim(),
                    transaction.TransactionId,
                    StringComparison.Ordinal));
    }

    private static bool TryGetConfirmationId(string[] args, out string confirmationId)
    {
        confirmationId = string.Empty;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], ConfirmationArgument, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!Guid.TryParseExact(args[i + 1], "N", out _))
                return false;
            confirmationId = args[i + 1];
            return true;
        }

        return false;
    }
}
