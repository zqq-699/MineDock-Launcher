using System.Globalization;
using System.Text.RegularExpressions;
using Launcher.App.Services;

namespace Launcher.Tests.Services;

public sealed class ProgressiveBlurSupportTests
{
    [Theory]
    [InlineData(0, true, true, false, nameof(ProgressiveBlurUnavailableReason.RenderingTierTooLow))]
    [InlineData(1, true, true, false, nameof(ProgressiveBlurUnavailableReason.RenderingTierTooLow))]
    [InlineData(2, false, true, false, nameof(ProgressiveBlurUnavailableReason.PixelShader30Unsupported))]
    [InlineData(2, true, false, false, nameof(ProgressiveBlurUnavailableReason.ShaderLoadFailed))]
    [InlineData(2, true, true, true, nameof(ProgressiveBlurUnavailableReason.ShaderRejected))]
    public void CapabilityEvaluatorReturnsExpectedFallbackReason(
        int renderingTier,
        bool pixelShader30Supported,
        bool shaderLoaded,
        bool shaderRejected,
        string expectedReason)
    {
        var result = ProgressiveBlurCapabilityEvaluator.Evaluate(
            renderingTier,
            pixelShader30Supported,
            shaderLoaded,
            shaderRejected);

        Assert.False(result.IsAvailable);
        Assert.Equal(expectedReason, result.UnavailableReason.ToString());
    }

    [Fact]
    public void CapabilityEvaluatorEnablesLoadedShaderOnTierTwo()
    {
        var result = ProgressiveBlurCapabilityEvaluator.Evaluate(
            renderingTier: 2,
            isPixelShader30Supported: true,
            isShaderLoaded: true,
            isShaderRejected: false);

        Assert.True(result.IsAvailable);
        Assert.Equal(ProgressiveBlurUnavailableReason.None, result.UnavailableReason);
    }

    [Fact]
    public void CompiledShaderUsesPixelShaderModelThreeAndBranchesBeforeLodSamples()
    {
        var shaderPath = Path.Combine(
            FindSolutionDirectory().FullName,
            "Launcher.App",
            "Effects",
            "Shaders",
            "ProgressiveGaussianBlur.ps");

        var bytecode = File.ReadAllBytes(shaderPath);

        Assert.True(bytecode.Length > 4);
        Assert.Equal([0x00, 0x03, 0xFF, 0xFF], bytecode[..4]);

        var opcodes = ReadShaderOpcodes(bytecode);
        // Direct3D 9 shader bytecode opcodes used to verify the compiled control flow.
        const ushort ifOpcode = 40;
        const ushort ifCompareOpcode = 41;
        const ushort elseOpcode = 42;
        const ushort endIfOpcode = 43;
        const ushort implicitLodTextureOpcode = 66;
        const ushort explicitLodTextureOpcode = 95;

        var branchIndex = opcodes.FindIndex(opcode => opcode is ifOpcode or ifCompareOpcode);
        var elseIndex = opcodes.FindIndex(opcode => opcode == elseOpcode);
        var endIfIndex = opcodes.FindIndex(opcode => opcode == endIfOpcode);
        var explicitLodSampleIndices = opcodes
            .Select((opcode, index) => (opcode, index))
            .Where(item => item.opcode == explicitLodTextureOpcode)
            .Select(item => item.index)
            .ToArray();

        Assert.True(branchIndex > 0);
        Assert.True(elseIndex > branchIndex);
        Assert.True(endIfIndex > elseIndex);
        Assert.Equal(19, explicitLodSampleIndices.Length);
        Assert.Single(explicitLodSampleIndices.Where(index => index < branchIndex));
        Assert.Equal(18, explicitLodSampleIndices.Count(index => index > elseIndex && index < endIfIndex));
        Assert.DoesNotContain(implicitLodTextureOpcode, opcodes);
    }

