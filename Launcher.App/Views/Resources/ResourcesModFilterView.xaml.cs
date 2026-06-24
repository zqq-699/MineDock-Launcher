using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Launcher.App.Views.Resources;

public partial class ResourcesModFilterView : UserControl
{
    private const double MaxComboWidth = 130;
    private const double CompactThreshold = 80;
    private const double MinimumGroupGap = 12;
    private double cachedLabelWidthTotal;
    private bool isLayoutUpdateScheduled;

    public ResourcesModFilterView()
    {
        InitializeComponent();
        Loaded += ResourcesModFilterView_OnLoaded;
        SizeChanged += ResourcesModFilterView_OnSizeChanged;
    }

    private void ResourcesModFilterView_OnLoaded(object sender, RoutedEventArgs e)
    {
        ScheduleLayoutUpdate();
    }

    private void ResourcesModFilterView_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ScheduleLayoutUpdate();
    }

    private void ScheduleLayoutUpdate()
    {
        if (isLayoutUpdateScheduled)
        {
            return;
        }

        isLayoutUpdateScheduled = true;
        Dispatcher.BeginInvoke(UpdateResponsiveLayout, DispatcherPriority.Loaded);
    }

    private void UpdateResponsiveLayout()
    {
        isLayoutUpdateScheduled = false;

        if (!IsLoaded || double.IsNaN(ActualWidth) || ActualWidth <= 0)
        {
            return;
        }

        var labelWidthTotal = MeasureLabelWidthTotal();
        if (labelWidthTotal > 0)
        {
            cachedLabelWidthTotal = labelWidthTotal;
        }
        else
        {
            labelWidthTotal = cachedLabelWidthTotal;
        }

        var comboWidth = (ActualWidth - labelWidthTotal - (MinimumGroupGap * 3)) / 4;
        if (comboWidth < CompactThreshold)
        {
            ShowCompactLayout();
            return;
        }

        ShowNormalLayout(Math.Min(MaxComboWidth, comboWidth));
    }

    private double MeasureLabelWidthTotal()
    {
        return MeasureLabelWidth(VersionFilterLabel)
            + MeasureLabelWidth(LoaderFilterLabel)
            + MeasureLabelWidth(SourceFilterLabel)
            + MeasureLabelWidth(TypeFilterLabel);
    }

    private static double MeasureLabelWidth(TextBlock label)
    {
        if (label.ActualWidth > 0)
        {
            return label.ActualWidth + label.Margin.Left + label.Margin.Right;
        }

        label.Measure(new Size(double.PositiveInfinity, 32));
        return label.DesiredSize.Width;
    }

    private void ShowCompactLayout()
    {
        SetComboWidth(CompactThreshold);
        NormalFilterGrid.Visibility = Visibility.Collapsed;
        CompactFilterButton.Visibility = Visibility.Visible;
    }

    private void ShowNormalLayout(double comboWidth)
    {
        CompactFilterButton.Visibility = Visibility.Collapsed;
        NormalFilterGrid.Visibility = Visibility.Visible;
        SetComboWidth(comboWidth);
    }

    private void SetComboWidth(double width)
    {
        VersionFilterComboBox.Width = width;
        LoaderFilterComboBox.Width = width;
        SourceFilterComboBox.Width = width;
        TypeFilterComboBox.Width = width;
    }
}
