/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Windows;
using System.Xml.Linq;
using Launcher.App.Behaviors;

namespace Launcher.Tests.Resources;

public sealed class ControlStyleHeightContractTests
{
    private const string SharedHeightKey = "LauncherCompactControlHeight";
    private const string SharedHeightReference = "{StaticResource " + SharedHeightKey + "}";
    private const string CardSurfaceReference = "{DynamicResource Brush.Card.Surface}";
    private const string CardBorderReference = "{DynamicResource Brush.Card.Border}";
    private const string PageBackgroundReference = "{DynamicResource Brush.Page.Background}";
    private const string CardShadowReference = "{DynamicResource Effect.Card.Surface}";

    [Fact]
    public void ButtonsAndReadOnlyFieldsShareCompactControlHeight()
    {
        var shared = LoadAppXaml("Resources", "Themes", "Shared.xaml");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var height = Assert.Single(shared.Descendants()
            .Where(element => element.Attribute(xaml + "Key")?.Value == SharedHeightKey));
        Assert.Equal("34", height.Value);

        var dialogs = LoadAppXaml("Styles", "ControlStyles.Dialogs.xaml");
        var dialogButtonStyle = FindStyle(dialogs, "DialogButtonStyle", xaml);
        AssertSetter(dialogButtonStyle, "Height", SharedHeightReference);

        var page = LoadAppXaml("Styles", "ControlStyles.Page.xaml");
        var readOnlyFieldStyle = FindStyle(page, "ReadOnlyFieldSurfaceStyle", xaml);
        AssertSetter(readOnlyFieldStyle, "MinHeight", SharedHeightReference);
        Assert.DoesNotContain(readOnlyFieldStyle.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "Height");
    }

    [Fact]
    public void ReadOnlyFieldUsagesDoNotOverrideAdaptiveHeight()
    {
        var root = FindRepositoryRoot();
        var appRoot = Path.Combine(root.FullName, "Launcher.App");
        var files = Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}Views{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || path.Contains($"{Path.DirectorySeparatorChar}Controls{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        foreach (var file in files)
        {
            var document = XDocument.Load(file);
            var fields = document.Descendants()
                .Where(element => element.Attribute("Style")?.Value == "{StaticResource ReadOnlyFieldSurfaceStyle}");
            foreach (var field in fields)
            {
                Assert.Null(field.Attribute("Height"));
                Assert.Null(field.Attribute("MinHeight"));
            }
        }
    }

    [Fact]
    public void DisplayInputDownloadTaskAndOrdinaryButtonSurfacesUseCardVisuals()
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var page = LoadAppXaml("Styles", "ControlStyles.Page.xaml");
        var sectionFieldStyle = FindStyle(page, "SectionFieldSurfaceStyle", xaml);
        var readOnlyFieldStyle = FindStyle(page, "ReadOnlyFieldSurfaceStyle", xaml);
        AssertSetter(sectionFieldStyle, "Effect", CardShadowReference);
        AssertSetter(readOnlyFieldStyle, "Background", CardSurfaceReference);
        AssertSetter(readOnlyFieldStyle, "BorderBrush", CardBorderReference);
        Assert.Equal("{StaticResource SectionFieldSurfaceStyle}", readOnlyFieldStyle.Attribute("BasedOn")?.Value);

        var inputs = LoadAppXaml("Styles", "ControlStyles.Inputs.xaml");
        var textBoxStyle = FindStyle(inputs, "DialogTextBoxStyle", xaml);
        var passwordBoxStyle = FindStyle(inputs, "DialogPasswordBoxStyle", xaml);
        AssertAllBackgroundSettersUseCardSurface(textBoxStyle);
        AssertAllBackgroundSettersUseCardSurface(passwordBoxStyle);
        AssertSetter(textBoxStyle, "BorderBrush", CardBorderReference);
        AssertSetter(passwordBoxStyle, "BorderBrush", CardBorderReference);
        AssertSetter(textBoxStyle, "Effect", CardShadowReference);
        AssertSetter(passwordBoxStyle, "Effect", CardShadowReference);
        var textContentHost = Assert.Single(textBoxStyle.Descendants()
            .Where(element => element.Name.LocalName == "ScrollViewer"
                && element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Name"
                    && attribute.Value == "PART_ContentHost")));
        Assert.Null(textContentHost.Attribute("Margin"));
        AssertUsesOrdinaryButtonHoverAnimation(textBoxStyle, xaml);
        AssertUsesOrdinaryButtonHoverAnimation(passwordBoxStyle, xaml);
        AssertUsesOrdinaryButtonHoverAnimation(
            FindStyle(inputs, "LauncherComboBoxToggleButtonStyle", xaml),
            xaml);
        var comboBoxStyle = FindStyle(inputs, "LauncherComboBoxStyle", xaml);
        AssertSetter(comboBoxStyle, "Background", CardSurfaceReference);
        AssertSetter(comboBoxStyle, "Effect", CardShadowReference);

