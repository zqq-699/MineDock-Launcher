using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Launcher.App.Resources;

namespace Launcher.Tests.Resources;

[Collection(WpfTestCollection.Name)]
public sealed class StringResourceTests
{
    private static readonly Regex PlaceholderPattern = new(@"\{\d+(?:[^}]*)\}", RegexOptions.CultureInvariant);
    private static readonly Regex CjkPattern = new(@"[\u3400-\u9fff]", RegexOptions.CultureInvariant);
    private static readonly Regex ModTermPattern = new(@"(?i)(?<![A-Za-z])Mods?(?![A-Za-z])", RegexOptions.CultureInvariant);

    private static readonly Regex MechanicalEnglishPattern = new(
        @"^(Settings|Home|Download|Game Settings|Account|Resources|Section|Detail|Nav|Mod Details|Launch Analysis|Local Import|Loader Version|Skin Model|Skin Manager|Rename Account|Add Account|Delete Account)\b.*\b(Button|Label|Description|Section|Details|Downloads|Subtitle|Filter|Source|Mods|General|Resource Packs|Title|Message|Detail|Recommendation|State|Empty|Format|Task|Pending)$",
        RegexOptions.CultureInvariant);

    private static readonly Regex MechanicalPhrasePattern = new(
        @"\b(Subtitle|Message Format|Detail Format|Recommendation Format)\b",
        RegexOptions.CultureInvariant);

    private static readonly HashSet<string> NativeLanguageNameKeys =
    [
        "Settings_LanguageSimplifiedChinese",
        "Settings_LanguageTraditionalChinese",
        "Settings_LanguageJapanese",
        "Settings_LanguageEnglish"
    ];

    [Fact]
    public void EnglishResourceKeysMatchDefaultResourceKeys()
    {
        var defaultEntries = LoadResourceEntries("Strings.resx");
        var englishEntries = LoadResourceEntries("Strings.en.resx");

        Assert.Empty(defaultEntries.Keys.Except(englishEntries.Keys).Order());
        Assert.Empty(englishEntries.Keys.Except(defaultEntries.Keys).Order());
        Assert.Equal(defaultEntries.Count, englishEntries.Count);
    }

    [Fact]
    public void SimplifiedChineseResourceKeysMatchDefaultResourceKeys()
    {
        var defaultEntries = LoadResourceEntries("Strings.resx");
        var simplifiedChineseEntries = LoadResourceEntries("Strings.zh-Hans.resx");

        Assert.Empty(defaultEntries.Keys.Except(simplifiedChineseEntries.Keys).Order());
        Assert.Empty(simplifiedChineseEntries.Keys.Except(defaultEntries.Keys).Order());
        Assert.Equal(defaultEntries.Count, simplifiedChineseEntries.Count);
    }

    [Fact]
    public void TraditionalChineseResourceKeysMatchDefaultResourceKeys()
    {
        var defaultEntries = LoadResourceEntries("Strings.resx");
        var traditionalChineseEntries = LoadResourceEntries("Strings.zh-Hant.resx");

        Assert.Empty(defaultEntries.Keys.Except(traditionalChineseEntries.Keys).Order());
        Assert.Empty(traditionalChineseEntries.Keys.Except(defaultEntries.Keys).Order());
        Assert.Equal(defaultEntries.Count, traditionalChineseEntries.Count);
    }

    [Fact]
    public void JapaneseResourceKeysMatchDefaultResourceKeys()
    {
        var defaultEntries = LoadResourceEntries("Strings.resx");
        var japaneseEntries = LoadResourceEntries("Strings.ja-JP.resx");

        Assert.Empty(defaultEntries.Keys.Except(japaneseEntries.Keys).Order());
        Assert.Empty(japaneseEntries.Keys.Except(defaultEntries.Keys).Order());
        Assert.Equal(defaultEntries.Count, japaneseEntries.Count);
    }

    [Fact]
    public void EnglishResourcePlaceholdersMatchDefaultResource()
    {
        var defaultEntries = LoadResourceEntries("Strings.resx");
        var englishEntries = LoadResourceEntries("Strings.en.resx");

        var mismatches = defaultEntries
            .Select(entry => new
            {
                Key = entry.Key,
                DefaultPlaceholders = ExtractPlaceholders(entry.Value),
                EnglishPlaceholders = ExtractPlaceholders(englishEntries[entry.Key])
            })
            .Where(entry => !entry.DefaultPlaceholders.SequenceEqual(entry.EnglishPlaceholders))
            .Select(entry => $"{entry.Key}: [{string.Join(", ", entry.DefaultPlaceholders)}] != [{string.Join(", ", entry.EnglishPlaceholders)}]")
            .ToArray();

        Assert.Empty(mismatches);
    }

