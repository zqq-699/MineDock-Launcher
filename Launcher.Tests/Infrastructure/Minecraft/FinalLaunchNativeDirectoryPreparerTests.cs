/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Diagnostics;
using Launcher.Infrastructure.Minecraft;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class FinalLaunchNativeDirectoryPreparerTests : TestTempDirectory
{
    [Fact]
    public void PrepareCreatesNativeSubdirectoryFromArgumentList()
    {
        var nativeRoot = Path.Combine(TempRoot, "path with spaces", "natives");
        var javaDirectory = Path.Combine(nativeRoot, "java");
        var startInfo = CreateStartInfo();
        startInfo.ArgumentList.Add($"-Djava.library.path={javaDirectory}");

        FinalLaunchNativeDirectoryPreparer.Prepare(
            startInfo,
            nativeRoot,
            "26.2",
            NullLogger.Instance);

        Assert.True(Directory.Exists(javaDirectory));
    }

    [Fact]
    public void PrepareCreatesNestedRelativeNativeSubdirectoryFromArgumentsString()
    {
        var nativeRoot = Path.Combine(TempRoot, "natives");
        var targetDirectory = Path.Combine(nativeRoot, "java", "future");
        var startInfo = CreateStartInfo();
        startInfo.Arguments = "\"-Djava.library.path=natives\\java\\future\"";

        FinalLaunchNativeDirectoryPreparer.Prepare(
            startInfo,
            nativeRoot,
            "future-version",
            NullLogger.Instance);

        Assert.True(Directory.Exists(targetDirectory));
    }

    [Fact]
    public void PrepareLeavesFlatNativeLayoutUnchanged()
    {
        var nativeRoot = Path.Combine(TempRoot, "natives");
        Directory.CreateDirectory(nativeRoot);
        var startInfo = CreateStartInfo();
        startInfo.ArgumentList.Add($"-Djava.library.path={nativeRoot}");

        FinalLaunchNativeDirectoryPreparer.Prepare(
            startInfo,
            nativeRoot,
            "1.21.11",
            NullLogger.Instance);

        Assert.True(Directory.Exists(nativeRoot));
        Assert.False(Directory.Exists(Path.Combine(nativeRoot, "java")));
    }

    [Fact]
    public void PrepareDoesNotCreateNativeDirectoryOutsideTrustedRoot()
    {
        var nativeRoot = Path.Combine(TempRoot, "natives");
        var outsideDirectory = Path.Combine(TempRoot, "outside", "java");
        var startInfo = CreateStartInfo();
        startInfo.ArgumentList.Add($"-Djava.library.path={outsideDirectory}");

        FinalLaunchNativeDirectoryPreparer.Prepare(
            startInfo,
            nativeRoot,
            "26.2",
            NullLogger.Instance);

        Assert.False(Directory.Exists(outsideDirectory));
    }

    [Fact]
    public void PrepareDoesNotWriteThroughNativeRootReparsePointWhenSupported()
    {
        var nativeRoot = Path.Combine(TempRoot, "natives");
        var externalDirectory = Path.Combine(TempRoot, "external");
        var linkDirectory = Path.Combine(nativeRoot, "linked");
        Directory.CreateDirectory(nativeRoot);
        Directory.CreateDirectory(externalDirectory);
        if (!TryCreateDirectoryLink(linkDirectory, externalDirectory))
            return;

        try
        {
            var startInfo = CreateStartInfo();
            startInfo.ArgumentList.Add($"-Djava.library.path={Path.Combine(linkDirectory, "java")}");

            FinalLaunchNativeDirectoryPreparer.Prepare(
                startInfo,
                nativeRoot,
                "26.2",
                NullLogger.Instance);

            Assert.False(Directory.Exists(Path.Combine(externalDirectory, "java")));
        }
        finally
        {
            DeleteDirectoryLink(linkDirectory);
        }
    }

    private ProcessStartInfo CreateStartInfo() => new()
    {
        WorkingDirectory = TempRoot
    };

    private static bool TryCreateDirectoryLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
                                           or IOException
                                           or PlatformNotSupportedException)
        {
            if (!OperatingSystem.IsWindows())
                return false;
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "/c", "mklink", "/J", linkPath, targetPath }
        });
        process?.WaitForExit();
        return process is { ExitCode: 0 }
            && Directory.Exists(linkPath)
            && (File.GetAttributes(linkPath) & FileAttributes.ReparsePoint) != 0;
    }

    private static void DeleteDirectoryLink(string linkPath)
    {
        if (Directory.Exists(linkPath)
            && (File.GetAttributes(linkPath) & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(linkPath, recursive: false);
        }
    }
}