    [Fact]
    public void ShaderKernelIsNormalizedSymmetricAndClampsAllSamples()
    {
        var shaderSourcePath = Path.Combine(
            FindSolutionDirectory().FullName,
            "Launcher.App",
            "Effects",
            "Shaders",
            "ProgressiveGaussianBlur.fx");
        var source = File.ReadAllText(shaderSourcePath);
        var centerWeight = ReadShaderWeight(source, "centerWeight");
        var pairedWeights = Enumerable.Range(1, 9)
            .Select(index => ReadShaderWeight(source, $"weight{index}"))
            .ToArray();

        Assert.Equal(1d, centerWeight + (2d * pairedWeights.Sum()), 7);
        for (var index = 1; index <= 9; index++)
            Assert.Equal(3, Regex.Matches(source, $@"\bweight{index}\b").Count);

        Assert.Equal(20, Regex.Matches(source, @"\bSampleInput\s*\(").Count);
        Assert.Single(Regex.Matches(source, @"\btex2Dlod\s*\("));
        Assert.Contains("float2 sampleUv = ClampSampleCoordinate(uv, halfTexel);", source, StringComparison.Ordinal);
        Assert.Contains("(localRadius / 9.0)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AllScrollableRightContentFramesOptIntoProgressiveBlur()
    {
        var solutionDirectory = FindSolutionDirectory().FullName;
        var viewsDirectory = Path.Combine(solutionDirectory, "Launcher.App", "Views");
        var listPageFrameViews = Directory
            .EnumerateFiles(viewsDirectory, "*.xaml", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("<controls:ListPageFrame", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(5, listPageFrameViews.Length);
        Assert.All(listPageFrameViews, path => Assert.Contains(
            "IsProgressiveBlurEnabled=\"{DynamicResource Is.ProgressiveBlur.Enabled}\"",
            File.ReadAllText(path),
            StringComparison.Ordinal));

        var expectedListPageFrameViews = new[]
        {
            Path.Combine("Views", "Download", "DownloadPageView.xaml"),
            Path.Combine("Views", "GameSettings", "GameSettingsPageView.xaml"),
            Path.Combine("Views", "Install", "InstallPageView.xaml"),
            Path.Combine("Views", "Resources", "ResourcesPageView.xaml"),
            Path.Combine("Views", "Settings", "SettingsPageView.xaml")
        };
        foreach (var expectedPath in expectedListPageFrameViews)
        {
            Assert.Contains(listPageFrameViews, path => path.EndsWith(
                expectedPath,
                StringComparison.OrdinalIgnoreCase));
        }

        var accountPageXaml = File.ReadAllText(Path.Combine(
            viewsDirectory,
            "Account",
            "AccountPageView.xaml"));
        Assert.Contains(
            "IsProgressiveBlurEnabled=\"{DynamicResource Is.ProgressiveBlur.Enabled}\"",
            accountPageXaml,
            StringComparison.Ordinal);

        var secondaryMenuFrameXaml = File.ReadAllText(Path.Combine(
            solutionDirectory,
            "Launcher.App",
            "Controls",
            "Navigation",
            "SecondaryMenuFrame.xaml"));
        Assert.DoesNotContain("IsProgressiveBlurEnabled", secondaryMenuFrameXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void BlurBandDoesNotDuplicateListsAndOptInListsRemainRecycling()
    {
        var solutionDirectory = FindSolutionDirectory().FullName;
        var listPageFrameXaml = File.ReadAllText(Path.Combine(
            solutionDirectory,
            "Launcher.App",
            "Controls",
            "Lists",
            "ListPageFrame.xaml"));
        var resourcesListXaml = File.ReadAllText(Path.Combine(
            solutionDirectory,
            "Launcher.App",
            "Views",
            "Resources",
            "ResourcesModPageView.xaml"));
        var settingsPreviewXaml = File.ReadAllText(Path.Combine(
            solutionDirectory,
            "Launcher.App",
            "Views",
            "Settings",
            "ListPreviewSettingsView.xaml"));

        Assert.Single(Regex.Matches(listPageFrameXaml, @"<VisualBrush\b"));
        Assert.DoesNotContain("<ListBox", listPageFrameXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ListView", listPageFrameXaml, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\"", resourcesListXaml, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\"", settingsPreviewXaml, StringComparison.Ordinal);
    }

    private static DirectoryInfo FindSolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Launcher.sln")))
                return directory;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Launcher.sln from the test output directory.");
    }

    private static List<ushort> ReadShaderOpcodes(byte[] bytecode)
    {
        Assert.True(bytecode.Length % sizeof(uint) == 0);

        var tokens = new uint[bytecode.Length / sizeof(uint)];
        Buffer.BlockCopy(bytecode, 0, tokens, 0, bytecode.Length);
        var opcodes = new List<ushort>();
        var tokenIndex = 1;

        while (tokenIndex < tokens.Length)
        {
            var instructionToken = tokens[tokenIndex];
            var opcode = (ushort)(instructionToken & 0xFFFFu);
            if (opcode == 0xFFFF)
                break;

            if (opcode == 0xFFFE)
            {
                var commentLength = (int)((instructionToken >> 16) & 0x7FFFu);
                tokenIndex += 1 + commentLength;
                continue;
            }

            opcodes.Add(opcode);
            var operandLength = (int)((instructionToken >> 24) & 0x0Fu);
            tokenIndex += 1 + operandLength;
        }

        Assert.True(tokenIndex < tokens.Length);
        Assert.Equal(0xFFFFu, tokens[tokenIndex] & 0xFFFFu);
        return opcodes;
    }

    private static double ReadShaderWeight(string source, string name)
    {
        var match = Regex.Match(
            source,
            $@"const\s+float\s+{Regex.Escape(name)}\s*=\s*(?<value>\d+(?:\.\d+)?);");
        Assert.True(match.Success, $"Shader weight '{name}' was not found.");
        return double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
    }
}