    [Fact]
    public void SimplifiedChineseResourcePlaceholdersMatchDefaultResource()
    {
        var defaultEntries = LoadResourceEntries("Strings.resx");
        var simplifiedChineseEntries = LoadResourceEntries("Strings.zh-Hans.resx");

        var mismatches = defaultEntries
            .Select(entry => new
            {
                Key = entry.Key,
                DefaultPlaceholders = ExtractPlaceholders(entry.Value),
                SimplifiedChinesePlaceholders = ExtractPlaceholders(simplifiedChineseEntries[entry.Key])
            })
            .Where(entry => !entry.DefaultPlaceholders.SequenceEqual(entry.SimplifiedChinesePlaceholders))
            .Select(entry => $"{entry.Key}: [{string.Join(", ", entry.DefaultPlaceholders)}] != [{string.Join(", ", entry.SimplifiedChinesePlaceholders)}]")
            .ToArray();

        Assert.Empty(mismatches);
    }

    [Fact]
    public void TraditionalChineseResourcePlaceholdersMatchDefaultResource()
    {
        var defaultEntries = LoadResourceEntries("Strings.resx");
        var traditionalChineseEntries = LoadResourceEntries("Strings.zh-Hant.resx");

        var mismatches = defaultEntries
            .Select(entry => new
            {
                Key = entry.Key,
                DefaultPlaceholders = ExtractPlaceholders(entry.Value),
                TraditionalChinesePlaceholders = ExtractPlaceholders(traditionalChineseEntries[entry.Key])
            })
            .Where(entry => !entry.DefaultPlaceholders.SequenceEqual(entry.TraditionalChinesePlaceholders))
            .Select(entry => $"{entry.Key}: [{string.Join(", ", entry.DefaultPlaceholders)}] != [{string.Join(", ", entry.TraditionalChinesePlaceholders)}]")
            .ToArray();

        Assert.Empty(mismatches);
    }

    [Fact]
    public void JapaneseResourcePlaceholdersMatchDefaultResource()
    {
        var defaultEntries = LoadResourceEntries("Strings.resx");
        var japaneseEntries = LoadResourceEntries("Strings.ja-JP.resx");

        var mismatches = defaultEntries
            .Select(entry => new
            {
                Key = entry.Key,
                DefaultPlaceholders = ExtractPlaceholders(entry.Value),
                JapanesePlaceholders = ExtractPlaceholders(japaneseEntries[entry.Key])
            })
            .Where(entry => !entry.DefaultPlaceholders.SequenceEqual(entry.JapanesePlaceholders))
            .Select(entry => $"{entry.Key}: [{string.Join(", ", entry.DefaultPlaceholders)}] != [{string.Join(", ", entry.JapanesePlaceholders)}]")
            .ToArray();

        Assert.Empty(mismatches);
    }

