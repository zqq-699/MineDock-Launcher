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

using System.IO;
using Launcher.Infrastructure.Minecraft;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class JavaRuntimeDiscoveryServiceTests
{
    [Fact]
    public void SearchRootsCoverOfficialUserAndThirdPartyRuntimes()
    {
        const string minecraftDirectory = @"C:\Games\.minecraft";
        const string programFiles = @"C:\Program Files";
        const string programFilesX86 = @"C:\Program Files (x86)";
        const string applicationData = @"C:\Users\Player\AppData\Roaming";
        const string localApplicationData = @"C:\Users\Player\AppData\Local";
        const string userProfile = @"C:\Users\Player";
        const string documents = @"C:\Users\Player\Documents";

        var roots = JavaRuntimeDiscoveryService.GetJavaSearchRoots(
            minecraftDirectory,
            programFiles,
            programFilesX86,
            applicationData,
            localApplicationData,
            userProfile,
            documents,
            (path, pattern, searchOption) =>
            {
                Assert.Equal("jdk-*", pattern);
                Assert.Equal(SearchOption.TopDirectoryOnly, searchOption);
                return [Path.Combine(path, "jdk-21.0.7-hotspot")];
            });

        AssertRoot(roots, Path.Combine(minecraftDirectory, "runtime"), "MinecraftRuntime");
        AssertRoot(roots, Path.Combine(applicationData, ".minecraft", "runtime"), "OfficialMinecraftRuntime");
        AssertRoot(
            roots,
            Path.Combine(
                localApplicationData,
                "Packages",
                "Microsoft.4297127D64EC6_8wekyb3d8bbwe",
                "LocalCache",
                "Local",
                "runtime"),
            "OfficialMinecraftRuntime");
        AssertRoot(roots, Path.Combine(programFiles, "Minecraft Launcher", "runtime"), "OfficialMinecraftRuntime");
        AssertRoot(roots, Path.Combine(programFilesX86, "Minecraft Launcher", "runtime"), "OfficialMinecraftRuntime");

        AssertRoot(roots, Path.Combine(userProfile, ".jdks"), "UserJava");
        AssertRoot(roots, Path.Combine(userProfile, ".sdkman", "candidates", "java"), "UserJava");
        AssertRoot(roots, Path.Combine(applicationData, ".hmcl", "java"), "ThirdPartyLauncherRuntime");
        AssertRoot(roots, Path.Combine(applicationData, "ATLauncher", "runtimes", "minecraft"), "ThirdPartyLauncherRuntime");
        AssertRoot(roots, Path.Combine(applicationData, "ModrinthApp", "meta", "java_versions"), "ThirdPartyLauncherRuntime");
        AssertRoot(roots, Path.Combine(applicationData, "PrismLauncher", "java"), "ThirdPartyLauncherRuntime");
        AssertRoot(roots, Path.Combine(userProfile, "curseforge", "minecraft", "Install", "runtime"), "ThirdPartyLauncherRuntime");
        AssertRoot(roots, Path.Combine(localApplicationData, ".ftba", "bin", "runtime"), "ThirdPartyLauncherRuntime");
        AssertRoot(roots, Path.Combine(documents, "Curse", "Minecraft", "Install", "runtime"), "ThirdPartyLauncherRuntime");

        AssertRoot(roots, Path.Combine(programFiles, "Amazon Corretto"), "ProgramFiles");
        AssertRoot(roots, Path.Combine(programFiles, "Microsoft", "jdk-21.0.7-hotspot"), "ProgramFiles");
        AssertRoot(roots, Path.Combine(programFilesX86, "Semeru"), "ProgramFiles");
    }

    [Fact]
    public void CandidateCollectionIncludesQuotedEnvironmentAndRegisteredJavaHomes()
    {
        var candidates = JavaRuntimeDiscoveryService.CollectCandidatePaths(
            minecraftDirectory: null,
            fileExists: _ => true,
            directoryExists: _ => false,
            enumerateFiles: (_, _, _) => [],
            enumerateDirectories: (_, _, _) => [],
            getEnvironmentVariable: name => name switch
            {
                "JAVA_HOME" => "  \"C:\\Java\\jdk-21\"  ",
                "PATH" => "\"C:\\Java\\path-bin\"",
                _ => null
            },
            getProgramFiles: () => string.Empty,
            getProgramFilesX86: () => string.Empty,
            getApplicationData: () => string.Empty,
            getLocalApplicationData: () => string.Empty,
            getUserProfile: () => string.Empty,
            getDocuments: () => string.Empty,
            getRegisteredJavaHomes: () => [@"C:\Registry Java\jdk-17"],
            resolveIdentityPath: path => path);

        Assert.Contains(candidates, candidate =>
            candidate.ExecutablePath == @"C:\Java\jdk-21\bin\java.exe"
            && candidate.Source == "JAVA_HOME");
        Assert.Contains(candidates, candidate =>
            candidate.ExecutablePath == @"C:\Java\path-bin\java.exe"
            && candidate.Source == "PATH");
        Assert.Contains(candidates, candidate =>
            candidate.ExecutablePath == @"C:\Registry Java\jdk-17\bin\java.exe"
            && candidate.Source == "RegisteredJava");
    }

    [Fact]
    public void ConfiguredMinecraftRuntimeWinsWhenItMatchesDefaultMinecraftRuntime()
    {
        const string applicationData = @"C:\Users\Player\AppData\Roaming";
        var minecraftDirectory = Path.Combine(applicationData, ".minecraft");

        var roots = JavaRuntimeDiscoveryService.GetJavaSearchRoots(
            minecraftDirectory,
            string.Empty,
            string.Empty,
            applicationData,
            string.Empty,
            string.Empty,
            string.Empty,
            (_, _, _) => []);

        var runtimeRoot = Assert.Single(roots.Where(root =>
            string.Equals(root.Path, Path.Combine(minecraftDirectory, "runtime"), StringComparison.OrdinalIgnoreCase)));
        Assert.Equal("MinecraftRuntime", runtimeRoot.Source);
    }

    private static void AssertRoot(
        IReadOnlyList<JavaRuntimeSearchRoot> roots,
        string expectedPath,
        string expectedSource)
    {
        Assert.Contains(roots, root =>
            string.Equals(root.Path, expectedPath, StringComparison.OrdinalIgnoreCase)
            && root.Source == expectedSource);
    }
}
