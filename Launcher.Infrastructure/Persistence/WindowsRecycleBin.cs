/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace Launcher.Infrastructure.Persistence;

internal static class WindowsRecycleBin
{
    private const uint FileOperationDelete = 0x0003;
    private const ushort AllowUndo = 0x0040;
    private const ushort NoConfirmation = 0x0010;
    private const ushort Silent = 0x0004;
    private const ushort NoErrorUi = 0x0400;

    public static void MoveDirectory(string path)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The system recycle bin is only available on Windows.");

        var operation = new ShellFileOperation
        {
            Function = FileOperationDelete,
            From = Path.GetFullPath(path) + '\0' + '\0',
            Flags = AllowUndo | NoConfirmation | Silent | NoErrorUi
        };
        var result = SHFileOperation(ref operation);
        if (result != 0)
            throw new Win32Exception(result, "Failed to move the staged instance directory to the recycle bin.");
        if (operation.AnyOperationsAborted || Directory.Exists(path))
            throw new IOException("Moving the staged instance directory to the recycle bin was aborted.");
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShellFileOperation operation);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileOperation
    {
        public IntPtr Window;
        public uint Function;
        [MarshalAs(UnmanagedType.LPWStr)] public string From;
        [MarshalAs(UnmanagedType.LPWStr)] public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool AnyOperationsAborted;
        public IntPtr NameMappings;
        public string? ProgressTitle;
    }
}
