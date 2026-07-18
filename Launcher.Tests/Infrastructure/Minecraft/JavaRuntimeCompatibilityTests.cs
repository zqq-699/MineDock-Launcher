/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class JavaRuntimeCompatibilityTests
{
    [Theory]
    [InlineData("1.8.0_320", 8, 0, 320, 0)]
    [InlineData("1.8.0_320-b07", 8, 0, 320, 0)]
    [InlineData("17.0.2", 17, 0, 2, 0)]
    [InlineData("21.0.1+12-LTS", 21, 0, 1, 12)]
    public void JavaVersionParserNormalizesLegacyAndModernFormats(
        string value,
        int major,
        int minor,
        int patch,
        int build)
    {
        Assert.True(JavaVersionNumber.TryParse(value, out var parsed));
        Assert.Equal(new JavaVersionNumber(major, minor, patch, build), parsed);
    }

    [Theory]
    [InlineData("1.12.2", "14.23.5.2860", "1.8.0_402", true)]
    [InlineData("1.12.2", "14.23.5.2860", "17.0.12", false)]
    [InlineData("1.14.4", "28.2.26", "10.0.2", true)]
    [InlineData("1.14.4", "28.2.26", "11.0.22", false)]
    [InlineData("1.15.2", "31.2.57", "15.0.2", true)]
    [InlineData("1.15.2", "31.2.57", "16.0.2", false)]
    [InlineData("1.16.5", "36.2.25", "1.8.0_320", true)]
    [InlineData("1.16.5", "36.2.25", "1.8.0_321", false)]
    [InlineData("1.16.5", "36.2.26", "23.0.2", true)]
    [InlineData("1.16.5", "36.2.26", "24.0.1", false)]
    [InlineData("1.17.1", "37.0.79", "16.0.2", true)]
    [InlineData("1.17.1", "37.0.79", "17.0.12", false)]
    [InlineData("1.17.1", "37.0.80", "17.0.12", true)]
    [InlineData("1.19.4", "45.0.65", "19.0.2", true)]
    [InlineData("1.19.4", "45.0.65", "20.0.2", false)]
    [InlineData("1.19.4", "45.0.66", "21.0.5", true)]
    [InlineData("1.19.4", "45.0.66", "22.0.2", false)]
    [InlineData("1.20.1", "47.4.8", "21.0.5", true)]
    [InlineData("1.20.1", "47.4.8", "22.0.2", false)]
    [InlineData("1.20.1", "47.4.9", "22.0.2", true)]
    public void ForgeCompatibilityRulesApplyAtDocumentedBoundaries(
        string minecraftVersion,
        string forgeVersion,
        string javaVersion,
        bool expectedCompatible)
    {
        var requirement = JavaRuntimeCompatibilityResolver.Resolve(
            minecraftVersion,
            LoaderKind.Forge,
            forgeVersion,
            metadataMajorVersion: null);

        Assert.Equal(expectedCompatible, requirement.IsCompatible(CreateRuntime(javaVersion)));
    }

    [Theory]
    [InlineData("1.20.1", "20.1.137", "21.0.5", true)]
    [InlineData("1.20.1", "20.1.137", "22.0.2", false)]
    [InlineData("1.20.2", "20.2.62-beta", "21.0.5", true)]
    [InlineData("1.20.2", "20.2.62-beta", "22.0.2", false)]
    [InlineData("1.20.2", "20.2.63-beta", "22.0.2", true)]
    [InlineData("1.20.2", "25w14craftmine-20.2.1", "25.0.1", true)]
    public void NeoForgeCompatibilityRulesApplyAtDocumentedBoundaries(
        string minecraftVersion,
        string neoForgeVersion,
        string javaVersion,
        bool expectedCompatible)
    {
        var requirement = JavaRuntimeCompatibilityResolver.Resolve(
            minecraftVersion,
            LoaderKind.NeoForge,
            neoForgeVersion,
            metadataMajorVersion: 17);

        Assert.Equal(expectedCompatible, requirement.IsCompatible(CreateRuntime(javaVersion)));
    }

    [Fact]
    public void ForgeLegacyRangeUsesJavaSevenInsteadOfConflictingFallback()
    {
        var requirement = JavaRuntimeCompatibilityResolver.Resolve(
            "1.7.2",
            LoaderKind.Forge,
            "10.12.2.1161",
            metadataMajorVersion: null);

        Assert.Equal(7, requirement.RecommendedMajorVersion);
        Assert.True(requirement.IsCompatible(CreateRuntime("1.7.0_80")));
        Assert.False(requirement.IsCompatible(CreateRuntime("1.8.0_202")));
    }

    [Fact]
    public void PatchBoundRejectsRuntimeWhoseFullVersionCannotBeVerified()
    {
        var requirement = JavaRuntimeCompatibilityResolver.Resolve(
            "1.16.5",
            LoaderKind.Forge,
            "36.2.25",
            metadataMajorVersion: 8);
        var runtime = CreateRuntime(version: null, majorVersion: 8);

        Assert.False(requirement.IsCompatible(runtime));
    }

    [Fact]
    public void MinecraftTwentySixFallbackRequiresJavaTwentyFive()
    {
        var requirement = JavaRuntimeCompatibilityResolver.Resolve(
            "26.2",
            LoaderKind.Vanilla,
            loaderVersion: null,
            metadataMajorVersion: null);

        Assert.Equal(25, requirement.RecommendedMajorVersion);
        Assert.False(requirement.IsCompatible(CreateRuntime("21.0.8")));
        Assert.True(requirement.IsCompatible(CreateRuntime("25.0.1")));
    }

    [Fact]
    public void SelectionPrefersX64ThenNewestPatchWithinRecommendedMajor()
    {
        var requirement = JavaRuntimeCompatibilityResolver.Resolve(
            "1.20.1",
            LoaderKind.Vanilla,
            loaderVersion: null,
            metadataMajorVersion: 17);
        var selected = JavaRuntimeSelectionService.SelectBestRuntime(
            [
                CreateRuntime("17.0.12", architecture: "x86", path: @"C:\Java\x86-new\bin\java.exe"),
                CreateRuntime("17.0.8", architecture: "x64", path: @"C:\Java\x64-old\bin\java.exe"),
                CreateRuntime("17.0.10", architecture: "x64", path: @"C:\Java\x64-new\bin\java.exe"),
                CreateRuntime("21.0.5", architecture: "x64", path: @"C:\Java\21\bin\java.exe")
            ],
            requirement);

        Assert.Equal("17.0.10", selected?.Version);
        Assert.Equal("x64", selected?.Architecture);
    }

    [Fact]
    public async Task ManualRuntimeAboveForgeUpperBoundCanOnlyRunWhenRequirementIsIgnored()
    {
        var runtime = CreateRuntime("17.0.12");
        var service = new JavaRuntimeSelectionService(new FixedDiscovery(runtime));
        var settings = new LauncherSettings
        {
            JavaSelectionMode = JavaSelectionMode.Manual,
            SelectedJavaExecutablePath = runtime.ExecutablePath
        };
        var instance = new GameInstance
        {
            MinecraftVersion = "1.12.2",
            Loader = LoaderKind.Forge,
            LoaderVersion = "14.23.5.2860"
        };

        var exception = await Assert.ThrowsAsync<JavaRuntimeSelectionException>(() =>
            service.SelectForLaunchAsync(instance, settings));

        Assert.Equal(JavaRuntimeSelectionFailureReason.ManualRuntimeIncompatible, exception.Reason);
        Assert.Equal("17.0.12", exception.CurrentVersion);
        Assert.Equal(8, exception.RecommendedMajorVersion);

        var selected = await service.SelectForLaunchAsync(
            instance,
            settings,
            new LaunchRequestOptions(IgnoreJavaVersionRequirement: true));
        Assert.Same(runtime, selected);
    }

    private static JavaRuntimeInfo CreateRuntime(
        string? version,
        int? majorVersion = null,
        string architecture = "x64",
        string path = @"C:\Java\runtime\bin\java.exe")
    {
        var parsedMajorVersion = majorVersion
            ?? (JavaVersionNumber.TryParse(version, out var parsed) ? parsed.Major : null);
        return new JavaRuntimeInfo(
            parsedMajorVersion is int major ? $"Java {major}" : "Java",
            version,
            parsedMajorVersion,
            architecture,
            path,
            Path.GetDirectoryName(Path.GetDirectoryName(path)) ?? string.Empty,
            "Test");
    }

    private sealed class FixedDiscovery(JavaRuntimeInfo runtime) : IJavaRuntimeDiscoveryService
    {
        public Task<IReadOnlyList<JavaRuntimeInfo>> DiscoverAsync(
            string? minecraftDirectory,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<JavaRuntimeInfo>>([runtime]);

        public Task<JavaRuntimeInfo> DiscoverExecutableAsync(
            string executablePath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(runtime);
    }
}
