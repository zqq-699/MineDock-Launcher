using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Launcher.App.Behaviors;
using Launcher.App.Controls;
using Launcher.App.Effects;
using Launcher.Tests.Helpers;
using WpfApplication = System.Windows.Application;

namespace Launcher.Tests.Controls;

[Collection(WpfTestCollection.Name)]
public sealed class ListPageFrameProgressiveBlurTests
{
    [Fact]
    public void ProgressiveBlurUsesTwoPassEffectsAndPreservesOpacityMask()
    {
        StaTest.Run(() =>
        {
            var application = WpfApplicationTestHelper.GetOrCreateApplication();
            AddLauncherResources(application);
            var frame = new ListPageFrame
            {
                IsProgressiveBlurEnabled = true,
                IsListVisible = true,
                ListContent = new Border
                {
                    Height = 200d,
                    Background = new SolidColorBrush(Color.FromArgb(0x20, 0xff, 0xff, 0xff))
                }
            };
            var window = CreateTestWindow(frame);

            try
            {
                window.Show();
                PumpLayout(frame);

                var expectedLayout = CalculateExpectedLayout(frame, 140d, 164d);
                var horizontalEffect = Assert.IsType<ProgressiveGaussianBlurEffect>(frame.BlurBandHorizontalHostElement.Effect);
                var verticalEffect = Assert.IsType<ProgressiveGaussianBlurEffect>(frame.BlurBandVerticalHostElement.Effect);

                Assert.Null(frame.ListVisualSourceElement.Effect);
                Assert.Null(frame.ListLayerElement.Effect);
                Assert.Null(frame.BlurBandViewportElement.Effect);
                Assert.Null(frame.BlurBandUpscaleHostElement.Effect);
                Assert.Equal((1d, 0d), (horizontalEffect.DirectionX, horizontalEffect.DirectionY));
                Assert.Equal((0d, 1d), (verticalEffect.DirectionX, verticalEffect.DirectionY));
                Assert.Same(frame.ListVisualSourceElement, frame.BlurBandBrush.Visual);
                Assert.Equal(Visibility.Visible, frame.BlurBandViewportElement.Visibility);
                Assert.Equal(expectedLayout.PresentationHeight, frame.BlurBandViewportElement.Height, 10);
                Assert.True(frame.BlurBandViewportElement.SnapsToDevicePixels);
                Assert.True(frame.DirectListHostElement.SnapsToDevicePixels);
                Assert.Equal(expectedLayout.LowResolutionWidth, frame.BlurBandUpscaleHostElement.Width, 10);
                Assert.Equal(expectedLayout.LowResolutionHeight, frame.BlurBandUpscaleHostElement.Height, 10);
                Assert.Equal(expectedLayout.LowResolutionWidth, frame.BlurBandHorizontalHostElement.Width, 10);
                Assert.Equal(expectedLayout.LowResolutionHeight, frame.BlurBandHorizontalHostElement.Height, 10);
                Assert.Equal(expectedLayout.LowResolutionWidth, frame.BlurBandVerticalHostElement.Width, 10);
                Assert.Equal(expectedLayout.LowResolutionHeight, frame.BlurBandVerticalHostElement.Height, 10);
                Assert.Equal(expectedLayout.UpscaleX, frame.BlurBandUpscaleTransform.ScaleX, 10);
                Assert.Equal(expectedLayout.UpscaleY, frame.BlurBandUpscaleTransform.ScaleY, 10);
                Assert.Equal(
                    expectedLayout.TextureHeight,
                    frame.BlurBandVerticalHostElement.Height * frame.BlurBandUpscaleTransform.ScaleY,
                    10);
                Assert.True(expectedLayout.TextureHeight > frame.BlurBandViewportElement.Height);
                Assert.Equal(
                    BitmapScalingMode.Linear,
                    RenderOptions.GetBitmapScalingMode(frame.BlurBandUpscaleHostElement));
                Assert.Equal(
                    new Rect(0d, 0d, frame.ListLayerElement.ActualWidth, expectedLayout.TextureHeight),
                    frame.BlurBandBrush.Viewbox);
                Assert.Null(frame.BlurBandViewportElement.OpacityMask);

                var directListClip = Assert.IsType<RectangleGeometry>(frame.DirectListHostElement.Clip);
                Assert.Equal(expectedLayout.PresentationHeight, directListClip.Rect.Y, 10);
                Assert.Equal(frame.ListLayerElement.ActualWidth, directListClip.Rect.Width);
                Assert.Equal(
                    frame.ListLayerElement.ActualHeight - expectedLayout.PresentationHeight,
                    directListClip.Rect.Height,
                    10);
                Assert.Equal(frame.BlurBandViewportElement.Height, directListClip.Rect.Y, 10);

                AssertEffectParameters(
                    horizontalEffect,
                    expectedLayout,
                    expectedLayout.HorizontalMaximumRadius);
                AssertEffectParameters(
                    verticalEffect,
                    expectedLayout,
                    expectedLayout.VerticalMaximumRadius);

                var activeMask = Assert.IsType<LinearGradientBrush>(frame.ListLayerElement.OpacityMask);
                Assert.Equal(0d, VerticalEdgeOpacityMask.GetTopMinimumOpacity(frame.ListLayerElement));
                Assert.Equal(0.4d, VerticalEdgeOpacityMask.GetTopIntermediateOpacity(frame.ListLayerElement));
                Assert.Equal(0, activeMask.GradientStops.First().Color.A);
                Assert.Equal(102, activeMask.GradientStops[1].Color.A);
                Assert.Equal(0, activeMask.GradientStops.Last().Color.A);
                Assert.Equal(72d, VerticalEdgeOpacityMask.GetBottomFadeLength(frame.ListLayerElement));

                frame.IsProgressiveBlurEnabled = false;
                PumpLayout(frame);

                Assert.Null(frame.ListLayerElement.Effect);
                Assert.Null(frame.ListVisualSourceElement.Effect);
                Assert.Null(frame.BlurBandVerticalHostElement.Effect);
                Assert.Null(frame.BlurBandHorizontalHostElement.Effect);
                Assert.Equal(Visibility.Collapsed, frame.BlurBandViewportElement.Visibility);
                Assert.Null(frame.DirectListHostElement.Clip);
                var fallbackMask = Assert.IsType<LinearGradientBrush>(frame.ListLayerElement.OpacityMask);
                Assert.True(double.IsNaN(VerticalEdgeOpacityMask.GetTopMinimumOpacity(frame.ListLayerElement)));
                Assert.Equal(0.1d, VerticalEdgeOpacityMask.GetTopIntermediateOpacity(frame.ListLayerElement));
                Assert.Equal(0, fallbackMask.GradientStops.First().Color.A);
                Assert.Equal(0, fallbackMask.GradientStops.Last().Color.A);
            }
            finally
            {
                window.Close();
                WpfApplicationTestHelper.ShutdownAndResetCurrentApplication();
            }
        });
    }

