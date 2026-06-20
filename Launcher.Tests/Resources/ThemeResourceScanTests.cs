using System.Text.RegularExpressions;

namespace Launcher.Tests.Resources;

public sealed class ThemeResourceScanTests
{
    private static readonly Regex HexColorPattern = new("#[0-9A-Fa-f]{3,8}", RegexOptions.Compiled);
    private static readonly Regex StaticThemeResourcePattern = new(
        "StaticResource [^}]*Brush|StaticResource LauncherFontFamily|StaticResource LauncherIconFontFamily",
        RegexOptions.Compiled);
    private static readonly Regex SvgPaintColorPattern = new(
        "(fill|stroke)=\"#[0-9A-Fa-f]{3,8}\"",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact]
    public void XamlThemeColorsUseDynamicResourcesOutsideThemeDictionaries()
    {
        var appRoot = Path.Combine(FindRepositoryRoot(), "Launcher.App");
        var files = Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Resources{Path.DirectorySeparatorChar}Themes{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        var offenders = files
            .SelectMany(path => FindMatches(path, HexColorPattern))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void XamlThemeBrushAndFontResourcesUseDynamicResource()
    {
        var appRoot = Path.Combine(FindRepositoryRoot(), "Launcher.App");
        var files = Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Resources{Path.DirectorySeparatorChar}Themes{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        var offenders = files
            .SelectMany(path => FindMatches(path, StaticThemeResourcePattern))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void UiSvgIconsDoNotHardCodePaintColors()
    {
        var iconsRoot = Path.Combine(FindRepositoryRoot(), "Launcher.App", "Assets", "Icons");
        var files = Directory.EnumerateFiles(iconsRoot, "*.svg", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}block{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        var offenders = files
            .SelectMany(path => FindMatches(path, SvgPaintColorPattern))
            .ToArray();

        Assert.Empty(offenders);
    }

    private static IEnumerable<string> FindMatches(string path, Regex regex)
    {
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (regex.IsMatch(line))
                yield return $"{path}:{lineNumber}: {line.Trim()}";
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Launcher.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Launcher.sln from the test output directory.");
    }
}
