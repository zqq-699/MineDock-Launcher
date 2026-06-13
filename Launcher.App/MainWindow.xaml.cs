using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Launcher.App.Animations;
using Launcher.App.Controls;
using Launcher.App.Models;
using Launcher.App.Services;
using Launcher.App.ViewModels;
using Microsoft.Win32;

namespace Launcher.App;

public partial class MainWindow : Window
{
    public static readonly DependencyProperty IsMenuExpandedProperty =
        DependencyProperty.Register(nameof(IsMenuExpanded), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    public static readonly DependencyProperty IsUuidCopiedProperty =
        DependencyProperty.Register(nameof(IsUuidCopied), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    private const double CollapsedMenuWidth = 62;
    private const double ExpandedMenuWidth = 210;
    private const double DialogBlurRadius = 42;
    private const double DialogAnimationBlurScale = 0.5;
    private const double DialogSizeAnimationThreshold = 1;
    private static readonly TimeSpan DialogAnimationBlurInterval = TimeSpan.FromMilliseconds(33);
    private static readonly Duration DialogFadeInDuration = TimeSpan.FromMilliseconds(160);
    private static readonly Duration DialogFadeOutDuration = TimeSpan.FromMilliseconds(180);
    private static readonly Duration DialogSizeTransitionDuration = TimeSpan.FromMilliseconds(240);
    private static readonly Color DialogBlurTintColor = Color.FromArgb(0x8A, 0x25, 0x25, 0x25);

    private bool isDialogSizeAnimating;
    private EventHandler? dialogBlurRenderingHandler;
    private DateTime lastDialogAnimationBlurRefreshUtc;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        AcrylicWindow.Enable(this);
        SizeChanged += (_, _) => QueueOpenDialogBlurRefresh();
        AddAccountDialogSurface.SizeChanged += (_, _) => QueueDialogBlurRefreshWhenIdle();
        DeleteAccountDialogSurface.SizeChanged += (_, _) => QueueDialogBlurRefreshWhenIdle();
        RenameAccountDialogSurface.SizeChanged += (_, _) => QueueDialogBlurRefreshWhenIdle();
        Loaded += async (_, _) =>
        {
            await viewModel.InitializeCommand.ExecuteAsync(null);
            IsMenuExpanded = viewModel.IsMenuExpanded;
            MenuColumn.Width = new GridLength(IsMenuExpanded ? ExpandedMenuWidth : CollapsedMenuWidth);
            _ = Dispatcher.BeginInvoke(PrewarmTransientUi, DispatcherPriority.ContextIdle);
        };
    }

    public bool IsMenuExpanded
    {
        get => (bool)GetValue(IsMenuExpandedProperty);
        set => SetValue(IsMenuExpandedProperty, value);
    }

    public bool IsUuidCopied
    {
        get => (bool)GetValue(IsUuidCopiedProperty);
        set => SetValue(IsUuidCopiedProperty, value);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMenu_Click(object sender, RoutedEventArgs e)
    {
        var nextState = !IsMenuExpanded;
        IsMenuExpanded = nextState;

        if (DataContext is MainViewModel viewModel)
            viewModel.IsMenuExpanded = nextState;

        var animation = new GridLengthAnimation
        {
            From = new GridLength(MenuColumn.ActualWidth),
            To = new GridLength(nextState ? ExpandedMenuWidth : CollapsedMenuWidth),
            Duration = TimeSpan.FromMilliseconds(360),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        animation.Completed += (_, _) =>
        {
            MenuColumn.Width = new GridLength(nextState ? ExpandedMenuWidth : CollapsedMenuWidth);
        };

        MenuColumn.BeginAnimation(ColumnDefinition.WidthProperty, animation);
    }

    private void Navigation_Click(object sender, RoutedEventArgs e)
    {
        ResetUuidCopyButton();
        if (sender is FrameworkElement { DataContext: NavigationItem item }
            && DataContext is MainViewModel viewModel)
            viewModel.SelectNavigationItem(item);
    }

    private void Account_Click(object sender, RoutedEventArgs e)
    {
        ResetUuidCopyButton();
        if (sender is FrameworkElement { DataContext: LauncherAccount account }
            && DataContext is MainViewModel viewModel)
            viewModel.SelectAccount(account);
    }

    private void CopyUuid_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { SelectedAccount: { } account })
            return;

        var uuid = account.Uuid;
        if (string.IsNullOrWhiteSpace(uuid))
        {
            SetUuidCopyButtonCopied(true);
            return;
        }

        SetUuidCopyButtonCopied(true);
        try
        {
            Clipboard.SetText(uuid);
            IsUuidCopied = true;
        }
        catch (Exception ex)
        {
            if (DataContext is MainViewModel viewModel)
                viewModel.StatusMessage = $"\u590d\u5236 UUID \u5931\u8d25\uff1a{ex.Message}";
        }
    }

    private void EditAccountName_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.OpenRenameAccountDialog();
            if (viewModel.IsRenameAccountDialogOpen)
                ShowDialogOverlay(RenameAccountDialogOverlay, RenameAccountDialogSurface, RenameAccountDialogBlurLayer);
        }
    }

    private async void ChangeSkin_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        if (!viewModel.CanChangeSelectedAccountSkin)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "\u9009\u62e9 Minecraft \u76ae\u80a4",
            Filter = "PNG \u76ae\u80a4 (*.png)|*.png",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
            await viewModel.ChangeSelectedAccountSkinAsync(dialog.FileName);
    }

    private async void RefreshCapes_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            await viewModel.RefreshSelectedAccountProfileAsync();
    }

    private async void ApplyCape_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            await viewModel.ApplySelectedAccountCapeAsync();
    }

    private void AddAccount_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.OpenAddAccountDialog();
            ShowDialogOverlay(AddAccountDialogOverlay, AddAccountDialogSurface, AddAccountDialogBlurLayer);
        }
    }

    private void CancelAddAccount_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.CancelAddAccountDialog();
            if (!viewModel.IsAddAccountDialogOpen)
                HideDialogOverlay(AddAccountDialogOverlay, viewModel.ResetAddAccountDialog);
        }
    }

    private void BackAddAccount_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            var previousHeight = AddAccountDialogSurface.ActualHeight;
            viewModel.BackToAddAccountTypeStep();
            AnimateDialogSizeChange(AddAccountDialogSurface, AddAccountDialogBlurLayer, previousHeight);
        }
    }

    private async void ConfirmAddAccount_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            var previousHeight = AddAccountDialogSurface.ActualHeight;

            if (viewModel.IsAccountTypeStep && viewModel.SelectedAccountTypeOption?.Kind is "Microsoft")
            {
                viewModel.BeginMicrosoftAccountLogin();
                AnimateDialogSizeChange(AddAccountDialogSurface, AddAccountDialogBlurLayer, previousHeight);

                var loginHeight = AddAccountDialogSurface.ActualHeight;
                await viewModel.CompleteMicrosoftAccountLoginAsync();
                AnimateDialogSizeChange(AddAccountDialogSurface, AddAccountDialogBlurLayer, loginHeight);
                return;
            }

            await viewModel.ConfirmAddAccountDialogAsync();
            if (viewModel.IsAddAccountDialogOpen)
                AnimateDialogSizeChange(AddAccountDialogSurface, AddAccountDialogBlurLayer, previousHeight);
            else
                HideDialogOverlay(AddAccountDialogOverlay, viewModel.ResetAddAccountDialog);
        }
    }

    private void DeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { DataContext: LauncherAccount account }
            && DataContext is MainViewModel viewModel)
        {
            viewModel.OpenDeleteAccountDialog(account);
            ShowDialogOverlay(DeleteAccountDialogOverlay, DeleteAccountDialogSurface, DeleteAccountDialogBlurLayer);
        }
    }

    private void CancelDeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.CancelDeleteAccountDialog();
            HideDialogOverlay(DeleteAccountDialogOverlay);
        }
    }

    private async void ConfirmDeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            var deleteTask = viewModel.ConfirmDeleteAccountDialogAsync();
            if (!viewModel.IsDeleteAccountDialogOpen)
                HideDialogOverlay(DeleteAccountDialogOverlay);

            await deleteTask;
        }
    }

    private void CancelRenameAccount_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.CancelRenameAccountDialog();
            if (!viewModel.IsRenameAccountDialogOpen)
                HideDialogOverlay(RenameAccountDialogOverlay, viewModel.ResetRenameAccountDialog);
        }
    }

    private async void ConfirmRenameAccount_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            var previousHeight = RenameAccountDialogSurface.ActualHeight;
            await viewModel.ConfirmRenameAccountDialogAsync();

            if (viewModel.IsRenameAccountDialogOpen)
                AnimateDialogSizeChange(RenameAccountDialogSurface, RenameAccountDialogBlurLayer, previousHeight);
            else
                HideDialogOverlay(RenameAccountDialogOverlay, viewModel.ResetRenameAccountDialog);
        }
    }

    private void QueueOpenDialogBlurRefresh()
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        if (viewModel.IsAddAccountDialogOpen)
            QueueDialogBlurRefresh(AddAccountDialogSurface, AddAccountDialogBlurLayer, 5);

        if (viewModel.IsDeleteAccountDialogOpen)
            QueueDialogBlurRefresh(DeleteAccountDialogSurface, DeleteAccountDialogBlurLayer, 5);

        if (viewModel.IsRenameAccountDialogOpen)
            QueueDialogBlurRefresh(RenameAccountDialogSurface, RenameAccountDialogBlurLayer, 5);
    }

    private void QueueDialogBlurRefreshWhenIdle()
    {
        if (!isDialogSizeAnimating)
            QueueOpenDialogBlurRefresh();
    }

    private void QueueDialogBlurRefresh(FrameworkElement dialog, Border target, int attempts = 5)
    {
        Dispatcher.BeginInvoke(
            () =>
            {
                UpdateLayout();
                if (!UpdateDialogBlur(dialog, target) && attempts > 0)
                    QueueDialogBlurRefresh(dialog, target, attempts - 1);
            },
            DispatcherPriority.ApplicationIdle);
    }

    private void RefreshDialogBlurNow(FrameworkElement dialog, Border target)
    {
        UpdateLayout();
        UpdateDialogBlur(dialog, target);
    }

    private void AnimateDialogSizeChange(Border dialog, Border blurTarget, double previousHeight)
    {
        StopRealtimeDialogBlur();
        dialog.BeginAnimation(HeightProperty, null);
        dialog.Height = double.NaN;
        isDialogSizeAnimating = true;

        UpdateLayout();
        var targetHeight = dialog.ActualHeight;

        if (previousHeight <= 0
            || targetHeight <= 0
            || Math.Abs(previousHeight - targetHeight) <= DialogSizeAnimationThreshold)
        {
            dialog.Height = double.NaN;
            isDialogSizeAnimating = false;
            RefreshDialogBlurNow(dialog, blurTarget);
            return;
        }

        dialog.Height = previousHeight;
        UpdateLayout();
        UpdateDialogBlur(dialog, blurTarget, DialogAnimationBlurScale);
        StartRealtimeDialogBlur(dialog, blurTarget);

        var animation = new DoubleAnimation
        {
            From = previousHeight,
            To = targetHeight,
            Duration = DialogSizeTransitionDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop
        };

        animation.Completed += (_, _) =>
        {
            StopRealtimeDialogBlur();
            dialog.Height = double.NaN;
            isDialogSizeAnimating = false;
            RefreshDialogBlurNow(dialog, blurTarget);
        };

        dialog.BeginAnimation(HeightProperty, animation);
    }

    private void StartRealtimeDialogBlur(FrameworkElement dialog, Border blurTarget)
    {
        lastDialogAnimationBlurRefreshUtc = DateTime.MinValue;
        dialogBlurRenderingHandler = (_, _) =>
        {
            if (!isDialogSizeAnimating || !dialog.IsVisible)
                return;

            var now = DateTime.UtcNow;
            if (now - lastDialogAnimationBlurRefreshUtc < DialogAnimationBlurInterval)
                return;

            lastDialogAnimationBlurRefreshUtc = now;
            UpdateDialogBlur(dialog, blurTarget, DialogAnimationBlurScale);
        };

        CompositionTarget.Rendering += dialogBlurRenderingHandler;
    }

    private void StopRealtimeDialogBlur()
    {
        if (dialogBlurRenderingHandler is null)
            return;

        CompositionTarget.Rendering -= dialogBlurRenderingHandler;
        dialogBlurRenderingHandler = null;
    }

    private void ShowDialogOverlay(Grid overlay, FrameworkElement dialog, Border blurTarget)
    {
        overlay.BeginAnimation(OpacityProperty, null);
        overlay.Visibility = Visibility.Visible;
        overlay.Opacity = 0;

        UpdateLayout();
        UpdateDialogBlur(dialog, blurTarget);

        var animation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = DialogFadeInDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        overlay.BeginAnimation(OpacityProperty, animation);
    }

    private void PrewarmTransientUi()
    {
        BlurEffectWarmup.EnsureWarmed();
        PrewarmDialogOverlay(AddAccountDialogOverlay, AddAccountDialogSurface, AddAccountDialogBlurLayer);
        PrewarmDialogOverlay(DeleteAccountDialogOverlay, DeleteAccountDialogSurface, DeleteAccountDialogBlurLayer);
        PrewarmDialogOverlay(RenameAccountDialogOverlay, RenameAccountDialogSurface, RenameAccountDialogBlurLayer);

        foreach (var comboBox in FindVisualChildren<AnimatedComboBox>(this))
            comboBox.ApplyTemplate();
    }

    private void PrewarmDialogOverlay(Grid overlay, Border dialog, Border blurTarget)
    {
        var originalVisibility = overlay.Visibility;
        var originalOpacity = overlay.Opacity;

        overlay.BeginAnimation(OpacityProperty, null);
        overlay.Visibility = Visibility.Hidden;
        overlay.Opacity = 0;

        dialog.ApplyTemplate();
        blurTarget.ApplyTemplate();
        UpdateLayout();
        UpdateDialogBlur(dialog, blurTarget, DialogAnimationBlurScale);

        overlay.Visibility = originalVisibility;
        overlay.Opacity = originalOpacity;
    }

    private static void HideDialogOverlay(Grid overlay, Action? completed = null)
    {
        var currentOpacity = overlay.Opacity;
        overlay.BeginAnimation(OpacityProperty, null);

        overlay.Opacity = currentOpacity;
        if (currentOpacity <= 0)
        {
            overlay.Visibility = Visibility.Collapsed;
            completed?.Invoke();
            return;
        }

        var animation = new DoubleAnimation
        {
            From = currentOpacity,
            To = 0,
            Duration = DialogFadeOutDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };

        animation.Completed += (_, _) =>
        {
            overlay.Opacity = 0;
            overlay.Visibility = Visibility.Collapsed;
            completed?.Invoke();
        };

        overlay.BeginAnimation(OpacityProperty, animation);
    }

    private bool UpdateDialogBlur(FrameworkElement dialog, Border target, double renderScale = 1)
    {
        if (!dialog.IsVisible
            || WindowContentLayer.ActualWidth <= 0
            || WindowContentLayer.ActualHeight <= 0
            || dialog.ActualWidth <= 0
            || dialog.ActualHeight <= 0)
            return false;

        var dpi = VisualTreeHelper.GetDpi(this);
        var scale = Math.Clamp(renderScale, 0.25, 1);
        var renderDpiScaleX = dpi.DpiScaleX * scale;
        var renderDpiScaleY = dpi.DpiScaleY * scale;
        var renderDpi = new DpiScale(renderDpiScaleX, renderDpiScaleY);
        var sourceWidth = Math.Max(1, (int)Math.Ceiling(WindowContentLayer.ActualWidth * renderDpiScaleX));
        var sourceHeight = Math.Max(1, (int)Math.Ceiling(WindowContentLayer.ActualHeight * renderDpiScaleY));

        var rendered = new RenderTargetBitmap(
            sourceWidth,
            sourceHeight,
            96 * renderDpiScaleX,
            96 * renderDpiScaleY,
            PixelFormats.Pbgra32);
        rendered.Render(WindowContentLayer);

        var topLeft = dialog.TransformToVisual(WindowContentLayer).Transform(new Point(0, 0));
        var cropX = (int)Math.Round(topLeft.X * renderDpiScaleX);
        var cropY = (int)Math.Round(topLeft.Y * renderDpiScaleY);
        var cropWidth = Math.Max(1, (int)Math.Round(dialog.ActualWidth * renderDpiScaleX));
        var cropHeight = Math.Max(1, (int)Math.Round(dialog.ActualHeight * renderDpiScaleY));

        var targetRect = IntersectWithSource(new Int32Rect(cropX, cropY, cropWidth, cropHeight), sourceWidth, sourceHeight);
        if (targetRect.Width <= 0 || targetRect.Height <= 0)
            return false;

        var blurPaddingX = Math.Max(1, (int)Math.Ceiling(DialogBlurRadius * renderDpiScaleX * 2));
        var blurPaddingY = Math.Max(1, (int)Math.Ceiling(DialogBlurRadius * renderDpiScaleY * 2));
        var expandedRect = IntersectWithSource(
            new Int32Rect(
                targetRect.X - blurPaddingX,
                targetRect.Y - blurPaddingY,
                targetRect.Width + blurPaddingX * 2,
                targetRect.Height + blurPaddingY * 2),
            sourceWidth,
            sourceHeight);

        if (expandedRect.Width <= 0 || expandedRect.Height <= 0)
            return false;

        var expandedCrop = new CroppedBitmap(rendered, expandedRect);
        expandedCrop.Freeze();

        var blurredExpanded = CreateBlurredBitmap(expandedCrop, renderDpi);
        var blurredTarget = new CroppedBitmap(
            blurredExpanded,
            new Int32Rect(
                targetRect.X - expandedRect.X,
                targetRect.Y - expandedRect.Y,
                targetRect.Width,
                targetRect.Height));
        blurredTarget.Freeze();

        var dialogBackground = CreateDialogBackgroundBitmap(blurredTarget, renderDpi);

        var brush = EnsureLocalDialogBrush(target);
        brush.ImageSource = dialogBackground;
        return true;
    }

    private static ImageBrush EnsureLocalDialogBrush(Border target)
    {
        if (target.ReadLocalValue(Border.BackgroundProperty) is ImageBrush localBrush && !localBrush.IsFrozen)
            return localBrush;

        var brush = new ImageBrush
        {
            Stretch = Stretch.Fill
        };
        target.Background = brush;
        return brush;
    }

    private static BitmapSource CreateBlurredBitmap(BitmapSource source, DpiScale dpi)
    {
        var width = source.PixelWidth / dpi.DpiScaleX;
        var height = source.PixelHeight / dpi.DpiScaleY;
        var image = new Image
        {
            Source = source,
            Stretch = Stretch.Fill,
            Width = width,
            Height = height,
            SnapsToDevicePixels = true,
            Effect = new BlurEffect
            {
                Radius = DialogBlurRadius,
                RenderingBias = RenderingBias.Quality
            }
        };

        var size = new Size(width, height);
        image.Measure(size);
        image.Arrange(new Rect(size));
        image.UpdateLayout();

        var blurred = new RenderTargetBitmap(
            source.PixelWidth,
            source.PixelHeight,
            96 * dpi.DpiScaleX,
            96 * dpi.DpiScaleY,
            PixelFormats.Pbgra32);
        blurred.Render(image);
        blurred.Freeze();
        return blurred;
    }

    private static BitmapSource CreateDialogBackgroundBitmap(BitmapSource blurredSource, DpiScale dpi)
    {
        var width = blurredSource.PixelWidth / dpi.DpiScaleX;
        var height = blurredSource.PixelHeight / dpi.DpiScaleY;
        var rect = new Rect(0, 0, width, height);
        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25)), null, rect);
            context.DrawImage(blurredSource, rect);
            context.DrawRectangle(new SolidColorBrush(DialogBlurTintColor), null, rect);
        }

        var bitmap = new RenderTargetBitmap(
            blurredSource.PixelWidth,
            blurredSource.PixelHeight,
            96 * dpi.DpiScaleX,
            96 * dpi.DpiScaleY,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static Int32Rect IntersectWithSource(Int32Rect rect, int sourceWidth, int sourceHeight)
    {
        var x = Math.Max(0, rect.X);
        var y = Math.Max(0, rect.Y);
        var right = Math.Min(sourceWidth, rect.X + rect.Width);
        var bottom = Math.Min(sourceHeight, rect.Y + rect.Height);

        return new Int32Rect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typedChild)
                yield return typedChild;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }

    private void ResetUuidCopyButton()
    {
        IsUuidCopied = false;
        SetUuidCopyButtonCopied(false);
    }

    private void SetUuidCopyButtonCopied(bool isCopied)
    {
        if (CopyUuidButton is null)
            return;

        IsUuidCopied = isCopied;
        CopyUuidButton.IsHitTestVisible = !isCopied;
        CopyUuidButton.Cursor = isCopied ? System.Windows.Input.Cursors.Arrow : System.Windows.Input.Cursors.Hand;
        CopyUuidButton.Foreground = new SolidColorBrush(isCopied
            ? Color.FromRgb(0x7D, 0xFF, 0xB2)
            : Color.FromArgb(0xDF, 0xFF, 0xFF, 0xFF));

        if (CopyUuidIcon is not null)
        {
            CopyUuidIcon.FontFamily = isCopied
                ? new FontFamily("Segoe UI Symbol")
                : new FontFamily("Segoe MDL2 Assets");
            CopyUuidIcon.FontSize = isCopied ? 18 : 15;
            CopyUuidIcon.Text = isCopied ? "\u2713" : "\uE8C8";
        }
    }
}