    [Fact]
    public void ProgressiveBlurLengthTracksAllHeaderStates()
    {
        StaTest.Run(() =>
        {
            var application = WpfApplicationTestHelper.GetOrCreateApplication();
            AddLauncherResources(application);
            var frame = new ListPageFrame
            {
                IsProgressiveBlurEnabled = true,
                IsListVisible = true,
                ListContent = new Border()
            };
            var window = CreateTestWindow(frame);

            try
            {
                window.Show();
                PumpLayout(frame);
                AssertBlurLength(frame, 140d);

                frame.IsSearchFilterVisible = true;
                PumpLayout(frame);
                AssertBlurLength(frame, 172d);

                frame.IsSearchToolbarVisible = true;
                PumpLayout(frame);
                AssertBlurLength(frame, 196d);

                frame.IsSearchVisible = false;
                PumpLayout(frame);
                AssertBlurLength(frame, 70d);
            }
            finally
            {
                window.Close();
                WpfApplicationTestHelper.ShutdownAndResetCurrentApplication();
            }
        });
    }

    [Fact]
    public void ProgressiveBlurReusesBrushEffectsAndParametersDuringScroll()
    {
        StaTest.Run(() =>
        {
            var application = WpfApplicationTestHelper.GetOrCreateApplication();
            AddLauncherResources(application);
            var content = new StackPanel();
            for (var index = 0; index < 80; index++)
                content.Children.Add(new Border { Height = 36d });

            var frame = new ListPageFrame
            {
                IsProgressiveBlurEnabled = true,
                IsListVisible = true,
                ListContent = content
            };
            var window = CreateTestWindow(frame);

            try
            {
                window.Show();
                PumpLayout(frame);

                var brush = frame.BlurBandBrush;
                var directListClip = Assert.IsType<RectangleGeometry>(frame.DirectListHostElement.Clip);
                var upscaleTransform = frame.BlurBandUpscaleTransform;
                var horizontalEffect = Assert.IsType<ProgressiveGaussianBlurEffect>(frame.BlurBandHorizontalHostElement.Effect);
                var verticalEffect = Assert.IsType<ProgressiveGaussianBlurEffect>(frame.BlurBandVerticalHostElement.Effect);
                var horizontalParameters = ReadParameters(horizontalEffect);
                var verticalParameters = ReadParameters(verticalEffect);
                var upscaleParameters = (upscaleTransform.ScaleX, upscaleTransform.ScaleY);

                frame.ScrollViewer.ScrollToVerticalOffset(240d);
                PumpLayout(frame);

                Assert.True(frame.ScrollViewer.VerticalOffset > 0d);
                Assert.Same(brush, frame.BlurBandBrush);
                Assert.Same(directListClip, frame.DirectListHostElement.Clip);
                Assert.Same(upscaleTransform, frame.BlurBandUpscaleTransform);
                Assert.Null(frame.BlurBandViewportElement.OpacityMask);
                Assert.Same(horizontalEffect, frame.BlurBandHorizontalHostElement.Effect);
                Assert.Same(verticalEffect, frame.BlurBandVerticalHostElement.Effect);
                Assert.Equal(horizontalParameters, ReadParameters(horizontalEffect));
                Assert.Equal(verticalParameters, ReadParameters(verticalEffect));
                Assert.Equal(upscaleParameters, (upscaleTransform.ScaleX, upscaleTransform.ScaleY));

                var previousInputWidth = horizontalEffect.InputWidth;
                window.Width += 120d;
                PumpLayout(frame);

                Assert.Same(brush, frame.BlurBandBrush);
                Assert.Same(directListClip, frame.DirectListHostElement.Clip);
                Assert.Same(upscaleTransform, frame.BlurBandUpscaleTransform);
                Assert.Same(horizontalEffect, frame.BlurBandHorizontalHostElement.Effect);
                Assert.Same(verticalEffect, frame.BlurBandVerticalHostElement.Effect);
                Assert.NotEqual(previousInputWidth, horizontalEffect.InputWidth);
                Assert.Equal(frame.ListLayerElement.ActualWidth, frame.BlurBandBrush.Viewbox.Width);
                Assert.True(horizontalEffect.InputWidth < frame.BlurBandBrush.Viewbox.Width);
            }
            finally
            {
                window.Close();
                WpfApplicationTestHelper.ShutdownAndResetCurrentApplication();
            }
        });
    }

