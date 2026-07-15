/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Launcher.Infrastructure.Minecraft;

internal readonly record struct WindowsFileIdentity(uint VolumeSerialNumber, ulong FileIndex);

internal readonly record struct MinecraftFileVerificationSnapshot(
    WindowsFileIdentity Identity,
    long Length,
    long LastWriteTime,
    long ChangeTime);

internal sealed class MinecraftVerifiedFileLease : IDisposable
{
    private FileStream? stream;

    internal MinecraftVerifiedFileLease(FileStream stream, MinecraftFileVerificationSnapshot snapshot)
    {
        this.stream = stream;
        Snapshot = snapshot;
    }

    public MinecraftFileVerificationSnapshot Snapshot { get; }

    public void Dispose() => Interlocked.Exchange(ref stream, null)?.Dispose();
}

internal static class WindowsFileSnapshot
{
    public static bool TryCapture(string path, out MinecraftFileVerificationSnapshot snapshot)
    {
        using var lease = TryAcquire(path);
        if (lease is null)
        {
            snapshot = default;
            return false;
        }

        snapshot = lease.Snapshot;
        return true;
    }

    public static MinecraftVerifiedFileLease? TryAcquire(string path)
    {
        try
        {
            var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1,
                FileOptions.SequentialScan);
            if (!TryCapture(stream.SafeFileHandle, out var snapshot))
            {
                stream.Dispose();
                return null;
            }

            return new MinecraftVerifiedFileLease(stream, snapshot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static bool TryGetIdentity(SafeFileHandle handle, out WindowsFileIdentity identity)
    {
        identity = default;
        if (!GetFileInformationByHandle(handle, out var information))
            return false;

        identity = new WindowsFileIdentity(
            information.VolumeSerialNumber,
            ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow);
        return true;
    }

    private static bool TryCapture(
        SafeFileHandle handle,
        out MinecraftFileVerificationSnapshot snapshot)
    {
        snapshot = default;
        if (!GetFileInformationByHandle(handle, out var information)
            || !GetFileInformationByHandleEx(
                handle,
                FileInfoByHandleClass.FileBasicInfo,
                out var basicInformation,
                (uint)Marshal.SizeOf<FileBasicInformation>()))
        {
            return false;
        }

        if ((information.FileAttributes & ((uint)FileAttributes.Directory | (uint)FileAttributes.ReparsePoint)) != 0)
            return false;

        var identity = new WindowsFileIdentity(
            information.VolumeSerialNumber,
            ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow);
        var length = checked((long)(((ulong)information.FileSizeHigh << 32) | information.FileSizeLow));
        snapshot = new MinecraftFileVerificationSnapshot(
            identity,
            length,
            basicInformation.LastWriteTime,
            basicInformation.ChangeTime);
        return true;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle fileHandle,
        out ByHandleFileInformation fileInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle fileHandle,
        FileInfoByHandleClass fileInformationClass,
        out FileBasicInformation fileInformation,
        uint bufferSize);

    private enum FileInfoByHandleClass
    {
        FileBasicInfo = 0
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileBasicInformation
    {
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public long ChangeTime;
        public uint FileAttributes;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
