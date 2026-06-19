using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Launcher.Domain.Models;

namespace Launcher.App.Controls.Account;

public sealed class SkinCarousel3DControl : Grid
{
    private const int AnimationMilliseconds = 600;
    private const double CenterBrightness = 1;
    private const double SideBrightness = 0.48;

    public static readonly DependencyProperty PreviousSkinProperty =
        DependencyProperty.Register(
            nameof(PreviousSkin),
            typeof(LauncherSkinRecord),
            typeof(SkinCarousel3DControl),
            new PropertyMetadata(null, OnSkinPropertyChanged));

    public static readonly DependencyProperty SelectedSkinProperty =
        DependencyProperty.Register(
            nameof(SelectedSkin),
            typeof(LauncherSkinRecord),
            typeof(SkinCarousel3DControl),
            new PropertyMetadata(null, OnSelectedSkinChanged));

    public static readonly DependencyProperty NextSkinProperty =
        DependencyProperty.Register(
            nameof(NextSkin),
            typeof(LauncherSkinRecord),
            typeof(SkinCarousel3DControl),
            new PropertyMetadata(null, OnSkinPropertyChanged));

    public static readonly DependencyProperty PreviousCommandProperty =
        DependencyProperty.Register(
            nameof(PreviousCommand),
            typeof(ICommand),
            typeof(SkinCarousel3DControl),
            new PropertyMetadata(null));

    public static readonly DependencyProperty NextCommandProperty =
        DependencyProperty.Register(
            nameof(NextCommand),
            typeof(ICommand),
            typeof(SkinCarousel3DControl),
            new PropertyMetadata(null));

    private readonly Viewport3D viewport = new();
    private readonly Border leftHoverHint;
    private readonly Border rightHoverHint;
    private readonly Dictionary<Model3D, SkinCarouselSlot> hitSlots = [];
    private readonly Dictionary<SkinCarouselSlot, SlotVisual> currentSlotVisuals = [];
    private bool rebuildQueued;
    private bool rebuildRequestedWhileQueued;
    private SkinCarouselDirection? pendingDirection;
    private LauncherSkinRecord? previousRenderedSkin;
    private LauncherSkinRecord? selectedRenderedSkin;
    private LauncherSkinRecord? nextRenderedSkin;

    public SkinCarousel3DControl()
    {
        ClipToBounds = true;
        Focusable = false;
        viewport.ClipToBounds = true;
        viewport.Camera = new PerspectiveCamera(
            new Point3D(0, 4, 62),
            new Vector3D(0, 0, -62),
            new Vector3D(0, 1, 0),
            28);
        var hoverLayer = CreateHoverLayer();
        leftHoverHint = CreateHoverHint();
        rightHoverHint = CreateHoverHint();
        PositionHoverHint(leftHoverHint, SkinCarouselSlot.Left);
        PositionHoverHint(rightHoverHint, SkinCarouselSlot.Right);
        hoverLayer.Children.Add(leftHoverHint);
        hoverLayer.Children.Add(rightHoverHint);
        Children.Add(hoverLayer);
        Children.Add(viewport);

        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseMove += OnMouseMove;
        MouseLeave += (_, _) =>
        {
            Cursor = Cursors.Arrow;
            UpdateHoverHint(null);
        };
    }

    public LauncherSkinRecord? PreviousSkin
    {
        get => (LauncherSkinRecord?)GetValue(PreviousSkinProperty);
        set => SetValue(PreviousSkinProperty, value);
    }

    public LauncherSkinRecord? SelectedSkin
    {
        get => (LauncherSkinRecord?)GetValue(SelectedSkinProperty);
        set => SetValue(SelectedSkinProperty, value);
    }

    public LauncherSkinRecord? NextSkin
    {
        get => (LauncherSkinRecord?)GetValue(NextSkinProperty);
        set => SetValue(NextSkinProperty, value);
    }

    public ICommand? PreviousCommand
    {
        get => (ICommand?)GetValue(PreviousCommandProperty);
        set => SetValue(PreviousCommandProperty, value);
    }

    public ICommand? NextCommand
    {
        get => (ICommand?)GetValue(NextCommandProperty);
        set => SetValue(NextCommandProperty, value);
    }

