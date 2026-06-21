using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Launcher.App.Behaviors;

namespace Launcher.Tests.Behaviors;

public sealed class VerticalEdgeOpacityMaskTests
{
    [Fact]
    public void VerticalEdgeOpacityMaskSupportsSegmentedTopFade()
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                var border = new Border
                {
                    Width = 100,
                    Height = 400
                };

                VerticalEdgeOpacityMask.SetTopFadeLength(border, 170d);
                VerticalEdgeOpacityMask.SetTopIntermediateLength(border, 120d);
                VerticalEdgeOpacityMask.SetTopIntermediateOpacity(border, 0.1d);
                VerticalEdgeOpacityMask.SetTopPlateauLength(border, 0d);
                VerticalEdgeOpacityMask.SetBottomFadeLength(border, 0d);
                VerticalEdgeOpacityMask.SetMinimumOpacity(border, 0d);
                VerticalEdgeOpacityMask.SetIsEnabled(border, true);

                border.Measure(new Size(100, 400));
                border.Arrange(new Rect(0, 0, 100, 400));
                border.UpdateLayout();

                var mask = Assert.IsType<LinearGradientBrush>(border.OpacityMask);
                var stops = mask.GradientStops;

                Assert.True(stops.Count >= 4);
                Assert.Equal(0d, stops[0].Color.A / 255d, 2);
                Assert.Equal(0d, stops[0].Offset, 3);
                Assert.Equal(120d / 400d, stops[1].Offset, 3);
                Assert.Equal(0.1d, stops[1].Color.A / 255d, 2);
                Assert.Equal(170d / 400d, stops[2].Offset, 3);
                Assert.Equal(byte.MaxValue, stops[2].Color.A);
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }
}