    [Theory]
    [InlineData(70d, 94d, 98d, 39d)]
    [InlineData(140d, 164d, 168d, 67d)]
    [InlineData(172d, 196d, 200d, 80d)]
    [InlineData(196d, 220d, 224d, 90d)]
    public void ProgressiveBlurRenderScaleUsesExpectedSurfaceHeightsAtOneHundredPercent(
        double blurLength,
        double blurBandHeight,
        double expectedTextureHeight,
        double expectedLowResolutionHeight)
    {
        var layout = ListPageFrame.CalculateProgressiveBlurRenderLayout(
            width: 800d,
            height: 600d,
            blurLength,
            blurBandHeight,
            maximumRadius: 24d,
            renderScale: 0.4d,
            new DpiScale(1d, 1d));

        Assert.Equal(320d, layout.LowResolutionWidth);
        Assert.Equal(expectedLowResolutionHeight, layout.LowResolutionHeight);
        Assert.Equal(expectedTextureHeight, layout.TextureHeight);
        Assert.Equal(800d, layout.LowResolutionWidth * layout.UpscaleX, 10);
        Assert.Equal(expectedTextureHeight, layout.LowResolutionHeight * layout.UpscaleY, 10);
        Assert.Equal(blurBandHeight, layout.PresentationHeight);
        Assert.Equal(layout.PresentationHeight, layout.DirectListStart);
        Assert.Equal(
            blurLength * layout.LowResolutionHeight / expectedTextureHeight,
            layout.ScaledBlurLength,
            10);
        Assert.Equal(24d * layout.LowResolutionWidth / 800d, layout.HorizontalMaximumRadius, 10);
        Assert.Equal(24d * layout.LowResolutionHeight / expectedTextureHeight, layout.VerticalMaximumRadius, 10);

        var surfaceAreaRatio =
            (layout.LowResolutionWidth * layout.LowResolutionHeight) / (800d * expectedTextureHeight);
        Assert.InRange(surfaceAreaRatio, 0.15d, 0.17d);
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(1.25d)]
    [InlineData(1.5d)]
    [InlineData(1.75d)]
    [InlineData(2d)]
    public void ProgressiveBlurRenderScaleAlignsToPhysicalPixelsAcrossDpi(double dpiScale)
    {
        const double width = 803d;
        const double blurBandHeight = 94d;
        var layout = ListPageFrame.CalculateProgressiveBlurRenderLayout(
            width,
            height: 600d,
            blurLength: 70d,
            blurBandHeight,
            maximumRadius: 24d,
            renderScale: 0.4d,
            new DpiScale(dpiScale, dpiScale));

        var expectedWidthPixels = Math.Round(width * dpiScale * 0.4d, MidpointRounding.AwayFromZero);
        var expectedSeamPixels = Math.Round(blurBandHeight * dpiScale, MidpointRounding.AwayFromZero);
        var expectedSeam = expectedSeamPixels / dpiScale;
        var textureHeight = expectedSeam + 4d;
        var expectedHeightPixels = Math.Round(textureHeight * dpiScale * 0.4d, MidpointRounding.AwayFromZero);

        Assert.Equal(expectedWidthPixels, layout.LowResolutionWidth * dpiScale, 10);
        Assert.Equal(expectedHeightPixels, layout.LowResolutionHeight * dpiScale, 10);
        Assert.Equal(width, layout.LowResolutionWidth * layout.UpscaleX, 10);
        Assert.Equal(textureHeight, layout.LowResolutionHeight * layout.UpscaleY, 10);
        Assert.Equal(expectedSeamPixels, layout.PresentationHeight * dpiScale, 10);
        Assert.Equal(layout.PresentationHeight, layout.DirectListStart, 10);
    }

