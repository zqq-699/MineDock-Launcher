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

using Launcher.Infrastructure.Minecraft;
using Launcher.Domain.Models;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class JavaRuntimeDiscoveryServiceTests
{
    [Fact]
    public void CollectCandidatePathsDeduplicatesEnvironmentAndPathEntries()
    {
        var existingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Java\jdk-21\bin\java.exe",
            @"C:\Java\jdk-17\bin\java.exe"
        };
        var environmentVariables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["JAVA_HOME"] = @"C:\Java\jdk-21",
            ["PATH"] = string.Join(Path.PathSeparator, @"C:\Java\jdk-21\bin", @"C:\Java\jdk-17\bin")
        };

        var candidates = JavaRuntimeDiscoveryService.CollectCandidatePaths(
            minecraftDirectory: null,
            fileExists: existingFiles.Contains,
            directoryExists: _ => false,
            getEnvironmentVariable: name => environmentVariables.GetValueOrDefault(name));

        Assert.Equal(2, candidates.Count);
        Assert.Equal(@"C:\Java\jdk-21\bin\java.exe", candidates[0].ExecutablePath);
        Assert.Equal("JAVA_HOME", candidates[0].Source);
        Assert.Equal(@"C:\Java\jdk-17\bin\java.exe", candidates[1].ExecutablePath);
        Assert.Equal("PATH", candidates[1].Source);
    }

    [Fact]
    public void CollectCandidatePathsIncludesMinecraftRuntime()
    {
        var runtimePath = @"C:\Launcher\.minecraft\runtime\java-runtime-gamma\windows-x64\java-runtime-gamma\bin\java.exe";

        var candidates = JavaRuntimeDiscoveryService.CollectCandidatePaths(
            minecraftDirectory: @"C:\Launcher\.minecraft",
            fileExists: path => path == runtimePath,
            directoryExists: path => path == @"C:\Launcher\.minecraft\runtime",
            enumerateFiles: (path, pattern, option) => path == @"C:\Launcher\.minecraft\runtime" ? [runtimePath] : [],
            getEnvironmentVariable: _ => null);

        var candidate = Assert.Single(candidates);
        Assert.Equal(runtimePath, candidate.ExecutablePath);
        Assert.Equal("MinecraftRuntime", candidate.Source);
    }

    [Fact]
    public void CollectCandidatePathsPrefersInstallDirectoryOverPathProxyForSameTarget()
    {
        const string proxyPath = @"C:\Program Files\Common Files\Oracle\Java\javapath\java.exe";
        const string installPath = @"C:\Program Files\Java\jdk-21\bin\java.exe";
        var existingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            proxyPath,
            installPath
        };

        var candidates = JavaRuntimeDiscoveryService.CollectCandidatePaths(
            minecraftDirectory: null,
            fileExists: existingFiles.Contains,
            directoryExists: path => path == @"C:\Program Files\Java",
            enumerateFiles: (path, pattern, option) => path == @"C:\Program Files\Java" ? [installPath] : [],
            getEnvironmentVariable: name => name == "PATH"
                ? @"C:\Program Files\Common Files\Oracle\Java\javapath"
                : null,
            getProgramFiles: () => @"C:\Program Files",
            getProgramFilesX86: () => string.Empty,
            resolveIdentityPath: path => path == proxyPath ? installPath : path);

        var candidate = Assert.Single(candidates);
        Assert.Equal(installPath, candidate.ExecutablePath);
        Assert.Equal("ProgramFiles", candidate.Source);
    }

    [Fact]
    public void CollapseDuplicateRuntimesKeepsOneRuntimeForSameInstallVersionAndArchitecture()
    {
        var runtimes = new[]
        {
            new JavaRuntimeInfo(
                "Java 21",
                "21.0.2",
                21,
                "x64",
                @"C:\Java\jdk-21\bin\java.exe",
                @"C:\Java\jdk-21",
                "ProgramFiles"),
            new JavaRuntimeInfo(
                "Java 21",
                "21.0.2",
                21,
                "x64",
                @"C:\Java\jdk-21\jre\bin\java.exe",
                @"C:\Java\jdk-21",
                "PATH")
        };

        var collapsed = JavaRuntimeDiscoveryService.CollapseDuplicateRuntimes(runtimes);

        var runtime = Assert.Single(collapsed);
        Assert.Equal(@"C:\Java\jdk-21\bin\java.exe", runtime.ExecutablePath);
        Assert.Equal("ProgramFiles", runtime.Source);
    }

    [Fact]
    public void ParseVersionOutputReadsModernJavaVersionAndArchitecture()
    {
        const string output = """
            openjdk version "21.0.2" 2024-01-16 LTS
            OpenJDK Runtime Environment Temurin-21.0.2+13
            OpenJDK 64-Bit Server VM Temurin-21.0.2+13
            """;

        var result = JavaRuntimeDiscoveryService.ParseVersionOutput(output);

        Assert.Equal("21.0.2", result.Version);
        Assert.Equal(21, result.MajorVersion);
        Assert.Equal("x64", result.Architecture);
    }

    [Fact]
    public void ParseVersionOutputReadsLegacyJavaMajorVersion()
    {
        const string output = """
            java version "1.8.0_351"
            Java(TM) SE Runtime Environment
            Java HotSpot(TM) Client VM
            """;

        var result = JavaRuntimeDiscoveryService.ParseVersionOutput(output);

        Assert.Equal("1.8.0_351", result.Version);
        Assert.Equal(8, result.MajorVersion);
        Assert.Equal("unknown", result.Architecture);
    }

    [Fact]
    public void ParseVersionOutputFallsBackForUnknownVersion()
    {
        var result = JavaRuntimeDiscoveryService.ParseVersionOutput("not a java version");

        Assert.Null(result.Version);
        Assert.Null(result.MajorVersion);
        Assert.Equal("unknown", result.Architecture);
    }
}
