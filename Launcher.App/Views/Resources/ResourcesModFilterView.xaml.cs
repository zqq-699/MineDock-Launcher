using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Launcher.App.ViewModels.Resources;

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
        DataContextChanged += ResourcesModFilterView_OnDataContextChanged;
    }

    private void ResourcesModFilterView_OnLoaded(object sender, RoutedEventArgs e)
    {
        ScheduleLayoutUpdate();
    }

    private void ResourcesModFilterView_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ScheduleLayoutUpdate();
    }

    private void ResourcesModFilterView_OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
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

        var showsLoaderFilters = ShowsLoaderFilters();
        ApplyFilterColumnLayout(showsLoaderFilters);

        var visibleGroupCount = showsLoaderFilters ? 4 : 3;
        var labelWidthTotal = MeasureLabelWidthTotal(showsLoaderFilters);
        if (labelWidthTotal > 0)
        {
            cachedLabelWidthTotal = labelWidthTotal;
        }
        else
        {
            labelWidthTotal = cachedLabelWidthTotal;
        }

        var comboWidth = (ActualWidth - labelWidthTotal - (MinimumGroupGap * (visibleGroupCount - 1))) / visibleGroupCount;
        if (comboWidth < CompactThreshold)
        {
            ShowCompactLayout();
            return;
        }

        ShowNormalLayout(Math.Min(MaxComboWidth, comboWidth));
    }

    private bool ShowsLoaderFilters()
    {
        return DataContext is not ResourcesModPageViewModel viewModel || viewModel.ShowsLoaderFilters;
    }

    private void ApplyFilterColumnLayout(bool showsLoaderFilters)
    {
        if (NormalFilterGrid.ColumnDefinitions.Count < 7)
            return;

        Grid.SetColumn(SourceFilterGroup, showsLoaderFilters ? 4 : 2);
        Grid.SetColumn(TypeFilterGroup, showsLoaderFilters ? 6 : 4);

        SetColumnWidth(1, new GridLength(1, GridUnitType.Star), MinimumGroupGap);
        SetColumnWidth(2, GridLength.Auto, 0);
        SetColumnWidth(3, new GridLength(1, GridUnitType.Star), MinimumGroupGap);
        SetColumnWidth(4, GridLength.Auto, 0);
        SetColumnWidth(5, showsLoaderFilters ? new GridLength(1, GridUnitType.Star) : new GridLength(0), showsLoaderFilters ? MinimumGroupGap : 0);
        SetColumnWidth(6, showsLoaderFilters ? GridLength.Auto : new GridLength(0), 0);
    }

    private void SetColumnWidth(int index, GridLength width, double minWidth)
    {
        var column = NormalFilterGrid.ColumnDefinitions[index];
        column.Width = width;
        column.MinWidth = minWidth;
    }

    private double MeasureLabelWidthTotal(bool showsLoaderFilters)
    {
        return MeasureLabelWidth(VersionFilterLabel)
            + (showsLoaderFilters ? MeasureLabelWidth(LoaderFilterLabel) : 0)
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
