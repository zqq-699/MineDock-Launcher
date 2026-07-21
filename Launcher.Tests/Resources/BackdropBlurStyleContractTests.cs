/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Xml.Linq;

namespace Launcher.Tests.Resources;

public sealed class BackdropBlurStyleContractTests
{
    [Fact]
    public void ControlStylesMergesBackdropEffectsDictionary()
    {
        var document = LoadAppXaml("Styles", "ControlStyles.xaml");
        var sources = document.Descendants()
            .Where(element => element.Name.LocalName == "ResourceDictionary")
            .Select(element => element.Attribute("Source")?.Value)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Cast<string>()
            .ToArray();

        Assert.Contains(
            "pack://application:,,,/BlockHelm_Launcher_x64;component/Styles/ControlStyles.Effects.xaml",
            sources);
    }

    [Fact]
    public void BackdropTemplateSamplesSharedPreblurredSourceAndKeepsForegroundLayers()
    {
        var document = LoadAppXaml("Styles", "ControlStyles.Effects.xaml");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var namedStyle = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "Style"
            && element.Attribute(xaml + "Key")?.Value == "BackdropBlurBorderStyle"));
        Assert.Equal("{x:Type controls:BackdropBlurBorder}", namedStyle.Attribute("TargetType")?.Value);
        Assert.Contains(namedStyle.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "SourceElement"
            && element.Attribute("Value")?.Value ==
                "{Binding LauncherPreblurredBackdropSourceElement, RelativeSource={RelativeSource AncestorType={x:Type Window}}}");
        Assert.Contains(namedStyle.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "IsSourcePreblurred"
            && element.Attribute("Value")?.Value == "True");

        var template = Assert.Single(namedStyle.Descendants().Where(element =>
            element.Name.LocalName == "ControlTemplate"));
        var root = Assert.Single(template.Elements().Where(element => element.Name.LocalName == "Grid"));
        Assert.Equal("True", root.Attribute("ClipToBounds")?.Value);
        var layers = root.Elements()
            .Where(element => element.Name.LocalName == "Border")
            .Select(element => element.Attribute(xaml + "Name")?.Value)
            .ToArray();
        Assert.Equal(
            ["PART_BaseLayer", "PART_BlurLayer", "PART_TintLayer", "PART_OverlayLayer", "PART_ContentLayer"],
            layers);

        var blurLayer = Assert.Single(root.Elements().Where(element =>
            element.Attribute(xaml + "Name")?.Value == "PART_BlurLayer"));
        Assert.Equal("False", blurLayer.Attribute("IsHitTestVisible")?.Value);
        Assert.Equal("Collapsed", blurLayer.Attribute("Visibility")?.Value);
        Assert.Null(blurLayer.Attribute("CornerRadius"));

        var brush = Assert.Single(blurLayer.Descendants().Where(element =>
            element.Name.LocalName == "VisualBrush"));
        Assert.Equal("Absolute", brush.Attribute("ViewboxUnits")?.Value);
        Assert.Equal("Absolute", brush.Attribute("ViewportUnits")?.Value);
        Assert.Equal("FlipXY", brush.Attribute("TileMode")?.Value);

        var effect = Assert.Single(blurLayer.Descendants().Where(element =>
            element.Name.LocalName == "BlurEffect"));
        Assert.Equal("Gaussian", effect.Attribute("KernelType")?.Value);
        Assert.Equal(
            "{Binding BlurRadius, RelativeSource={RelativeSource TemplatedParent}}",
            effect.Attribute("Radius")?.Value);
        Assert.Equal(
            "{Binding BlurRenderingBias, RelativeSource={RelativeSource TemplatedParent}}",
            effect.Attribute("RenderingBias")?.Value);

        var noPerSurfaceBlurTrigger = Assert.Single(template.Descendants().Where(element =>
            element.Name.LocalName == "Trigger"
            && element.Attribute("Property")?.Value == "IsSourcePreblurred"
            && element.Attribute("Value")?.Value == "True"));
        Assert.Contains(noPerSurfaceBlurTrigger.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("TargetName")?.Value == "PART_BlurLayer"
            && element.Attribute("Property")?.Value == "Effect"
            && element.Attribute("Value")?.Value == "{x:Null}");
    }

    [Fact]
    public void MainWindowCreatesOneCachedFullWindowBlurSource()
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var effects = LoadAppXaml("Styles", "ControlStyles.Effects.xaml");
        var sourceStyle = Assert.Single(effects.Descendants().Where(element =>
            element.Name.LocalName == "Style"
            && element.Attribute(xaml + "Key")?.Value == "SharedBackdropBlurSourceStyle"));
        Assert.Contains(sourceStyle.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "IsSourcePreblurred"
            && element.Attribute("Value")?.Value == "False");
        var cache = Assert.Single(sourceStyle.Descendants().Where(element =>
            element.Name.LocalName == "BitmapCache"));
        Assert.Equal("0.4", cache.Attribute("RenderAtScale")?.Value);

        var window = LoadAppXaml("Views", "Shell", "MainWindow.xaml");
        var sharedSource = Assert.Single(window.Descendants().Where(element =>
            element.Name.LocalName == "BackdropBlurBorder"
            && element.Attribute(xaml + "Name")?.Value == "LauncherPreblurredBackdropSource"));
        Assert.Equal(
            "{Binding ElementName=LauncherBackgroundVisualSource}",
            sharedSource.Attribute("SourceElement")?.Value);
        Assert.Equal(
            "{StaticResource SharedBackdropBlurSourceStyle}",
            sharedSource.Attribute("Style")?.Value);
    }

    [Fact]
    public void BackdropBlurHasAnImplicitStyleBasedOnTheReusableNamedStyle()
    {
        var document = LoadAppXaml("Styles", "ControlStyles.Effects.xaml");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var implicitStyle = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "Style"
            && element.Attribute(xaml + "Key") is null));

        Assert.Equal("{x:Type controls:BackdropBlurBorder}", implicitStyle.Attribute("TargetType")?.Value);
        Assert.Equal("{StaticResource BackdropBlurBorderStyle}", implicitStyle.Attribute("BasedOn")?.Value);
    }

    [Fact]
    public void ImageBackgroundSurfaceHostsUseTheSharedBackdropStyleAndSwitch()
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var effects = LoadAppXaml("Styles", "ControlStyles.Effects.xaml");
        var surfaceStyle = Assert.Single(effects.Descendants().Where(element =>
            element.Name.LocalName == "Style"
            && element.Attribute(xaml + "Key")?.Value == "SurfaceBackdropBlurStyle"));
        var enabledTrigger = Assert.Single(surfaceStyle.Descendants().Where(element =>
            element.Name.LocalName == "Trigger"
            && element.Attribute("Property")?.Value == "IsTintEnabled"
            && element.Attribute("Value")?.Value == "True"));
        Assert.Contains(surfaceStyle.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "IsTintEnabled"
            && element.Attribute("Value")?.Value ==
                "{DynamicResource Is.ImageBackground.ControlTint.Enabled}");
        Assert.Contains(enabledTrigger.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "TintBrush"
            && element.Attribute("Value")?.Value ==
                "{DynamicResource Brush.SecondaryMenu.BackdropTint}");
        Assert.Contains(enabledTrigger.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "OverlayBrush"
            && element.Attribute("Value")?.Value ==
                "{DynamicResource Brush.SurfaceBackdrop.ContrastOverlay}");

        var page = LoadAppXaml("Styles", "ControlStyles.Page.xaml");
        var sectionStyle = Assert.Single(page.Descendants().Where(element =>
            element.Name.LocalName == "Style"
            && element.Attribute(xaml + "Key")?.Value == "SectionFieldSurfaceStyle"));
        Assert.Contains(sectionStyle.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "behaviors:BackdropBlurHost.IsBlurEnabled"
            && element.Attribute("Value")?.Value ==
                "{DynamicResource Is.Surface.BackdropBlur.Enabled}");

        var dialogs = LoadAppXaml("Styles", "ControlStyles.Dialogs.xaml");
        var ordinaryButtonStyle = Assert.Single(dialogs.Descendants().Where(element =>
            element.Name.LocalName == "Style"
            && element.Attribute(xaml + "Key")?.Value == "DialogButtonStyle"));
        Assert.Contains(ordinaryButtonStyle.Descendants(), element =>
            element.Name.LocalName == "Border"
            && element.Attribute(xaml + "Name")?.Value == "BaseBackground"
            && element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "BackdropBlurHost.IsApplied"
                && attribute.Value == "True"));
        var contrastLayer = Assert.Single(ordinaryButtonStyle.Descendants().Where(element =>
            element.Name.LocalName == "Border"
            && element.Attribute(xaml + "Name")?.Value == "SurfaceContrastLayer"));
        var ordinaryBaseBackground = Assert.Single(ordinaryButtonStyle.Descendants().Where(element =>
            element.Name.LocalName == "Border"
            && element.Attribute(xaml + "Name")?.Value == "BaseBackground"));
        Assert.Equal("{TemplateBinding Background}", ordinaryBaseBackground.Attribute("Background")?.Value);
        Assert.Equal("{DynamicResource Brush.Button.Secondary.Background}", contrastLayer.Attribute("Background")?.Value);
        Assert.Equal("{DynamicResource Brush.Input.TextBox.Border}", contrastLayer.Attribute("BorderBrush")?.Value);
        Assert.Equal("1", contrastLayer.Attribute("BorderThickness")?.Value);
        Assert.Equal("0", contrastLayer.Attribute("Opacity")?.Value);
        var contrastTrigger = Assert.Single(ordinaryButtonStyle.Descendants().Where(element =>
            element.Name.LocalName == "DataTrigger"
            && element.Attribute("Binding")?.Value.Contains(
                "BackdropBlurHost.IsBlurEnabled",
                StringComparison.Ordinal) == true));
        Assert.Contains(contrastTrigger.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("TargetName")?.Value == "SurfaceContrastLayer"
            && element.Attribute("Property")?.Value == "Opacity"
            && element.Attribute("Value")?.Value == "1");
        var disabledTrigger = Assert.Single(ordinaryButtonStyle.Descendants().Where(element =>
            element.Name.LocalName == "Trigger"
            && element.Attribute("Property")?.Value == "IsEnabled"
            && element.Attribute("Value")?.Value == "False"));
        Assert.Contains(disabledTrigger.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("TargetName")?.Value == "BaseBackground"
            && element.Attribute("Property")?.Value == "Background"
            && element.Attribute("Value")?.Value == "{DynamicResource Brush.Button.Secondary.Disabled}");
        Assert.Contains(disabledTrigger.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("TargetName")?.Value == "BaseBackground"
            && element.Attribute("Property")?.Value == "behaviors:BackdropBlurHost.FallbackBrush"
            && element.Attribute("Value")?.Value == "{DynamicResource Brush.Button.Secondary.Disabled}");
        Assert.Contains(disabledTrigger.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("TargetName")?.Value == "SurfaceContrastLayer"
            && element.Attribute("Property")?.Value == "Background"
            && element.Attribute("Value")?.Value == "{DynamicResource Brush.Button.Secondary.Disabled}");

        foreach (var styleKey in new[] { "PrimaryDialogButtonStyle", "DangerDialogButtonStyle" })
        {
            var semanticButtonStyle = Assert.Single(dialogs.Descendants().Where(element =>
                element.Name.LocalName == "Style"
                && element.Attribute(xaml + "Key")?.Value == styleKey));
            var semanticBaseBackground = Assert.Single(semanticButtonStyle.Descendants().Where(element =>
                element.Name.LocalName == "Border"
                && element.Attribute(xaml + "Name")?.Value == "BaseBackground"));
            Assert.Equal("{TemplateBinding Background}", semanticBaseBackground.Attribute("Background")?.Value);
            Assert.Equal(
                "True",
                semanticBaseBackground.Attributes().Single(attribute =>
                    attribute.Name.LocalName == "BackdropBlurHost.IsApplied").Value);

            var semanticColorLayer = Assert.Single(semanticButtonStyle.Descendants().Where(element =>
                element.Name.LocalName == "Border"
                && element.Attribute(xaml + "Name")?.Value == "SemanticBackground"));
            Assert.Equal("{TemplateBinding Background}", semanticColorLayer.Attribute("Background")?.Value);

            var semanticDisabledTrigger = Assert.Single(semanticButtonStyle.Descendants().Where(element =>
                element.Name.LocalName == "Trigger"
                && element.Attribute("Property")?.Value == "IsEnabled"
                && element.Attribute("Value")?.Value == "False"));
            Assert.Contains(semanticDisabledTrigger.Elements(), element =>
                element.Name.LocalName == "Setter"
                && element.Attribute("TargetName")?.Value == "BaseBackground"
                && element.Attribute("Property")?.Value == "Background"
                && element.Attribute("Value")?.Value ==
                    "{DynamicResource Brush.Button.Secondary.Disabled}");
            Assert.Contains(semanticDisabledTrigger.Elements(), element =>
                element.Name.LocalName == "Setter"
                && element.Attribute("TargetName")?.Value == "BaseBackground"
                && element.Attribute("Property")?.Value == "behaviors:BackdropBlurHost.IsBlurEnabled"
                && element.Attribute("Value")?.Value ==
                    "{DynamicResource Is.Surface.BackdropBlur.Enabled}");
            Assert.Contains(semanticDisabledTrigger.Elements(), element =>
                element.Name.LocalName == "Setter"
                && element.Attribute("TargetName")?.Value == "SemanticBackground"
                && element.Attribute("Property")?.Value == "Opacity"
                && element.Attribute("Value")?.Value == "0");
            Assert.Single(semanticButtonStyle.Descendants().Where(element =>
                element.Name.LocalName == "Border"
                && element.Attribute(xaml + "Name")?.Value == "SurfaceContrastLayer"));
            Assert.Single(semanticButtonStyle.Descendants().Where(element =>
                element.Name.LocalName == "DataTrigger"
                && element.Attribute("Binding")?.Value.Contains(
                    "BackdropBlurHost.IsBlurEnabled",
                    StringComparison.Ordinal) == true));
        }

        var inputs = LoadAppXaml("Styles", "ControlStyles.Inputs.xaml");
        Assert.Equal(2, inputs.Descendants().Count(element =>
            element.Name.LocalName == "Border"
            && element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "BackdropBlurHost.IsApplied"
                && attribute.Value == "True")));

        var comboToggleStyle = Assert.Single(inputs.Descendants().Where(element =>
            element.Name.LocalName == "Style"
            && element.Attribute(xaml + "Key")?.Value == "LauncherComboBoxToggleButtonStyle"));
        var comboInteractiveRoot = Assert.Single(comboToggleStyle.Descendants().Where(element =>
            element.Name.LocalName == "Border"
            && element.Attribute(xaml + "Name")?.Value == "Root"));
        Assert.Equal(
            "{Binding Background, RelativeSource={RelativeSource AncestorType=ComboBox}}",
            comboInteractiveRoot.Attribute("Background")?.Value);
        Assert.DoesNotContain(comboToggleStyle.DescendantsAndSelf().Attributes(), attribute =>
            attribute.Name.LocalName == "BackdropBlurHost.IsApplied");

        var comboBoxStyle = Assert.Single(inputs.Descendants().Where(element =>
            element.Name.LocalName == "Style"
            && element.Attribute(xaml + "Key")?.Value == "LauncherComboBoxStyle"));
        var comboToggle = Assert.Single(comboBoxStyle.Descendants().Where(element =>
            element.Name.LocalName == "ToggleButton"
            && element.Attribute(xaml + "Name")?.Value == "ToggleButton"));
        Assert.Contains("IsDropDownOpen", comboToggle.Attribute("IsChecked")?.Value, StringComparison.Ordinal);
        Assert.Contains("Mode=TwoWay", comboToggle.Attribute("IsChecked")?.Value, StringComparison.Ordinal);
        Assert.DoesNotContain(comboBoxStyle.Descendants(), element =>
            element.Name.LocalName == "BackdropBlurBorder");
    }

    [Fact]
    public void DownloadTaskCardsAndLoaderChoicesUseTheImageBackdropSwitch()
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var lists = LoadAppXaml("Styles", "ControlStyles.Lists.xaml");
        var taskCardStyle = Assert.Single(lists.Descendants().Where(element =>
            element.Name.LocalName == "Style"
            && element.Attribute(xaml + "Key")?.Value == "InstallTaskCardStyle"));
        Assert.Contains(taskCardStyle.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "behaviors:BackdropBlurHost.IsApplied"
            && element.Attribute("Value")?.Value == "True");
        Assert.Contains(taskCardStyle.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "behaviors:BackdropBlurHost.IsBlurEnabled"
            && element.Attribute("Value")?.Value ==
                "{DynamicResource Is.Surface.BackdropBlur.Enabled}");

        var dialogs = LoadAppXaml("Styles", "ControlStyles.Dialogs.xaml");
        var loaderChoiceStyle = Assert.Single(dialogs.Descendants().Where(element =>
            element.Name.LocalName == "Style"
            && element.Attribute(xaml + "Key")?.Value == "DialogChoiceItemStyle"));
        var baseBackground = Assert.Single(loaderChoiceStyle.Descendants().Where(element =>
            element.Name.LocalName == "Border"
            && element.Attribute(xaml + "Name")?.Value == "BaseBackground"));
        Assert.Equal(
            "True",
            baseBackground.Attributes().Single(attribute =>
                attribute.Name.LocalName == "BackdropBlurHost.IsApplied").Value);
        Assert.Equal(
            "{DynamicResource Is.Surface.BackdropBlur.Enabled}",
            baseBackground.Attributes().Single(attribute =>
                attribute.Name.LocalName == "BackdropBlurHost.IsBlurEnabled").Value);
        Assert.Equal(
            "{DynamicResource Brush.List.Item.Background}",
            baseBackground.Attributes().Single(attribute =>
                attribute.Name.LocalName == "BackdropBlurHost.FallbackBrush").Value);
    }

    [Fact]
    public void SecondaryMenuBackdropStyleUsesTheImageModeSwitchAndPreservesThePanelTint()
    {
        var document = LoadAppXaml("Styles", "ControlStyles.Effects.xaml");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var style = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "Style"
            && element.Attribute(xaml + "Key")?.Value == "SecondaryMenuBackdropStyle"));

        Assert.Equal("{x:Type controls:BackdropBlurBorder}", style.Attribute("TargetType")?.Value);
        Assert.Equal("{StaticResource BackdropBlurBorderStyle}", style.Attribute("BasedOn")?.Value);
        Assert.Contains(style.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "IsBlurEnabled"
            && element.Attribute("Value")?.Value == "{DynamicResource Is.SecondaryMenu.BackdropBlur.Enabled}");

        var enabledTrigger = Assert.Single(style.Descendants().Where(element =>
            element.Name.LocalName == "Trigger"
            && element.Attribute("Property")?.Value == "IsTintEnabled"
            && element.Attribute("Value")?.Value == "True"));
        Assert.Contains(enabledTrigger.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "BaseBrush"
            && element.Attribute("Value")?.Value == "Transparent");
        Assert.Contains(enabledTrigger.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "TintBrush"
            && element.Attribute("Value")?.Value == "{DynamicResource Brush.SecondaryMenu.BackdropTint}");
    }

    [Fact]
    public void SecondaryMenuFrameUsesTheCentralBackdropSourceStyle()
    {
        var document = LoadAppXaml("Controls", "Navigation", "SecondaryMenuFrame.xaml");
        var backdrop = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "BackdropBlurBorder"));

        Assert.Equal("{StaticResource SecondaryMenuBackdropStyle}", backdrop.Attribute("Style")?.Value);
        Assert.Null(backdrop.Attribute("SourceElement"));
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "Border"
            && element.Attribute("Style")?.Value == "{StaticResource SecondaryMenuPanelStyle}");
    }

    [Fact]
    public void PrimaryMenuBackdropUsesTheImageModeTintAndDoesNotInterceptInput()
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var effects = LoadAppXaml("Styles", "ControlStyles.Effects.xaml");
        var style = Assert.Single(effects.Descendants().Where(element =>
            element.Name.LocalName == "Style"
            && element.Attribute(xaml + "Key")?.Value == "PrimaryMenuBackdropStyle"));
        Assert.Equal("{StaticResource BackdropBlurBorderStyle}", style.Attribute("BasedOn")?.Value);
        Assert.Contains(style.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "IsBlurEnabled"
            && element.Attribute("Value")?.Value ==
                "{DynamicResource Is.SecondaryMenu.BackdropBlur.Enabled}");
        Assert.Contains(style.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "BaseBrush"
            && element.Attribute("Value")?.Value == "Transparent");
        var enabledTrigger = Assert.Single(style.Descendants().Where(element =>
            element.Name.LocalName == "Trigger"
            && element.Attribute("Property")?.Value == "IsTintEnabled"
            && element.Attribute("Value")?.Value == "True"));
        Assert.Contains(enabledTrigger.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "TintBrush"
            && element.Attribute("Value")?.Value ==
                "{DynamicResource Brush.SecondaryMenu.BackdropTint}");

        var navigation = LoadAppXaml("Views", "Shell", "ShellNavigationView.xaml");
        var backdrop = Assert.Single(navigation.Descendants().Where(element =>
            element.Name.LocalName == "BackdropBlurBorder"));
        Assert.Equal("{StaticResource PrimaryMenuBackdropStyle}", backdrop.Attribute("Style")?.Value);
        Assert.Equal("False", backdrop.Attribute("IsHitTestVisible")?.Value);
        Assert.Null(backdrop.Attribute("SourceElement"));
    }

    [Fact]
    public void HomeLaunchMenuUsesTheCentralBackdropSourceStyle()
    {
        var document = LoadAppXaml("Views", "Home", "HomeLaunchGameListView.xaml");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var backdrop = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "BackdropBlurBorder"
            && element.Attribute(xaml + "Name")?.Value == "HomeLaunchMenuPanel"));

        Assert.Equal("{StaticResource SecondaryMenuBackdropStyle}", backdrop.Attribute("Style")?.Value);
        Assert.Null(backdrop.Attribute("SourceElement"));
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "Border"
            && element.Attribute(xaml + "Name")?.Value == "HomeLaunchMenuPanel"
            && element.Attribute("Style")?.Value == "{StaticResource SecondaryMenuPanelStyle}");
    }

    [Fact]
    public void DialogHostDisablesSurfaceBackdropBlurForDialogControls()
    {
        var document = LoadAppXaml("Controls", "Dialogs", "DialogHost.xaml");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var overlay = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "Grid"
            && element.Attribute(xaml + "Name")?.Value == "RootOverlay"));
        var blurSwitch = Assert.Single(overlay.Attributes().Where(attribute =>
            attribute.Name.LocalName == "BackdropBlurHost.IsBlurSuppressed"));

        Assert.Equal("True", blurSwitch.Value);
    }

    private static XDocument LoadAppXaml(params string[] relativeSegments)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");

        return XDocument.Load(Path.Combine([root.FullName, "Launcher.App", .. relativeSegments]));
    }
}
