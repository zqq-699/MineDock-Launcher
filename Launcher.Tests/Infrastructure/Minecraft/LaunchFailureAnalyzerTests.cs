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

using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class LaunchFailureAnalyzerTests : TestTempDirectory
{
    [Fact]
    public void AnalyzerDetectsFabricJavaVersionMismatch()
    {
        var latestLog = """
            [main/ERROR]: Incompatible mods found!
            More details:
             - Mod 'Fabric API' (fabric-api) 0.134.1+1.21.9 requires version 21 or later of 'Java HotSpot(TM) 64-Bit Server VM' (java), but only the wrong version is present: 8!
            """;

        var analysis = LaunchFailureAnalyzer.Analyze(CreateContext(), string.Empty, string.Empty, latestLog, []);

        Assert.NotNull(analysis);
        Assert.Equal(LaunchFailureCategory.JavaVersionMismatch, analysis.Category);
        Assert.Equal("Fabric API", analysis.ModName);
        Assert.Equal(21, analysis.RequiredJavaMajorVersion);
        Assert.Equal(8, analysis.CurrentJavaMajorVersion);
    }

    [Fact]
    public void AnalyzerPrefersCurrentMissingClasspathEntryOverStaleLatestLog()
    {
        const string missingPath = @"C:\Users\zhouquan\Documents\launcher\.minecraft\versions\1.21.9-fabric-0.19.3\1.21.9-fabric-0.19.3.jar";
        var stdout = $"""
            [22:57:15] [WARN] [FabricLoader/Knot]: Class path entries reference missing files: {missingPath} - the game may not load properly!
            """;
        const string stderr = """
            [22:57:15] [ERROR] [FabricLoader/GameProvider]: Minecraft game provider couldn't locate the game! The game may be absent from the class path.
            java.lang.RuntimeException: Minecraft game provider couldn't locate the game!
            """;
        const string staleLatestLog = """
            [22:48:39] [main/ERROR]: Incompatible mods found!
             - Mod 'Fabric API' (fabric-api) 0.134.1+1.21.9 requires version 21 or later of 'Java HotSpot(TM) 64-Bit Server VM' (java), but only the wrong version is present: 8!
            """;

        var analysis = LaunchFailureAnalyzer.Analyze(CreateContext(), stdout, stderr, staleLatestLog, []);

        Assert.NotNull(analysis);
        Assert.Equal(LaunchFailureCategory.MissingGameFiles, analysis.Category);
        Assert.Equal("missing_classpath_entry", analysis.ReasonDetail);
        Assert.Equal(missingPath, analysis.MissingPath);
        Assert.Null(analysis.RequiredJavaMajorVersion);
    }

    [Fact]
    public void AnalyzerDetectsMissingClientJarRepairFailure()
    {
        var context = CreateContext();
        const string exceptionText = """
            [0] Launcher.Application.Services.InstanceRepairException
            Version Example is missing its client jar and automatic repair is disabled.
            """;

        var analysis = LaunchFailureAnalyzer.Analyze(context, string.Empty, exceptionText, string.Empty, []);

        Assert.NotNull(analysis);
        Assert.Equal(LaunchFailureCategory.MissingGameFiles, analysis.Category);
        Assert.Equal("missing_client_jar", analysis.ReasonDetail);
        Assert.Equal(Path.Combine(context.InstanceDirectory, "Example.jar"), analysis.MissingPath);
    }

    [Fact]
    public void AnalyzerDetectsMissingModDependency()
    {
        var latestLog = "Mod 'Example Addon' requires mod 'fabric-api', which is missing.";

        var analysis = LaunchFailureAnalyzer.Analyze(CreateContext(), string.Empty, string.Empty, latestLog, []);

        Assert.NotNull(analysis);
        Assert.Equal(LaunchFailureCategory.ModDependencyMissing, analysis.Category);
        Assert.Equal("Example Addon", analysis.ModName);
        Assert.Equal("fabric-api", analysis.DependencyName);
    }

    [Fact]
    public void AnalyzerDetectsOutOfMemory()
    {
        var latestLog = "java.lang.OutOfMemoryError: Java heap space";

        var analysis = LaunchFailureAnalyzer.Analyze(CreateContext(), string.Empty, string.Empty, latestLog, []);

        Assert.NotNull(analysis);
        Assert.Equal(LaunchFailureCategory.OutOfMemory, analysis.Category);
    }

    [Fact]
    public void AnalyzerDoesNotTreatLog4jErrorsAsPrimaryCause()
    {
        var latestLog = """
            main ERROR Error processing element Listener ([Appenders: null]): CLASS_NOT_FOUND
            main ERROR Console contains an invalid element or attribute "LegacyXMLLayout"
            main ERROR Unable to locate appender "Tracy" for logger config "root"
            """;

        var analysis = LaunchFailureAnalyzer.Analyze(CreateContext(), string.Empty, string.Empty, latestLog, []);

        Assert.Null(analysis);
    }

    [Fact]
    public void AnalyzerReturnsNullWhenNoRuleMatches()
    {
        var analysis = LaunchFailureAnalyzer.Analyze(CreateContext(), string.Empty, string.Empty, "Game closed.", []);

        Assert.Null(analysis);
    }

    private LaunchDiagnosticContext CreateContext()
    {
        return new LaunchDiagnosticContext(
            Path.Combine(TempRoot, ".minecraft"),
            Path.Combine(TempRoot, ".minecraft", "versions", "Example"),
            "instance",
            "Example",
            "Example",
            "1.21.9",
            LoaderKind.Fabric,
            "0.19.3",
            @"C:\Java\bin\java.exe",
            "21.0.1",
            "Test",
            4096,
            []);
    }
}