        var lists = LoadAppXaml("Styles", "ControlStyles.Lists.xaml");
        AssertSetter(FindStyle(lists, "InstallTaskCardStyle", xaml), "Background", CardSurfaceReference);

        var dialogs = LoadAppXaml("Styles", "ControlStyles.Dialogs.xaml");
        var dialogButtonStyle = FindStyle(dialogs, "DialogButtonStyle", xaml);
        AssertSetter(dialogButtonStyle, "Background", CardSurfaceReference);
        AssertSetter(dialogButtonStyle, "Effect", CardShadowReference);
        AssertSetter(FindStyle(dialogs, "DialogSurfaceStyle", xaml), "Background", PageBackgroundReference);
    }

    [Fact]
    public void CardSurfaceSuppressesNestedControlShadows()
    {
        var metadata = Assert.IsType<FrameworkPropertyMetadata>(
            SurfaceShadow.SuppressChildShadowsProperty.GetMetadata(typeof(FrameworkElement)));
        Assert.True(metadata.Inherits);

        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var page = LoadAppXaml("Styles", "ControlStyles.Page.xaml");
        var sectionFieldStyle = FindStyle(page, "SectionFieldSurfaceStyle", xaml);
        AssertSetter(sectionFieldStyle, "behaviors:SurfaceShadow.SuppressChildShadows", "True");
        AssertShadowSuppressionTrigger(sectionFieldStyle, usesAncestorBinding: true);

        var inputs = LoadAppXaml("Styles", "ControlStyles.Inputs.xaml");
        AssertShadowSuppressionTrigger(FindStyle(inputs, "DialogTextBoxStyle", xaml));
        AssertShadowSuppressionTrigger(FindStyle(inputs, "DialogPasswordBoxStyle", xaml));
        AssertShadowSuppressionTrigger(FindStyle(inputs, "LauncherComboBoxStyle", xaml));

        var dialogs = LoadAppXaml("Styles", "ControlStyles.Dialogs.xaml");
        AssertShadowSuppressionTrigger(FindStyle(dialogs, "DialogButtonStyle", xaml));
    }

    private static XElement FindStyle(XDocument document, string key, XNamespace xaml) =>
        Assert.Single(document.Descendants()
            .Where(element => element.Name.LocalName == "Style"
                && element.Attribute(xaml + "Key")?.Value == key));

    private static void AssertSetter(XElement style, string property, string value) =>
        Assert.Contains(style.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == property
            && element.Attribute("Value")?.Value == value);

    private static void AssertAllBackgroundSettersUseCardSurface(XElement style)
    {
        var backgroundSetters = style.DescendantsAndSelf()
            .Where(element => element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value == "Background")
            .ToArray();

        Assert.NotEmpty(backgroundSetters);
        Assert.All(backgroundSetters, setter =>
            Assert.Equal(CardSurfaceReference, setter.Attribute("Value")?.Value));
    }

    private static void AssertShadowSuppressionTrigger(XElement style, bool usesAncestorBinding = false)
    {
        var trigger = Assert.Single(style.Descendants()
            .Where(element => element.Name.LocalName == (usesAncestorBinding ? "DataTrigger" : "Trigger"))
            .Where(element => usesAncestorBinding
                ? element.Attribute("Binding")?.Value.Contains("SurfaceShadow.SuppressChildShadows", StringComparison.Ordinal) == true
                : element.Attribute("Property")?.Value == "behaviors:SurfaceShadow.SuppressChildShadows"));

        Assert.Contains(trigger.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "Effect"
            && element.Attribute("Value")?.Value == "{x:Null}");
    }

    private static void AssertUsesOrdinaryButtonHoverAnimation(XElement style, XNamespace xaml)
    {
        var hoverBackground = Assert.Single(style.Descendants()
            .Where(element => element.Name.LocalName == "Border"
                && element.Attribute(xaml + "Name")?.Value == "HoverBackground"));
        Assert.Equal("{DynamicResource Brush.Button.Secondary.Hover}", hoverBackground.Attribute("Background")?.Value);
        Assert.Equal("0", hoverBackground.Attribute("Opacity")?.Value);

        var hoverAnimations = style.Descendants()
            .Where(element => element.Name.LocalName == "DoubleAnimation"
                && element.Attribute("Storyboard.TargetName")?.Value == "HoverBackground"
                && element.Attribute("Storyboard.TargetProperty")?.Value == "Opacity")
            .ToArray();

        Assert.Contains(hoverAnimations, animation =>
            animation.Attribute("To")?.Value == "1"
            && animation.Attribute("Duration")?.Value == "0:0:0.16");
        Assert.Contains(hoverAnimations, animation =>
            animation.Attribute("To")?.Value == "0"
            && animation.Attribute("Duration")?.Value == "0:0:0.22");
    }

    private static XDocument LoadAppXaml(params string[] pathParts) =>
        XDocument.Load(Path.Combine([FindRepositoryRoot().FullName, "Launcher.App", .. pathParts]));

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        return root;
    }
}