    private static void OnSkinPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SkinCarousel3DControl)d).QueueRebuild();
    }

    private static void OnSelectedSkinChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (SkinCarousel3DControl)d;
        var newSkin = e.NewValue as LauncherSkinRecord;
        if (newSkin is not null)
        {
            if (SkinsMatch(newSkin, control.nextRenderedSkin))
                control.pendingDirection = SkinCarouselDirection.Next;
            else if (SkinsMatch(newSkin, control.previousRenderedSkin))
                control.pendingDirection = SkinCarouselDirection.Previous;
        }

        control.QueueRebuild();
    }

    private void QueueRebuild()
    {
        if (rebuildQueued)
        {
            rebuildRequestedWhileQueued = true;
            return;
        }

        rebuildQueued = true;
        Dispatcher.BeginInvoke(Rebuild, DispatcherPriority.Background);
    }

    private void Rebuild()
    {
        rebuildQueued = false;
        var shouldRebuildAgain = rebuildRequestedWhileQueued;
        rebuildRequestedWhileQueued = false;

        var oldSlotVisuals = currentSlotVisuals.Values.ToList();
        var direction = pendingDirection;
        pendingDirection = null;

        viewport.Children.Clear();
        currentSlotVisuals.Clear();
        hitSlots.Clear();
        UpdateHoverHint(null);

        var scene = new Model3DGroup
        {
            Children =
            {
                MinecraftSkinPreviewModelBuilder.CreateAmbientLight(),
                MinecraftSkinPreviewModelBuilder.CreateDirectionalLight()
            }
        };

        AddSlot(scene, SkinCarouselSlot.Left, PreviousSkin, oldSlotVisuals, direction);
        AddSlot(scene, SkinCarouselSlot.Center, SelectedSkin, oldSlotVisuals, direction);
        AddSlot(scene, SkinCarouselSlot.Right, NextSkin, oldSlotVisuals, direction);

        viewport.Children.Add(new ModelVisual3D { Content = scene });

        previousRenderedSkin = PreviousSkin;
        selectedRenderedSkin = SelectedSkin;
        nextRenderedSkin = NextSkin;

        if (direction is not null && currentSlotVisuals.Count > 0)
            AnimateSlots();

        if (shouldRebuildAgain && direction is null)
            QueueRebuild();
    }

    private void AddSlot(
        Model3DGroup scene,
        SkinCarouselSlot targetSlot,
        LauncherSkinRecord? skin,
        IReadOnlyList<SlotVisual> oldSlotVisuals,
        SkinCarouselDirection? direction)
    {
        if (skin is null || string.IsNullOrWhiteSpace(skin.Source))
            return;

        BitmapImage skinBitmap;
        try
        {
            skinBitmap = MinecraftSkinPreviewModelBuilder.LoadSkinBitmap(skin.Source);
        }
        catch
        {
            return;
        }

        var targetPlacement = SkinCarousel3DLayout.GetPlacement(targetSlot);
        var startPlacement = ResolveStartPlacement(skin, targetSlot, oldSlotVisuals, direction);
        var scale = new ScaleTransform3D(
            startPlacement.Scale,
            startPlacement.Scale,
            startPlacement.Scale,
            0,
            4,
            0);
        var translate = new TranslateTransform3D(startPlacement.X, 0, 0);
        var transform = new Transform3DGroup();
        transform.Children.Add(scale);
        transform.Children.Add(translate);

        var slotGroup = new Model3DGroup { Transform = transform };
        slotGroup.Children.Add(MinecraftSkinPreviewModelBuilder.BuildPlayerModel(
            skinBitmap,
            skin.SkinModel,
            targetSlot is SkinCarouselSlot.Center ? CenterBrightness : SideBrightness));
        scene.Children.Add(slotGroup);

        RegisterHitModels(slotGroup, targetSlot);
        currentSlotVisuals[targetSlot] = new SlotVisual(skin, targetSlot, scale, translate, targetPlacement);
    }

    private static SkinCarouselSlotPlacement ResolveStartPlacement(
        LauncherSkinRecord skin,
        SkinCarouselSlot targetSlot,
        IReadOnlyList<SlotVisual> oldSlotVisuals,
        SkinCarouselDirection? direction)
    {
        var oldVisual = oldSlotVisuals.FirstOrDefault(visual => SkinsMatch(visual.Skin, skin));
        if (oldVisual is not null)
            return oldVisual.GetCurrentPlacement();

        return direction switch
        {
            SkinCarouselDirection.Next => SkinCarousel3DLayout.GetEntryPlacement(SkinCarouselDirection.Next),
            SkinCarouselDirection.Previous => SkinCarousel3DLayout.GetEntryPlacement(SkinCarouselDirection.Previous),
            _ => SkinCarousel3DLayout.GetPlacement(targetSlot)
        };
    }

    private void AnimateSlots()
    {
        var easing = new PowerEase { EasingMode = EasingMode.EaseOut, Power = 7 };

        foreach (var visual in currentSlotVisuals.Values)
        {
            var translateAnimation = CreateAnimation(visual.TargetPlacement.X, easing);
            visual.Translate.BeginAnimation(TranslateTransform3D.OffsetXProperty, translateAnimation);
            visual.Scale.BeginAnimation(ScaleTransform3D.ScaleXProperty, CreateAnimation(visual.TargetPlacement.Scale, easing));
            visual.Scale.BeginAnimation(ScaleTransform3D.ScaleYProperty, CreateAnimation(visual.TargetPlacement.Scale, easing));
            visual.Scale.BeginAnimation(ScaleTransform3D.ScaleZProperty, CreateAnimation(visual.TargetPlacement.Scale, easing));
        }
    }

    private static DoubleAnimation CreateAnimation(double to, IEasingFunction easing)
    {
        return new DoubleAnimation(to, TimeSpan.FromMilliseconds(AnimationMilliseconds))
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.HoldEnd
        };
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var slot = HitTestSlot(e.GetPosition(this));
        if (slot is SkinCarouselSlot.Left)
            ExecuteCommand(PreviousCommand);
        else if (slot is SkinCarouselSlot.Right)
            ExecuteCommand(NextCommand);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var slot = HitTestSlot(e.GetPosition(this));
        var canClickSide = CanClickSlot(slot);
        Cursor = canClickSide
            ? Cursors.Hand
            : Cursors.Arrow;
        UpdateHoverHint(canClickSide ? slot : null);
    }

    private SkinCarouselSlot? HitTestSlot(Point point)
    {
        SkinCarouselSlot? slot = null;
        VisualTreeHelper.HitTest(
            viewport,
            null,
            result =>
            {
                if (result is RayHitTestResult rayResult
                    && rayResult.ModelHit is not null
                    && hitSlots.TryGetValue(rayResult.ModelHit, out var hitSlot))
                {
                    slot = hitSlot;
                    return HitTestResultBehavior.Stop;
                }

                return HitTestResultBehavior.Continue;
            },
            new PointHitTestParameters(TranslatePoint(point, viewport)));

        return slot;
    }

    private bool CanClickSlot(SkinCarouselSlot? slot)
    {
        return slot switch
        {
            SkinCarouselSlot.Left => PreviousCommand?.CanExecute(null) == true,
            SkinCarouselSlot.Right => NextCommand?.CanExecute(null) == true,
            _ => false
        };
    }

    private void UpdateHoverHint(SkinCarouselSlot? slot)
    {
        leftHoverHint.Visibility = slot is SkinCarouselSlot.Left ? Visibility.Visible : Visibility.Collapsed;
        rightHoverHint.Visibility = slot is SkinCarouselSlot.Right ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Grid CreateHoverLayer()
    {
        var layer = new Grid
        {
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        return layer;
    }

    private static Border CreateHoverHint()
    {
        return new Border
        {
            Width = 96,
            Height = 148,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
    }

    private static void PositionHoverHint(Border hint, SkinCarouselSlot slot)
    {
        var offset = slot is SkinCarouselSlot.Left ? -175 : 175;
        hint.RenderTransform = new TranslateTransform(offset, 0);
    }

    private static void ExecuteCommand(ICommand? command)
    {
        if (command?.CanExecute(null) == true)
            command.Execute(null);
    }

    private void RegisterHitModels(Model3D model, SkinCarouselSlot slot)
    {
        hitSlots[model] = slot;
        if (model is Model3DGroup group)
        {
            foreach (var child in group.Children)
                RegisterHitModels(child, slot);
        }
    }

    private static bool SkinsMatch(LauncherSkinRecord? left, LauncherSkinRecord? right)
    {
        if (left is null || right is null)
            return false;

        if (string.Equals(left.Id, right.Id, StringComparison.Ordinal))
            return true;

        return !string.IsNullOrWhiteSpace(left.ContentHash)
            && string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record SlotVisual(
        LauncherSkinRecord Skin,
        SkinCarouselSlot Slot,
        ScaleTransform3D Scale,
        TranslateTransform3D Translate,
        SkinCarouselSlotPlacement TargetPlacement)
    {
        public SkinCarouselSlotPlacement GetCurrentPlacement()
        {
            return new SkinCarouselSlotPlacement(Translate.OffsetX, Scale.ScaleX);
        }
    }
}

public enum SkinCarouselSlot
{
    Left,
    Center,
    Right
}

public enum SkinCarouselDirection
{
    Previous,
    Next
}

public readonly record struct SkinCarouselSlotPlacement(double X, double Scale);

public static class SkinCarousel3DLayout
{
    private static readonly SkinCarouselSlotPlacement LeftPlacement = new(-10.0, 0.21);
    private static readonly SkinCarouselSlotPlacement CenterPlacement = new(0, 0.30);
    private static readonly SkinCarouselSlotPlacement RightPlacement = new(10.0, 0.21);
    private static readonly SkinCarouselSlotPlacement LeftEntryPlacement = new(-19.0, 0.21);
    private static readonly SkinCarouselSlotPlacement RightEntryPlacement = new(19.0, 0.21);

    public static SkinCarouselSlotPlacement GetPlacement(SkinCarouselSlot slot)
    {
        return slot switch
        {
            SkinCarouselSlot.Left => LeftPlacement,
            SkinCarouselSlot.Right => RightPlacement,
            _ => CenterPlacement
        };
    }

    public static SkinCarouselSlotPlacement GetEntryPlacement(SkinCarouselDirection direction)
    {
        return direction is SkinCarouselDirection.Previous
            ? LeftEntryPlacement
            : RightEntryPlacement;
    }
}
