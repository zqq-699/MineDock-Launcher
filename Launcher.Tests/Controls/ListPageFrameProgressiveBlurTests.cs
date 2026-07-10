using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Launcher.App.Behaviors;
using Launcher.App.Controls;
using Launcher.App.Converters;
using Launcher.App.Effects;
using Launcher.App.Views.Account;
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
    public void AccountDetailsUsesTheSameProgressiveBlurBandForItsScrollableContent()
    {
        StaTest.Run(() =>
        {
            var application = WpfApplicationTestHelper.GetOrCreateApplication();
            AddLauncherResources(application);
            AddAccountDetailsResources(application);
            var view = new AccountDetailsView
            {
                IsProgressiveBlurEnabled = true
            };
            var window = CreateTestWindow(view);

            try
            {
                window.Show();
                PumpLayout(view);

                const double blurLength = 70d;
                var visibleBandHeight = Math.Min(
                    view.ProgressiveBlurLayerElement.ActualHeight,
                    blurLength + ProgressiveBlurDefaults.SamplingGuardLength);
                var expectedLayout = ProgressiveBlurLayoutCalculator.Calculate(
                    view.ProgressiveBlurLayerElement.ActualWidth,
                    view.ProgressiveBlurLayerElement.ActualHeight,
                    blurLength,
                    visibleBandHeight,
                    ProgressiveBlurDefaults.MaximumRadius,
                    ProgressiveBlurDefaults.RenderScale,
                    VisualTreeHelper.GetDpi(view.ProgressiveBlurLayerElement));
                var horizontalEffect = Assert.IsType<ProgressiveGaussianBlurEffect>(
                    view.ProgressiveBlurHorizontalHostElement.Effect);
                var verticalEffect = Assert.IsType<ProgressiveGaussianBlurEffect>(
                    view.ProgressiveBlurVerticalHostElement.Effect);

                Assert.Null(view.ProgressiveBlurLayerElement.Effect);
                Assert.Null(view.ProgressiveBlurVisualSourceElement.Effect);
                Assert.Equal((1d, 0d), (horizontalEffect.DirectionX, horizontalEffect.DirectionY));
                Assert.Equal((0d, 1d), (verticalEffect.DirectionX, verticalEffect.DirectionY));
                Assert.Same(view.ProgressiveBlurVisualSourceElement, view.ProgressiveBlurBrush.Visual);
                Assert.Equal(Visibility.Visible, view.ProgressiveBlurViewportElement.Visibility);
                Assert.Equal(expectedLayout.PresentationHeight, view.ProgressiveBlurViewportElement.Height, 10);
                Assert.Equal(expectedLayout.LowResolutionWidth, view.ProgressiveBlurUpscaleHostElement.Width, 10);
                Assert.Equal(expectedLayout.LowResolutionHeight, view.ProgressiveBlurUpscaleHostElement.Height, 10);
                Assert.Equal(expectedLayout.UpscaleX, view.ProgressiveBlurUpscaleTransform.ScaleX, 10);
                Assert.Equal(expectedLayout.UpscaleY, view.ProgressiveBlurUpscaleTransform.ScaleY, 10);
                Assert.Equal(
                    new Rect(
                        0d,
                        0d,
                        view.ProgressiveBlurLayerElement.ActualWidth,
                        expectedLayout.TextureHeight),
                    view.ProgressiveBlurBrush.Viewbox);
                var directListClip = Assert.IsType<RectangleGeometry>(
                    view.ProgressiveBlurDirectHostElement.Clip);
                Assert.Equal(expectedLayout.DirectListStart, directListClip.Rect.Y, 10);
                AssertEffectParameters(
                    horizontalEffect,
                    expectedLayout,
                    expectedLayout.HorizontalMaximumRadius);
                AssertEffectParameters(
                    verticalEffect,
                    expectedLayout,
                    expectedLayout.VerticalMaximumRadius);
                Assert.Equal(70d, VerticalEdgeOpacityMask.GetTopFadeLength(view.ProgressiveBlurLayerElement));
                Assert.Equal(30d,
                    VerticalEdgeOpacityMask.GetTopIntermediateLength(view.ProgressiveBlurLayerElement));
                Assert.Equal(72d, VerticalEdgeOpacityMask.GetBottomFadeLength(view.ProgressiveBlurLayerElement));
                Assert.Equal(0d, VerticalEdgeOpacityMask.GetTopMinimumOpacity(view.ProgressiveBlurLayerElement));
                Assert.Equal(0.4d,
                    VerticalEdgeOpacityMask.GetTopIntermediateOpacity(view.ProgressiveBlurLayerElement));

                var brush = view.ProgressiveBlurBrush;
                view.DetailsScrollViewerElement.ScrollToVerticalOffset(200d);
                PumpLayout(view);
                Assert.True(view.DetailsScrollViewerElement.VerticalOffset > 0d);
                Assert.Same(brush, view.ProgressiveBlurBrush);
                Assert.Same(horizontalEffect, view.ProgressiveBlurHorizontalHostElement.Effect);
                Assert.Same(verticalEffect, view.ProgressiveBlurVerticalHostElement.Effect);

                var offsetBeforeForwardedWheel = view.DetailsScrollViewerElement.VerticalOffset;
                var wheelEvent = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120)
                {
                    RoutedEvent = UIElement.MouseWheelEvent
                };
                view.ProgressiveBlurLayerElement.RaiseEvent(wheelEvent);
                PumpLayout(view);
                Assert.True(wheelEvent.Handled);
                Assert.True(view.DetailsScrollViewerElement.VerticalOffset < offsetBeforeForwardedWheel);

                view.IsProgressiveBlurEnabled = false;
                PumpLayout(view);
                Assert.Null(view.ProgressiveBlurHorizontalHostElement.Effect);
                Assert.Null(view.ProgressiveBlurVerticalHostElement.Effect);
                Assert.Equal(Visibility.Collapsed, view.ProgressiveBlurViewportElement.Visibility);
                Assert.Null(view.ProgressiveBlurDirectHostElement.Clip);
                Assert.True(double.IsNaN(
                    VerticalEdgeOpacityMask.GetTopMinimumOpacity(view.ProgressiveBlurLayerElement)));
                Assert.Equal(0.1d,
                    VerticalEdgeOpacityMask.GetTopIntermediateOpacity(view.ProgressiveBlurLayerElement));
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

    [Fact]
    public void ProgressiveBlurReusesEffectsAcrossVisibilityDisableAndReload()
    {
        StaTest.Run(() =>
        {
            var application = WpfApplicationTestHelper.GetOrCreateApplication();
            AddLauncherResources(application);
            var frame = new ListPageFrame
            {
                IsProgressiveBlurEnabled = true,
                IsListVisible = true,
                ListContent = new Border { Height = 200d }
            };
            var window = CreateTestWindow(frame);

            try
            {
                window.Show();
                PumpLayout(frame);

                var horizontalEffect = Assert.IsType<ProgressiveGaussianBlurEffect>(
                    frame.BlurBandHorizontalHostElement.Effect);
                var verticalEffect = Assert.IsType<ProgressiveGaussianBlurEffect>(
                    frame.BlurBandVerticalHostElement.Effect);
                var blurBandBrush = frame.BlurBandBrush;
                var upscaleTransform = frame.BlurBandUpscaleTransform;

                frame.IsListVisible = false;
                PumpLayout(frame);
                Assert.Null(frame.BlurBandHorizontalHostElement.Effect);
                Assert.Null(frame.BlurBandVerticalHostElement.Effect);

                frame.IsListVisible = true;
                PumpLayout(frame);
                Assert.Same(horizontalEffect, frame.BlurBandHorizontalHostElement.Effect);
                Assert.Same(verticalEffect, frame.BlurBandVerticalHostElement.Effect);

                frame.IsProgressiveBlurEnabled = false;
                PumpLayout(frame);
                Assert.Null(frame.BlurBandHorizontalHostElement.Effect);
                Assert.Null(frame.BlurBandVerticalHostElement.Effect);

                frame.IsProgressiveBlurEnabled = true;
                PumpLayout(frame);
                Assert.Same(horizontalEffect, frame.BlurBandHorizontalHostElement.Effect);
                Assert.Same(verticalEffect, frame.BlurBandVerticalHostElement.Effect);

                window.Content = null;
                PumpLayout(frame);
                Assert.False(frame.IsLoaded);
                Assert.Null(frame.BlurBandHorizontalHostElement.Effect);
                Assert.Null(frame.BlurBandVerticalHostElement.Effect);

                window.Content = frame;
                PumpLayout(frame);
                Assert.True(frame.IsLoaded);
                Assert.Same(horizontalEffect, frame.BlurBandHorizontalHostElement.Effect);
                Assert.Same(verticalEffect, frame.BlurBandVerticalHostElement.Effect);
                Assert.Same(blurBandBrush, frame.BlurBandBrush);
                Assert.Same(upscaleTransform, frame.BlurBandUpscaleTransform);
            }
            finally
            {
                window.Close();
                WpfApplicationTestHelper.ShutdownAndResetCurrentApplication();
            }
        });
    }

    [Fact]
    public void ProgressiveBlurDeactivationPreservesEffectsItDoesNotOwn()
    {
        StaTest.Run(() =>
        {
            var application = WpfApplicationTestHelper.GetOrCreateApplication();
            AddLauncherResources(application);
            var frame = new ListPageFrame
            {
                IsProgressiveBlurEnabled = true,
                IsListVisible = true,
                ListContent = new Border { Height = 200d }
            };
            var window = CreateTestWindow(frame);

            try
            {
                window.Show();
                PumpLayout(frame);

                var listLayerEffect = new BlurEffect { Radius = 0d };
                var listVisualSourceEffect = new BlurEffect { Radius = 0d };
                frame.ListLayerElement.Effect = listLayerEffect;
                frame.ListVisualSourceElement.Effect = listVisualSourceEffect;

                frame.IsProgressiveBlurEnabled = false;
                PumpLayout(frame);

                Assert.Same(listLayerEffect, frame.ListLayerElement.Effect);
                Assert.Same(listVisualSourceEffect, frame.ListVisualSourceElement.Effect);
                Assert.Null(frame.BlurBandHorizontalHostElement.Effect);
                Assert.Null(frame.BlurBandVerticalHostElement.Effect);
            }
            finally
            {
                window.Close();
                WpfApplicationTestHelper.ShutdownAndResetCurrentApplication();
            }
        });
    }

    [Fact]
    public void ProgressiveBlurCreationFailureRetriesOnlyAfterExplicitReenable()
    {
        StaTest.Run(() =>
        {
            var application = WpfApplicationTestHelper.GetOrCreateApplication();
            AddLauncherResources(application);
            var creationAttempts = 0;
            ProgressiveBlurEffectFactory effectFactory = () =>
            {
                creationAttempts++;
                return ProgressiveBlurEffectCreationResult.Failed(
                    new InvalidOperationException("Expected test activation failure."));
            };
            var frame = new ListPageFrame(effectFactory, null)
            {
                IsProgressiveBlurEnabled = true,
                IsListVisible = true,
                ListContent = new Border { Height = 200d }
            };
            var window = CreateTestWindow(frame);

            try
            {
                window.Show();
                PumpLayout(frame);

                Assert.Equal(1, creationAttempts);
                Assert.Null(frame.BlurBandHorizontalHostElement.Effect);
                Assert.Null(frame.BlurBandVerticalHostElement.Effect);
                Assert.Equal(Visibility.Collapsed, frame.BlurBandViewportElement.Visibility);
                Assert.Null(frame.DirectListHostElement.Clip);
                Assert.True(double.IsNaN(VerticalEdgeOpacityMask.GetTopMinimumOpacity(frame.ListLayerElement)));
                Assert.Equal(0.1d, VerticalEdgeOpacityMask.GetTopIntermediateOpacity(frame.ListLayerElement));

                window.Width += 120d;
                PumpLayout(frame);
                frame.IsListVisible = false;
                frame.IsListVisible = true;
                PumpLayout(frame);
                Assert.Equal(1, creationAttempts);

                frame.IsProgressiveBlurEnabled = false;
                frame.IsProgressiveBlurEnabled = true;
                PumpLayout(frame);
                Assert.Equal(2, creationAttempts);
                Assert.Null(frame.BlurBandHorizontalHostElement.Effect);
                Assert.Null(frame.BlurBandVerticalHostElement.Effect);
            }
            finally
            {
                window.Close();
                WpfApplicationTestHelper.ShutdownAndResetCurrentApplication();
            }
        });
    }

    [Fact]
    public void ProgressiveBlurAttachmentFailureClearsBothEffectsAtomically()
    {
        StaTest.Run(() =>
        {
            var application = WpfApplicationTestHelper.GetOrCreateApplication();
            AddLauncherResources(application);
            ListPageFrame? frame = null;
            var attachmentAttempts = 0;
            ProgressiveBlurEffectAttacher effectAttacher = (horizontalEffect, _) =>
            {
                attachmentAttempts++;
                frame!.BlurBandHorizontalHostElement.Effect = horizontalEffect;
                throw new InvalidOperationException("Expected test attachment failure.");
            };
            frame = new ListPageFrame(null, effectAttacher)
            {
                IsProgressiveBlurEnabled = true,
                IsListVisible = true,
                ListContent = new Border { Height = 200d }
            };
            var window = CreateTestWindow(frame);

            try
            {
                window.Show();
                PumpLayout(frame);

                Assert.Equal(1, attachmentAttempts);
                Assert.Null(frame.BlurBandHorizontalHostElement.Effect);
                Assert.Null(frame.BlurBandVerticalHostElement.Effect);
                Assert.Equal(Visibility.Collapsed, frame.BlurBandViewportElement.Visibility);
                Assert.Null(frame.DirectListHostElement.Clip);

                window.Width += 120d;
                PumpLayout(frame);
                Assert.Equal(1, attachmentAttempts);
            }
            finally
            {
                window.Close();
                WpfApplicationTestHelper.ShutdownAndResetCurrentApplication();
            }
        });
    }

    [Fact]
    public void ProgressiveBlurDefaultsPreserveEstablishedVisualParameters()
    {
        Assert.Equal(24d, ProgressiveBlurDefaults.MaximumRadius);
        Assert.Equal(0.4d, ProgressiveBlurDefaults.RenderScale);
        Assert.Equal(0d, ProgressiveBlurDefaults.ActiveMinimumOpacity);
        Assert.Equal(0.4d, ProgressiveBlurDefaults.ActiveIntermediateOpacity);
        Assert.Equal(24d, ProgressiveBlurDefaults.SamplingGuardLength);
        Assert.Equal(4d, ProgressiveBlurDefaults.TextureOverscanLength);
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
        var layout = ProgressiveBlurLayoutCalculator.Calculate(
            width: 800d,
            height: 600d,
            blurLength,
            blurBandHeight,
            maximumRadius: ProgressiveBlurDefaults.MaximumRadius,
            renderScale: ProgressiveBlurDefaults.RenderScale,
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
        Assert.Equal(
            ProgressiveBlurDefaults.MaximumRadius * layout.LowResolutionWidth / 800d,
            layout.HorizontalMaximumRadius,
            10);
        Assert.Equal(
            ProgressiveBlurDefaults.MaximumRadius * layout.LowResolutionHeight / expectedTextureHeight,
            layout.VerticalMaximumRadius,
            10);

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
        var layout = ProgressiveBlurLayoutCalculator.Calculate(
            width,
            height: 600d,
            blurLength: 70d,
            blurBandHeight,
            maximumRadius: ProgressiveBlurDefaults.MaximumRadius,
            renderScale: ProgressiveBlurDefaults.RenderScale,
            new DpiScale(dpiScale, dpiScale));

        var expectedWidthPixels = Math.Round(
            width * dpiScale * ProgressiveBlurDefaults.RenderScale,
            MidpointRounding.AwayFromZero);
        var expectedSeamPixels = Math.Round(blurBandHeight * dpiScale, MidpointRounding.AwayFromZero);
        var expectedSeam = expectedSeamPixels / dpiScale;
        var textureHeight = expectedSeam + ProgressiveBlurDefaults.TextureOverscanLength;
        var expectedHeightPixels = Math.Round(
            textureHeight * dpiScale * ProgressiveBlurDefaults.RenderScale,
            MidpointRounding.AwayFromZero);

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
        var layout = ProgressiveBlurLayoutCalculator.Calculate(
            width: 800d,
            height: 165d,
            blurLength: 140d,
            visibleBlurBandHeight: 164d,
            maximumRadius: ProgressiveBlurDefaults.MaximumRadius,
            renderScale: ProgressiveBlurDefaults.RenderScale,
            new DpiScale(1d, 1d));

        Assert.Equal(164d, layout.PresentationHeight);
        Assert.Equal(165d, layout.TextureHeight);
        Assert.Equal(164d, layout.DirectListStart);
        Assert.Equal(66d, layout.LowResolutionHeight);
        Assert.Equal(165d, layout.LowResolutionHeight * layout.UpscaleY, 10);
    }

    private static Window CreateTestWindow(FrameworkElement content)
    {
        return new Window
        {
            Width = 800,
            Height = 600,
            Left = -10000,
            Top = -10000,
            ShowActivated = false,
            ShowInTaskbar = false,
            Content = content
        };
    }

    private static void AddLauncherResources(WpfApplication application)
    {
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/BlockHelm_Launcher_x64;component/Resources/ThemeResources.xaml",
                UriKind.Absolute)
        });
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/BlockHelm_Launcher_x64;component/Styles/ControlStyles.xaml",
                UriKind.Absolute)
        });
    }

    private static void AddAccountDetailsResources(WpfApplication application)
    {
        application.Resources["AccountKindTextConverter"] = new AccountKindTextConverter();
        application.Resources["BooleanToMenuTextVisibilityConverter"] =
            new BooleanToMenuTextVisibilityConverter();
        application.Resources["CapeDisplayNameConverter"] = new CapeDisplayNameConverter();
        application.Resources["CapeStateTextConverter"] = new CapeStateTextConverter();
    }

    private static void PumpLayout(FrameworkElement element)
    {
        element.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        element.UpdateLayout();
    }

    private static void AssertBlurLength(ListPageFrame frame, double expected)
    {
        var expectedBlurBandHeight = Math.Min(
            frame.ListLayerElement.ActualHeight,
            expected + ProgressiveBlurDefaults.SamplingGuardLength);
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
        Assert.Equal(expected - 40d, VerticalEdgeOpacityMask.GetTopIntermediateLength(frame.ListLayerElement));
    }

    private static ProgressiveBlurRenderLayout CalculateExpectedLayout(
        ListPageFrame frame,
        double blurLength,
        double blurBandHeight)
    {
        return ProgressiveBlurLayoutCalculator.Calculate(
            frame.ListLayerElement.ActualWidth,
            frame.ListLayerElement.ActualHeight,
            blurLength,
            blurBandHeight,
            maximumRadius: ProgressiveBlurDefaults.MaximumRadius,
            renderScale: ProgressiveBlurDefaults.RenderScale,
            VisualTreeHelper.GetDpi(frame.ListLayerElement));
    }

    private static void AssertEffectParameters(
        ProgressiveGaussianBlurEffect effect,
        ProgressiveBlurRenderLayout expectedLayout,
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
