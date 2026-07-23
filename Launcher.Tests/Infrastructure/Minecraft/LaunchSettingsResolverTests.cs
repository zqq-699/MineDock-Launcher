/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class LaunchSettingsResolverTests
{
    [Fact]
    public async Task ResolveUsesGlobalAutoJoinAddressWhenInstanceInheritsLaunchSettings()
    {
        var resolver = new LaunchSettingsResolver(null, null, NullLogger.Instance);
        var settings = new LauncherSettings
        {
            DefaultMemorySettingsMode = MemorySettingsMode.Manual,
            DefaultMemoryMb = 4096,
            DefaultAutoJoinServerAddress = "global.example.com:25565"
        };
        var instance = new GameInstance
        {
            MinecraftVersion = "1.20.1",
            LaunchSettingsMode = LaunchSettingsMode.UseGlobal,
            AutoJoinServerAddress = "instance.example.com:25566"
        };

        var resolved = await resolver.ResolveAsync(instance, settings, CancellationToken.None);

        Assert.Equal("global.example.com:25565", resolved.AutoJoinServerAddress);
    }

    [Fact]
    public async Task ResolveUsesInstanceAutoJoinAddressWhenLaunchSettingsAreOverridden()
    {
        var resolver = new LaunchSettingsResolver(null, null, NullLogger.Instance);
        var settings = new LauncherSettings
        {
            DefaultMemorySettingsMode = MemorySettingsMode.Manual,
            DefaultMemoryMb = 4096,
            DefaultAutoJoinServerAddress = "global.example.com:25565"
        };
        var instance = new GameInstance
        {
            MinecraftVersion = "1.20.1",
            LaunchSettingsMode = LaunchSettingsMode.PerInstance,
            MemorySettingsMode = MemorySettingsMode.Manual,
            MemoryMb = 6144,
            AutoJoinServerAddress = "instance.example.com:25566"
        };

        var resolved = await resolver.ResolveAsync(instance, settings, CancellationToken.None);

        Assert.Equal("instance.example.com:25566", resolved.AutoJoinServerAddress);
    }
}