    [Fact]
    public void ProgressiveBlurTextureOverscanClampsToAvailableListHeight()
    {
        var layout = ListPageFrame.CalculateProgressiveBlurRenderLayout(
            width: 800d,
            height: 165d,
            blurLength: 140d,
            visibleBlurBandHeight: 164d,
            maximumRadius: 24d,
            renderScale: 0.4d,
            new DpiScale(1d, 1d));

        Assert.Equal(164d, layout.PresentationHeight);
        Assert.Equal(165d, layout.TextureHeight);
        Assert.Equal(164d, layout.DirectListStart);
        Assert.Equal(66d, layout.LowResolutionHeight);
        Assert.Equal(165d, layout.LowResolutionHeight * layout.UpscaleY, 10);
    }

    private static Window CreateTestWindow(ListPageFrame frame)
    {
        return new Window
        {
            Width = 800,
            Height = 600,
            Left = -10000,
            Top = -10000,
            ShowActivated = false,
            ShowInTaskbar = false,
            Content = frame
        };
    }

    private static void AddLauncherResources(WpfApplication application)
    {
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/MineDock_Launcher_x64;component/Resources/ThemeResources.xaml",
                UriKind.Absolute)
        });
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/MineDock_Launcher_x64;component/Styles/ControlStyles.xaml",
                UriKind.Absolute)
        });
    }

    private static void PumpLayout(FrameworkElement element)
    {
        element.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        element.UpdateLayout();
    }

    private static void AssertBlurLength(ListPageFrame frame, double expected)
    {
        var expectedBlurBandHeight = Math.Min(frame.ListLayerElement.ActualHeight, expected + 24d);
        var expectedLayout = CalculateExpectedLayout(frame, expected, expectedBlurBandHeight);
        var horizontalEffect = Assert.IsType<ProgressiveGaussianBlurEffect>(frame.BlurBandHorizontalHostElement.Effect);
        var verticalEffect = Assert.IsType<ProgressiveGaussianBlurEffect>(frame.BlurBandVerticalHostElement.Effect);

        AssertEffectParameters(horizontalEffect, expectedLayout, expectedLayout.HorizontalMaximumRadius);
        AssertEffectParameters(verticalEffect, expectedLayout, expectedLayout.VerticalMaximumRadius);
        Assert.Equal(expectedLayout.LowResolutionHeight, frame.BlurBandHorizontalHostElement.Height, 10);
        Assert.Equal(expectedLayout.LowResolutionHeight, frame.BlurBandVerticalHostElement.Height, 10);
        Assert.Equal(
            expectedLayout.TextureHeight,
            frame.BlurBandVerticalHostElement.Height * frame.BlurBandUpscaleTransform.ScaleY,
            10);
        Assert.Equal(expectedLayout.TextureHeight, frame.BlurBandBrush.Viewbox.Height);
        Assert.Equal(expectedLayout.PresentationHeight, frame.BlurBandViewportElement.Height, 10);
        var directListClip = Assert.IsType<RectangleGeometry>(frame.DirectListHostElement.Clip);
        Assert.Equal(expectedLayout.DirectListStart, directListClip.Rect.Y, 10);
        Assert.Null(frame.BlurBandViewportElement.OpacityMask);
        Assert.Equal(expected, VerticalEdgeOpacityMask.GetTopFadeLength(frame.ListLayerElement));
    }

    private static ListPageFrame.ProgressiveBlurRenderLayout CalculateExpectedLayout(
        ListPageFrame frame,
        double blurLength,
        double blurBandHeight)
    {
        return ListPageFrame.CalculateProgressiveBlurRenderLayout(
            frame.ListLayerElement.ActualWidth,
            frame.ListLayerElement.ActualHeight,
            blurLength,
            blurBandHeight,
            maximumRadius: 24d,
            renderScale: 0.4d,
            VisualTreeHelper.GetDpi(frame.ListLayerElement));
    }

    private static void AssertEffectParameters(
        ProgressiveGaussianBlurEffect effect,
        ListPageFrame.ProgressiveBlurRenderLayout expectedLayout,
        double expectedRadius)
    {
        Assert.Equal(expectedLayout.LowResolutionWidth, effect.InputWidth, 10);
        Assert.Equal(expectedLayout.LowResolutionHeight, effect.InputHeight, 10);
        Assert.Equal(expectedLayout.ScaledBlurLength, effect.BlurLength, 10);
        Assert.Equal(expectedRadius, effect.MaximumRadius, 10);
    }

    private static (double Width, double Height, double BlurLength, double Radius) ReadParameters(
        ProgressiveGaussianBlurEffect effect)
    {
        return (effect.InputWidth, effect.InputHeight, effect.BlurLength, effect.MaximumRadius);
    }
}
