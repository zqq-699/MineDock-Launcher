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

using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class LoaderInstallerJavaRuntimeResolverTests
{
    [Fact]
    public async Task NewInstallUsesGlobalManualJavaSelection()
    {
        const string javaPath = @"C:\Program Files\Launcher Java\bin\java.exe";
        var discovery = new RecordingJavaRuntimeDiscoveryService
        {
            ManualRuntime = CreateRuntime(javaPath, 21)
        };
        var settings = new LauncherSettings
        {
            MinecraftDirectory = @"C:\Games\.minecraft",
            JavaSelectionMode = JavaSelectionMode.Manual,
            SelectedJavaExecutablePath = javaPath
        };
        var resolver = CreateResolver(settings, [], discovery);

        var runtime = await resolver.ResolveAsync("1.20.5", "forge-instance");

        Assert.Equal(javaPath, runtime.ExecutablePath);
        Assert.Equal(javaPath, discovery.LastProbedPath);
    }

    [Fact]
    public async Task NewInstallUsesCompatibleAutomaticallyDiscoveredJava()
    {
        const string expectedPath = @"C:\Java\jdk-17\bin\java.exe";
        var discovery = new RecordingJavaRuntimeDiscoveryService
        {
            Runtimes =
            [
                CreateRuntime(@"C:\Java\jdk-8\bin\java.exe", 8),
                CreateRuntime(expectedPath, 17),
                CreateRuntime(@"C:\Java\jdk-21\bin\java.exe", 21)
            ]
        };
        var settings = new LauncherSettings
        {
            MinecraftDirectory = @"C:\Games\.minecraft",
            JavaSelectionMode = JavaSelectionMode.Auto
        };
        var resolver = CreateResolver(settings, [], discovery);

        var runtime = await resolver.ResolveAsync("1.20.1", "forge-instance");

        Assert.Equal(expectedPath, runtime.ExecutablePath);
    }

    [Fact]
    public async Task ExistingInstanceUsesItsPerInstanceJavaSelection()
    {
        const string globalPath = @"C:\Java\global\bin\java.exe";
        const string instancePath = @"C:\Java\instance\bin\java.exe";
        var discovery = new RecordingJavaRuntimeDiscoveryService
        {
            ManualRuntime = CreateRuntime(instancePath, 21)
        };
        var settings = new LauncherSettings
        {
            MinecraftDirectory = @"C:\Games\.minecraft",
            JavaSelectionMode = JavaSelectionMode.Manual,
            SelectedJavaExecutablePath = globalPath
        };
        var storedInstance = new GameInstance
        {
            Name = "Repair Target",
            MinecraftVersion = "1.20.5",
            VersionName = "repair-target",
            JavaSettingsMode = LaunchSettingsMode.PerInstance,
            JavaSelectionMode = JavaSelectionMode.Manual,
            SelectedJavaExecutablePath = instancePath
        };
        var resolver = CreateResolver(settings, [storedInstance], discovery);

        var runtime = await resolver.ResolveAsync("1.20.5", "repair-target");

        Assert.Equal(instancePath, runtime.ExecutablePath);
        Assert.Equal(instancePath, discovery.LastProbedPath);
    }

    [Fact]
    public async Task InvalidSelectedJavaPreservesStructuredSelectionFailure()
    {
        const string javaPath = @"C:\Java\jdk-8\bin\java.exe";
        var discovery = new RecordingJavaRuntimeDiscoveryService
        {
            ManualRuntime = CreateRuntime(javaPath, 8)
        };
        var settings = new LauncherSettings
        {
            MinecraftDirectory = @"C:\Games\.minecraft",
            JavaSelectionMode = JavaSelectionMode.Manual,
            SelectedJavaExecutablePath = javaPath
        };
        var resolver = CreateResolver(settings, [], discovery);

        var exception = await Assert.ThrowsAsync<JavaRuntimeSelectionException>(() =>
            resolver.ResolveAsync("1.20.5", "forge-instance"));

        Assert.Equal(JavaRuntimeSelectionFailureReason.ManualRuntimeVersionTooLow, exception.Reason);
        Assert.Equal(21, exception.RequiredMajorVersion);
        Assert.Equal(8, exception.CurrentMajorVersion);
    }

    private static LoaderInstallerJavaRuntimeResolver CreateResolver(
        LauncherSettings settings,
        IReadOnlyList<GameInstance> instances,
        IJavaRuntimeDiscoveryService discovery)
    {
        return new LoaderInstallerJavaRuntimeResolver(
            _ => Task.FromResult(settings),
            (_, _) => Task.FromResult(instances),
            new JavaRuntimeSelectionService(discovery));
    }

    private static JavaRuntimeInfo CreateRuntime(string path, int majorVersion)
    {
        return new JavaRuntimeInfo(
            $"Java {majorVersion}",
            $"{majorVersion}.0.0",
            majorVersion,
            "x64",
            path,
            Path.GetDirectoryName(Path.GetDirectoryName(path))!,
            "Test");
    }

    private sealed class RecordingJavaRuntimeDiscoveryService : IJavaRuntimeDiscoveryService
    {
        public IReadOnlyList<JavaRuntimeInfo> Runtimes { get; init; } = [];

        public JavaRuntimeInfo? ManualRuntime { get; init; }

        public string? LastProbedPath { get; private set; }

        public Task<IReadOnlyList<JavaRuntimeInfo>> DiscoverAsync(
            string? minecraftDirectory,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Runtimes);
        }

        public Task<JavaRuntimeInfo> DiscoverExecutableAsync(
            string executablePath,
            CancellationToken cancellationToken = default)
        {
            LastProbedPath = executablePath;
            if (ManualRuntime is null)
                throw new FileNotFoundException("Java runtime is unavailable.", executablePath);

            return Task.FromResult(ManualRuntime);
        }
    }
}
