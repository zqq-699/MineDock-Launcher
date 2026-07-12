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
    public void AnalyzerDoesNotCombineUnrelatedFabricVersionRequirementsIntoJavaMismatch()
    {
        const string latestLog = """
            Incompatible mods found!
            A potential solution has been determined:
             - Install bclib, any version between 3.0.14 (inclusive) and 3.1- (exclusive).
             - Install bookshelf, version 20 or later.
            More details:
             - Mod 'Better End' (betterend) 4.0.11 requires any 3.0.x version of bclib, which is missing!
             - Mod 'EnchantmentDescriptions' (enchdesc) 17.1.19 requires version 20 or later of bookshelf, which is missing!
             - Mod 'Statement Vanilla Compatibility Module' (statement_vanilla_compatibility) 1.0 requires any version between 1.14-0 (inclusive) and 1.16-0 (exclusive) of 'Minecraft' (minecraft), but only the wrong version is present: 1.20.1!
            """;

        var analysis = LaunchFailureAnalyzer.Analyze(CreateContext(), string.Empty, string.Empty, latestLog, []);

        Assert.NotNull(analysis);
        Assert.Equal(LaunchFailureCategory.ModDependencyMissing, analysis.Category);
        Assert.Null(analysis.RequiredJavaMajorVersion);
        Assert.Null(analysis.CurrentJavaMajorVersion);
        Assert.Equal(3, analysis.Details.Count);
        Assert.Contains(analysis.Details, detail =>
            detail.ModName == "Better End"
            && detail.DependencyName == "bclib"
            && detail.Kind == LaunchFailureDetailKind.MissingDependency);
        Assert.Contains(analysis.Details, detail =>
            detail.ModName == "Statement Vanilla Compatibility Module"
            && detail.Kind == LaunchFailureDetailKind.IncompatibleMinecraftVersion);
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
    public void AnalyzerPrefersStructuredLatestLogOverGenericCurrentMarker()
    {
        const string stdout = "Incompatible mods found!";
        const string latestLog = """
            Incompatible mods found!
            More details:
              - Mod 'Iris' (iris) 1.0 requires any 0.7.x version of sodium, which is missing!
            """;

        var analysis = LaunchFailureAnalyzer.Analyze(CreateContext(), stdout, string.Empty, latestLog, []);

        Assert.NotNull(analysis);
        Assert.Equal(LaunchFailureCategory.ModDependencyMissing, analysis.Category);
        var detail = Assert.Single(analysis.Details);
        Assert.Equal("Iris", detail.ModName);
        Assert.Equal("sodium", detail.DependencyName);
    }

    [Fact]
    public void AnalyzerMergesSameCategoryDetailsFromCurrentOutputAndLatestLog()
    {
        const string stdout = "Mod 'First' (first) 1.0 requires any version of alpha, which is missing!";
        const string latestLog = "Mod 'Second' (second) 1.0 requires any version of beta, which is missing!";

        var analysis = LaunchFailureAnalyzer.Analyze(CreateContext(), stdout, string.Empty, latestLog, []);

        Assert.NotNull(analysis);
        Assert.Collection(
            analysis.Details,
            detail => Assert.Equal("First", detail.ModName),
            detail => Assert.Equal("Second", detail.ModName));
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
    public void AnalyzerExtractsFabricMissingDependencyReasonAndSuggestion()
    {
        const string latestLog = """
            Incompatible mods found!
            A potential solution has been determined, this may resolve your problem:
              - Install sodium, any 0.7.x version.
            More details:
              - Mod 'Iris' (iris) 1.9.6+mc1.21.8 requires any 0.7.x version of sodium, which is missing!
            """;

        var analysis = LaunchFailureAnalyzer.Analyze(CreateContext(), string.Empty, string.Empty, latestLog, []);

        Assert.NotNull(analysis);
        Assert.Equal(LaunchFailureCategory.ModDependencyMissing, analysis.Category);
        var detail = Assert.Single(analysis.Details);
        Assert.Equal(LaunchFailureDetailKind.MissingDependency, detail.Kind);
        Assert.Equal("Iris", detail.ModName);
        Assert.Equal("1.9.6+mc1.21.8", detail.ModVersion);
        Assert.Equal("sodium", detail.DependencyName);
        Assert.Equal("0.7.x", detail.RequiredVersion);
        Assert.Null(detail.CurrentVersion);
        Assert.Contains("which is missing", detail.OriginalReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Install sodium, any 0.7.x version.", detail.OriginalSuggestion);
        Assert.Contains(analysis.Evidence, evidence =>
            evidence.Kind == LaunchFailureEvidenceKind.Reason
            && evidence.Text.Contains("Iris", StringComparison.Ordinal));
        Assert.Contains(analysis.Evidence, evidence =>
            evidence.Kind == LaunchFailureEvidenceKind.Suggestion
            && evidence.Text.Contains("Install sodium", StringComparison.Ordinal));
    }

    [Fact]
    public void AnalyzerExtractsMultipleFabricWrongVersionDetails()
    {
        const string latestLog = """
            Incompatible mods found!
            A potential solution has been determined:
              - Replace mod 'Fabric Loader' (fabricloader) 0.14.21 with version 0.16.10 or later.
            Unmet dependency listing:
              - Mod 'Fabric API' (fabric-api) 0.92.5 requires version 0.16.10 or later of mod 'Fabric Loader' (fabricloader), but only the wrong version is present: 0.14.21!
              - Mod 'GeckoLib' (geckolib) 4.2.4 requires version 0.14.22 or later of mod 'Fabric Loader' (fabricloader), but only the wrong version is present: 0.14.21!
            """;

        var analysis = LaunchFailureAnalyzer.Analyze(CreateContext(), string.Empty, string.Empty, latestLog, []);

        Assert.NotNull(analysis);
        Assert.Equal(LaunchFailureCategory.ModVersionIncompatible, analysis.Category);
        Assert.Equal(2, analysis.Details.Count);
        Assert.All(analysis.Details, detail =>
        {
            Assert.Equal(LaunchFailureDetailKind.IncompatibleLoaderVersion, detail.Kind);
            Assert.Equal("Fabric Loader", detail.DependencyName);
            Assert.Equal("0.14.21", detail.CurrentVersion);
            Assert.Contains("Replace mod", detail.OriginalSuggestion);
        });
    }

    [Theory]
    [InlineData("not installed", LaunchFailureDetailKind.MissingDependency, null)]
    [InlineData("0.6.8.a", LaunchFailureDetailKind.IncompatibleDependencyVersion, "0.6.8.a")]
    public void AnalyzerExtractsForgeAndNeoForgeFailureMessage(
        string currentState,
        LaunchFailureDetailKind expectedKind,
        string? expectedCurrentVersion)
    {
        var latestLog = $"""
            Mod File: create.jar
            Failure message: Mod create requires flywheel 0.6.9 or above, and below 0.6.10
            Currently, flywheel is {currentState}
            Mod Issue URL: https://example.invalid
            """;

        var analysis = LaunchFailureAnalyzer.Analyze(CreateContext(), string.Empty, string.Empty, latestLog, []);

        Assert.NotNull(analysis);
        var detail = Assert.Single(analysis.Details);
        Assert.Equal(expectedKind, detail.Kind);
        Assert.Equal("create", detail.ModName);
        Assert.Equal("flywheel", detail.DependencyName);
        Assert.Equal("0.6.9 or above, and below 0.6.10", detail.RequiredVersion);
        Assert.Equal(expectedCurrentVersion, detail.CurrentVersion);
        Assert.Contains("Currently, flywheel", detail.OriginalReason);
    }

    [Fact]
    public void AnalyzerDeduplicatesRepeatedForgeFailureMessages()
    {
        const string failure = """
            Failure message: Mod craftingtweaks requires balm 21.11.2 or above
            Currently, balm is not installed
            """;
        var latestLog = $"{failure}{Environment.NewLine}{failure}";

        var analysis = LaunchFailureAnalyzer.Analyze(CreateContext(), string.Empty, string.Empty, latestLog, []);

        Assert.NotNull(analysis);
        var detail = Assert.Single(analysis.Details);
        Assert.Equal("craftingtweaks", detail.ModName);
        Assert.Equal("balm", detail.DependencyName);
    }

    [Fact]
    public void AnalyzerExtractsMultipleForgeFailuresWithSupportsAndRequiresWording()
    {
        const string latestLog = """
            Failure message: Mod irons_only only supports curios 5.14.1 or above
            Currently, curios is 5.6.1+1.20.1
            Failure message: Mod sdrp requires cloth_config 9.0.94 or above
            Currently, cloth_config is not installed
            """;

        var analysis = LaunchFailureAnalyzer.Analyze(CreateContext(), string.Empty, string.Empty, latestLog, []);

        Assert.NotNull(analysis);
        Assert.Equal(LaunchFailureCategory.ModDependencyMissing, analysis.Category);
        Assert.Collection(
            analysis.Details,
            detail =>
            {
                Assert.Equal(LaunchFailureDetailKind.IncompatibleDependencyVersion, detail.Kind);
                Assert.Equal("irons_only", detail.ModName);
                Assert.Equal("curios", detail.DependencyName);
                Assert.Equal("5.14.1 or above", detail.RequiredVersion);
                Assert.Equal("5.6.1+1.20.1", detail.CurrentVersion);
            },
            detail =>
            {
                Assert.Equal(LaunchFailureDetailKind.MissingDependency, detail.Kind);
                Assert.Equal("sdrp", detail.ModName);
                Assert.Equal("cloth_config", detail.DependencyName);
                Assert.Equal("9.0.94 or above", detail.RequiredVersion);
                Assert.Null(detail.CurrentVersion);
            });
    }

    [Fact]
    public void AnalyzerExtractsNeoForgeFailureWithoutFailureMessagePrefix()
    {
        const string latestLog = """
            Error loading mods
            1 error has occurred during loading
            Mod sodium_extra requires sodium 0.8.13+mc1.21.11 or above
            Currently, sodium is not installed
            """;

        var analysis = LaunchFailureAnalyzer.Analyze(
            CreateContext(loader: LoaderKind.NeoForge),
            string.Empty,
            string.Empty,
            latestLog,
            []);

        Assert.NotNull(analysis);
        Assert.Equal(LaunchFailureCategory.ModDependencyMissing, analysis.Category);
        var detail = Assert.Single(analysis.Details);
        Assert.Equal(LaunchFailureDetailKind.MissingDependency, detail.Kind);
        Assert.Equal("sodium_extra", detail.ModName);
        Assert.Equal("sodium", detail.DependencyName);
        Assert.Equal("0.8.13+mc1.21.11 or above", detail.RequiredVersion);
        Assert.Null(detail.CurrentVersion);
        Assert.Contains("Currently, sodium is not installed", detail.OriginalReason);
    }

    [Fact]
    public void AnalyzerDoesNotJoinForgeFailureWithAnotherFailureBlock()
    {
        const string latestLog = """
            Failure message: Mod first requires alpha 2.0 or above
            Mod Issue URL: https://example.invalid/first
            Failure message: Mod second requires beta 3.0 or above
            Currently, alpha is not installed
            """;

        var analysis = LaunchFailureAnalyzer.Analyze(CreateContext(), string.Empty, string.Empty, latestLog, []);

        Assert.NotNull(analysis);
        Assert.Empty(analysis.Details);
    }

    [Fact]
    public void AnalyzerFallbackDoesNotJoinUnrelatedLines()
    {
        const string latestLog = """
            Incompatible mods found!
            Mod 'First' reported an initialization problem.
            A later subsystem requires 'alpha'.
            Another unrelated value is missing.
            """;

        var analysis = LaunchFailureAnalyzer.Analyze(CreateContext(), string.Empty, string.Empty, latestLog, []);

        Assert.NotNull(analysis);
        Assert.Empty(analysis.Details);
    }

    [Fact]
    public void AnalyzerDoesNotAssociateSuggestionByIdentifierSubstring()
    {
        const string latestLog = """
            Incompatible mods found!
            A potential solution has been determined:
              - Install service_api 2.0 or later.
            More details:
              - Mod 'Example' (example) 1.0 requires any version of ice, which is missing!
            """;

        var analysis = LaunchFailureAnalyzer.Analyze(CreateContext(), string.Empty, string.Empty, latestLog, []);

        Assert.NotNull(analysis);
        var detail = Assert.Single(analysis.Details);
        Assert.Null(detail.OriginalSuggestion);
    }

    [Fact]
    public void AnalyzerKeepsHighSignalEvidenceWhenModConflictFormatIsUnknown()
    {
        const string latestLog = """
            Mod loading has failed
            Incompatible custom loader constraint for ExampleMod and Minecraft 1.21.7
            at example.Loader.start(Loader.java:12)
            """;

        var analysis = LaunchFailureAnalyzer.Analyze(CreateContext(), string.Empty, string.Empty, latestLog, []);

        Assert.NotNull(analysis);
        Assert.Equal(LaunchFailureCategory.ModVersionIncompatible, analysis.Category);
        Assert.Empty(analysis.Details);
        Assert.Contains(analysis.Evidence, evidence => evidence.Text.Contains("ExampleMod", StringComparison.Ordinal));
        Assert.DoesNotContain(analysis.Evidence, evidence => evidence.Text.StartsWith("at ", StringComparison.Ordinal));
    }

    [Fact]
    public void AnalyzerRedactsSensitiveValuesFromEvidence()
    {
        const string token = "secret-token-value";
        const string latestLog = """
            Incompatible mods found!
            A potential solution has been determined:
              - Install sodium --accessToken secret-token-value
            More details:
              - Mod 'Iris' (iris) 1.0 requires any version of sodium, which is missing!
            """;

        var analysis = LaunchFailureAnalyzer.Analyze(
            CreateContext([token]),
            string.Empty,
            string.Empty,
            latestLog,
            []);

        Assert.NotNull(analysis);
        Assert.DoesNotContain(analysis.Evidence, evidence => evidence.Text.Contains(token, StringComparison.Ordinal));
        Assert.Contains(analysis.Evidence, evidence => evidence.Text.Contains("<redacted>", StringComparison.Ordinal));
    }

    [Fact]
    public void AnalyzerExtractsQuiltRemoveSuggestion()
    {
        const string latestLog = """
            Incompatible mod set!
            A potential solution has been determined:
              - Remove mod 'Quilted API' (qsl) 7.0.0.
            More details:
              - Mod 'Quilted API' (qsl) 7.0.0 requires any 8.x version of quilt_loader, but only the wrong version is present: 0.26.0!
            """;

        var analysis = LaunchFailureAnalyzer.Analyze(
            CreateContext(loader: LoaderKind.Quilt),
            string.Empty,
            string.Empty,
            latestLog,
            []);

        Assert.NotNull(analysis);
        var detail = Assert.Single(analysis.Details);
        Assert.Equal(LaunchFailureDetailKind.IncompatibleLoaderVersion, detail.Kind);
        Assert.Equal("Remove mod 'Quilted API' (qsl) 7.0.0.", detail.OriginalSuggestion);
    }

    [Fact]
    public void AnalyzerDeduplicatesAndLimitsFallbackEvidence()
    {
        var repeatedLine = "Incompatible custom loader constraint " + new string('x', 1000);
        var latestLog = $"Mod loading has failed{Environment.NewLine}{repeatedLine}{Environment.NewLine}{repeatedLine}";

        var analysis = LaunchFailureAnalyzer.Analyze(CreateContext(), string.Empty, string.Empty, latestLog, []);

        Assert.NotNull(analysis);
        var evidence = Assert.Single(analysis.Evidence);
        Assert.Equal(801, evidence.Text.Length);
        Assert.EndsWith("…", evidence.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzerDoesNotThrowForOversizedAdversarialLine()
    {
        var latestLog = $"Incompatible mods found!{Environment.NewLine}Mod '{new string('x', 300_000)} requires an invalid dependency";

        var exception = Record.Exception(() =>
            LaunchFailureAnalyzer.Analyze(CreateContext(), string.Empty, string.Empty, latestLog, []));

        Assert.Null(exception);
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

    private LaunchDiagnosticContext CreateContext(
        IReadOnlyList<string>? sensitiveValues = null,
        LoaderKind loader = LoaderKind.Fabric)
    {
        return new LaunchDiagnosticContext(
            Path.Combine(TempRoot, ".minecraft"),
            Path.Combine(TempRoot, ".minecraft", "versions", "Example"),
            "instance",
            "Example",
            "Example",
            "1.21.9",
            loader,
            "0.19.3",
            @"C:\Java\bin\java.exe",
            "21.0.1",
            "Test",
            4096,
            sensitiveValues ?? []);
    }
}