    [Theory]
    [InlineData("Strings.resx", "模组")]
    [InlineData("Strings.zh-Hans.resx", "模组")]
    [InlineData("Strings.zh-Hant.resx", "模組")]
    public void ChineseResourcesUseLocalizedModTerminology(string fileName, string localizedTerm)
    {
        var entries = LoadResourceEntries(fileName);
        var spacedTermPattern = new Regex(
            $" {Regex.Escape(localizedTerm)}|{Regex.Escape(localizedTerm)} ",
            RegexOptions.CultureInvariant);

        var offenders = entries
            .Where(entry => ModTermPattern.IsMatch(entry.Value) || spacedTermPattern.IsMatch(entry.Value))
            .Select(entry => $"{entry.Key}={entry.Value}")
            .Order()
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void EnglishResourceValuesAreUserFacingText()
    {
        var englishEntries = LoadResourceEntries("Strings.en.resx");

        var offenders = englishEntries
            .Where(entry => !NativeLanguageNameKeys.Contains(entry.Key))
            .Where(entry =>
                string.Equals(entry.Key, entry.Value, StringComparison.Ordinal) ||
                CjkPattern.IsMatch(entry.Value) ||
                MechanicalEnglishPattern.IsMatch(entry.Value) ||
                MechanicalPhrasePattern.IsMatch(entry.Value))
            .Select(entry => $"{entry.Key}={entry.Value}")
            .Order()
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void EnglishResourceContainsExpectedUserFacingSamples()
    {
        var englishEntries = LoadResourceEntries("Strings.en.resx");

        Assert.Equal("Settings", englishEntries["Page_Settings"]);
        Assert.Equal("Launch", englishEntries["Launch_Button"]);
        Assert.Equal("Check for Updates", englishEntries["Settings_CheckUpdatesButton"]);
        Assert.Equal("An instance with this name already exists.", englishEntries["Status_DuplicateInstanceName"]);
        Assert.Equal("Choose the account type to add.", englishEntries["Dialog_AddAccountSubtitle"]);
        Assert.Equal("Release", englishEntries["Download_ReleaseCategory"]);
        Assert.Equal("Supported Versions", englishEntries["Resources_ModDetailsVersionLabel"]);
        Assert.Equal("General", englishEntries["GameSettings_DetailGeneral"]);
        Assert.Equal("简体中文", englishEntries["Settings_LanguageSimplifiedChinese"]);
        Assert.Equal("繁體中文", englishEntries["Settings_LanguageTraditionalChinese"]);
        Assert.Equal("日本語", englishEntries["Settings_LanguageJapanese"]);
        Assert.Equal("English", englishEntries["Settings_LanguageEnglish"]);
        Assert.Equal("The language will take effect after restarting the app.", englishEntries["Settings_LanguageRestartNotice"]);
        Assert.Equal("Delete save \"{0}\"? This cannot be undone.", englishEntries["Dialog_DeleteSingleSaveMessageFormat"]);
    }

    [Fact]
    public void EnglishCultureUsesEnglishResource()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en");
            CultureInfo.CurrentUICulture = new CultureInfo("en");

            Assert.Equal("Settings", Strings.Page_Settings);
            Assert.Equal("Launch", Strings.Launch_Button);
            Assert.Equal("Check for Updates", Strings.Settings_CheckUpdatesButton);
            Assert.Equal("An instance with this name already exists.", Strings.Status_DuplicateInstanceName);
            Assert.Equal("Choose the account type to add.", Strings.Dialog_AddAccountSubtitle);
            Assert.Equal("Supported Versions", Strings.Resources_ModDetailsVersionLabel);
            Assert.Equal("简体中文", Strings.Settings_LanguageSimplifiedChinese);
            Assert.Equal("繁體中文", Strings.Settings_LanguageTraditionalChinese);
            Assert.Equal("日本語", Strings.Settings_LanguageJapanese);
            Assert.Equal("English", Strings.Settings_LanguageEnglish);
            Assert.Equal("The language will take effect after restarting the app.", Strings.Settings_LanguageRestartNotice);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void SimplifiedChineseCultureUsesSimplifiedChineseResource()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("zh-Hans");
            CultureInfo.CurrentUICulture = new CultureInfo("zh-Hans");

            Assert.Equal("全局设置", Strings.Page_Settings);
            Assert.Equal("启动游戏", Strings.Launch_Button);
            Assert.Equal("已存在同名游戏", Strings.Status_DuplicateInstanceName);
            Assert.Equal("简体中文", Strings.Settings_LanguageSimplifiedChinese);
            Assert.Equal("繁體中文", Strings.Settings_LanguageTraditionalChinese);
            Assert.Equal("日本語", Strings.Settings_LanguageJapanese);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void TraditionalChineseCultureUsesTraditionalChineseResource()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("zh-Hant");
            CultureInfo.CurrentUICulture = new CultureInfo("zh-Hant");

            Assert.Equal("全域設定", Strings.Page_Settings);
            Assert.Equal("啟動遊戲", Strings.Launch_Button);
            Assert.Equal("已存在同名遊戲", Strings.Status_DuplicateInstanceName);
            Assert.Equal("简体中文", Strings.Settings_LanguageSimplifiedChinese);
            Assert.Equal("繁體中文", Strings.Settings_LanguageTraditionalChinese);
            Assert.Equal("日本語", Strings.Settings_LanguageJapanese);
            Assert.Equal("語言將在重新啟動應用程式後生效。", Strings.Settings_LanguageRestartNotice);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void JapaneseCultureUsesJapaneseResource()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ja-JP");
            CultureInfo.CurrentUICulture = new CultureInfo("ja-JP");

            Assert.Equal("グローバル設定", Strings.Page_Settings);
            Assert.Equal("ゲームを起動", Strings.Launch_Button);
            Assert.Equal("同名のゲームが既に存在します", Strings.Status_DuplicateInstanceName);
            Assert.Equal("简体中文", Strings.Settings_LanguageSimplifiedChinese);
            Assert.Equal("日本語", Strings.Settings_LanguageJapanese);
            Assert.Equal("言語はアプリの再起動後に有効になります。", Strings.Settings_LanguageRestartNotice);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private static IReadOnlyDictionary<string, string> LoadResourceEntries(string fileName)
    {
        var resourcePath = FindRepositoryRoot()
            .GetDirectories("Launcher.App", SearchOption.TopDirectoryOnly)
            .Single()
            .GetDirectories("Resources", SearchOption.TopDirectoryOnly)
            .Single()
            .GetFiles(fileName, SearchOption.TopDirectoryOnly)
            .Single()
            .FullName;

        return XDocument.Load(resourcePath)
            .Root!
            .Elements("data")
            .ToDictionary(
                element => element.Attribute("name")!.Value,
                element => element.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
    }

    private static string[] ExtractPlaceholders(string value)
    {
        return PlaceholderPattern.Matches(value)
            .Select(match => match.Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (directory.GetFiles("Launcher.sln", SearchOption.TopDirectoryOnly).Length == 1)
            {
                return directory;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
