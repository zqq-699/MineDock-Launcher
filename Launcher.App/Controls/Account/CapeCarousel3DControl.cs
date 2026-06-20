using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using System.Xml.Linq;
using Launcher.Application.Accounts;

namespace Launcher.App.Controls.Account;

public sealed class CapeCarousel3DControl : Grid
{
    private const int AnimationMilliseconds = 600;
    private const double CenterBrightness = 1;
    private const double SideBrightness = 0.48;

    public static readonly DependencyProperty PreviousCapeProperty =
        DependencyProperty.Register(
            nameof(PreviousCape),
            typeof(AccountCapeOption),
            typeof(CapeCarousel3DControl),
            new PropertyMetadata(null, OnCapePropertyChanged));

    public static readonly DependencyProperty SelectedCapeProperty =
        DependencyProperty.Register(
            nameof(SelectedCape),
            typeof(AccountCapeOption),
            typeof(CapeCarousel3DControl),
            new PropertyMetadata(null, OnSelectedCapeChanged));

    public static readonly DependencyProperty NextCapeProperty =
        DependencyProperty.Register(
            nameof(NextCape),
            typeof(AccountCapeOption),
            typeof(CapeCarousel3DControl),
            new PropertyMetadata(null, OnCapePropertyChanged));

    public static readonly DependencyProperty PreviousCommandProperty =
        DependencyProperty.Register(
            nameof(PreviousCommand),
            typeof(ICommand),
            typeof(CapeCarousel3DControl),
            new PropertyMetadata(null));

    public static readonly DependencyProperty NextCommandProperty =
        DependencyProperty.Register(
            nameof(NextCommand),
            typeof(ICommand),
            typeof(CapeCarousel3DControl),
            new PropertyMetadata(null));

    private readonly Viewport3D viewport = new();
    private readonly Border leftHoverHint;
    private readonly Border rightHoverHint;
    private readonly Dictionary<Model3D, CapeCarouselSlot> hitSlots = [];
    private readonly Dictionary<CapeCarouselSlot, SlotVisual> currentSlotVisuals = [];
    private readonly Dictionary<string, BitmapSource> capeTextureCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> capeTextureRequests = new(StringComparer.Ordinal);
    private bool rebuildQueued;
    private bool rebuildRequestedWhileQueued;
    private bool isRebuilding;
    private bool isAnimating;
    private bool rebuildAfterAnimation;
    private int animationGeneration;
    private CapeCarouselDirection? pendingDirection;
    private AccountCapeOption? previousRenderedCape;
    private AccountCapeOption? selectedRenderedCape;
    private AccountCapeOption? nextRenderedCape;

    public CapeCarousel3DControl()
    {
        ClipToBounds = true;
        Focusable = false;
        viewport.ClipToBounds = true;
        viewport.Camera = new PerspectiveCamera(
            new Point3D(0, 8, 46),
            new Vector3D(0, 0, -46),
            new Vector3D(0, 1, 0),
            28);

        var hoverLayer = CreateHoverLayer();
        leftHoverHint = CreateHoverHint();
        rightHoverHint = CreateHoverHint();
        PositionHoverHint(leftHoverHint, CapeCarouselSlot.Left);
        PositionHoverHint(rightHoverHint, CapeCarouselSlot.Right);
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

    public AccountCapeOption? PreviousCape
    {
        get => (AccountCapeOption?)GetValue(PreviousCapeProperty);
        set => SetValue(PreviousCapeProperty, value);
    }

    public AccountCapeOption? SelectedCape
    {
        get => (AccountCapeOption?)GetValue(SelectedCapeProperty);
        set => SetValue(SelectedCapeProperty, value);
    }

    public AccountCapeOption? NextCape
    {
        get => (AccountCapeOption?)GetValue(NextCapeProperty);
        set => SetValue(NextCapeProperty, value);
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

    private static void OnCapePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((CapeCarousel3DControl)d).QueueRebuild();
    }

    private static void OnSelectedCapeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (CapeCarousel3DControl)d;
        var newCape = e.NewValue as AccountCapeOption;
        if (newCape is not null)
        {
            if (CapeCarousel3DLayout.CapesRepresentSameVisualItem(newCape, control.nextRenderedCape))
                control.pendingDirection = CapeCarouselDirection.Next;
            else if (CapeCarousel3DLayout.CapesRepresentSameVisualItem(newCape, control.previousRenderedCape))
                control.pendingDirection = CapeCarouselDirection.Previous;
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
        isRebuilding = true;
        rebuildQueued = false;
        var shouldRebuildAgain = rebuildRequestedWhileQueued;
        rebuildRequestedWhileQueued = false;

        var oldSlotVisuals = currentSlotVisuals.Values.ToList();
        var direction = CapeCarousel3DLayout.CanAnimateTransition(
            pendingDirection,
            previousRenderedCape,
            selectedRenderedCape,
            nextRenderedCape,
            PreviousCape,
            SelectedCape,
            NextCape)
            ? pendingDirection
            : null;
        pendingDirection = null;

        viewport.Children.Clear();
        currentSlotVisuals.Clear();
        hitSlots.Clear();
        UpdateHoverHint(null);

        var scene = new Model3DGroup
        {
            Children =
            {
                MinecraftCapePreviewModelBuilder.CreateAmbientLight(),
                MinecraftCapePreviewModelBuilder.CreateDirectionalLight()
            }
        };

        AddSlot(scene, CapeCarouselSlot.Left, PreviousCape, oldSlotVisuals, direction);
        AddSlot(scene, CapeCarouselSlot.Center, SelectedCape, oldSlotVisuals, direction);
        AddSlot(scene, CapeCarouselSlot.Right, NextCape, oldSlotVisuals, direction);

        viewport.Children.Add(new ModelVisual3D { Content = scene });

        previousRenderedCape = PreviousCape;
        selectedRenderedCape = SelectedCape;
        nextRenderedCape = NextCape;
        isRebuilding = false;

        if (direction is not null && currentSlotVisuals.Count > 0)
        {
            AnimateSlots();
        }
        else
        {
            isAnimating = false;
            animationGeneration++;
            if (rebuildAfterAnimation)
            {
                rebuildAfterAnimation = false;
                QueueRebuild();
            }
        }

        if (shouldRebuildAgain && direction is null)
            QueueRebuild();
        else if (shouldRebuildAgain)
            rebuildAfterAnimation = true;
    }

    private void AddSlot(
        Model3DGroup scene,
        CapeCarouselSlot targetSlot,
        AccountCapeOption? cape,
        IReadOnlyList<SlotVisual> oldSlotVisuals,
        CapeCarouselDirection? direction)
    {
        if (cape is null)
            return;

        Model3DGroup capeModel;
        try
        {
            var capeTexture = GetOrRequestCapeTexture(cape);
            capeModel = MinecraftCapePreviewModelBuilder.BuildCapeModel(
                cape,
                targetSlot is CapeCarouselSlot.Center ? CenterBrightness : SideBrightness,
                capeTexture);
        }
        catch
        {
            return;
        }

        var targetPlacement = CapeCarousel3DLayout.GetPlacement(targetSlot);
        var startPlacement = ResolveStartPlacement(cape, targetSlot, oldSlotVisuals, direction);
        var scale = new ScaleTransform3D(
            startPlacement.Scale,
            startPlacement.Scale,
            startPlacement.Scale,
            0,
            8,
            0);
        var translate = new TranslateTransform3D(startPlacement.X, 0, 0);
        var transform = new Transform3DGroup();
        transform.Children.Add(scale);
        transform.Children.Add(translate);
        capeModel.Transform = CombineTransforms(capeModel.Transform, transform);
        scene.Children.Add(capeModel);

        RegisterHitModels(capeModel, targetSlot);
        currentSlotVisuals[targetSlot] = new SlotVisual(cape, targetSlot, scale, translate, targetPlacement);
    }

    private BitmapSource? GetOrRequestCapeTexture(AccountCapeOption cape)
    {
        if (cape.IsNone || string.IsNullOrWhiteSpace(cape.ImageUrl))
            return null;

        var source = cape.ImageUrl;
        if (capeTextureCache.TryGetValue(source, out var cachedTexture))
            return cachedTexture;

        if (capeTextureRequests.Add(source))
            BeginLoadCapeTexture(source);

        return null;
    }

    private void BeginLoadCapeTexture(string source)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(source, UriKind.RelativeOrAbsolute);
            bitmap.EndInit();

            if (bitmap.IsDownloading)
            {
                bitmap.DownloadCompleted += (_, _) => StoreLoadedCapeTexture(source, bitmap);
                bitmap.DownloadFailed += (_, _) => capeTextureRequests.Remove(source);
            }
            else
            {
                StoreLoadedCapeTexture(source, bitmap);
            }
        }
        catch
        {
            capeTextureRequests.Remove(source);
        }
    }

    private void StoreLoadedCapeTexture(string source, BitmapSource texture)
    {
        try
        {
            var frozenTexture = FreezeTexture(texture);
            capeTextureCache[source] = frozenTexture;
            capeTextureRequests.Remove(source);
            QueueTextureRefresh();
        }
        catch
        {
            capeTextureRequests.Remove(source);
        }
    }

    private void QueueTextureRefresh()
    {
        if (isRebuilding || isAnimating)
        {
            rebuildAfterAnimation = true;
            return;
        }

        QueueRebuild();
    }

    private static BitmapSource FreezeTexture(BitmapSource texture)
    {
        BitmapSource frozenTexture = texture;
        if (texture is BitmapImage bitmapImage && bitmapImage.IsDownloading)
            return frozenTexture;

        if (!texture.IsFrozen && texture.CanFreeze)
        {
            texture.Freeze();
            frozenTexture = texture;
        }

        return frozenTexture;
    }

    private static Transform3D CombineTransforms(Transform3D existing, Transform3D added)
    {
        if (existing == Transform3D.Identity)
            return added;

        var transform = new Transform3DGroup();
        transform.Children.Add(existing);
        transform.Children.Add(added);
        return transform;
    }

    private static CapeCarouselSlotPlacement ResolveStartPlacement(
        AccountCapeOption cape,
        CapeCarouselSlot targetSlot,
        IReadOnlyList<SlotVisual> oldSlotVisuals,
        CapeCarouselDirection? direction)
    {
        if (direction is null)
            return CapeCarousel3DLayout.GetPlacement(targetSlot);

        var oldVisual = oldSlotVisuals.FirstOrDefault(visual => CapesMatch(visual.Cape, cape));
        if (oldVisual is not null)
            return oldVisual.GetCurrentPlacement();

        return direction switch
        {
            CapeCarouselDirection.Next => CapeCarousel3DLayout.GetEntryPlacement(CapeCarouselDirection.Next),
            CapeCarouselDirection.Previous => CapeCarousel3DLayout.GetEntryPlacement(CapeCarouselDirection.Previous),
            _ => CapeCarousel3DLayout.GetPlacement(targetSlot)
        };
    }

    private void AnimateSlots()
    {
        var easing = new PowerEase { EasingMode = EasingMode.EaseOut, Power = 7 };
        isAnimating = true;
        var generation = ++animationGeneration;

        foreach (var visual in currentSlotVisuals.Values)
        {
            visual.Translate.BeginAnimation(TranslateTransform3D.OffsetXProperty, CreateAnimation(visual.TargetPlacement.X, easing));
            visual.Scale.BeginAnimation(ScaleTransform3D.ScaleXProperty, CreateAnimation(visual.TargetPlacement.Scale, easing));
            visual.Scale.BeginAnimation(ScaleTransform3D.ScaleYProperty, CreateAnimation(visual.TargetPlacement.Scale, easing));
            visual.Scale.BeginAnimation(ScaleTransform3D.ScaleZProperty, CreateAnimation(visual.TargetPlacement.Scale, easing));
        }

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(AnimationMilliseconds + 40)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (generation != animationGeneration)
                return;

            isAnimating = false;
            if (!rebuildAfterAnimation)
                return;

            rebuildAfterAnimation = false;
            QueueRebuild();
        };
        timer.Start();
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
        if (slot is CapeCarouselSlot.Left)
            ExecuteCommand(PreviousCommand);
        else if (slot is CapeCarouselSlot.Right)
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

    private CapeCarouselSlot? HitTestSlot(Point point)
    {
        CapeCarouselSlot? slot = null;
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

    private bool CanClickSlot(CapeCarouselSlot? slot)
    {
        if (isAnimating)
            return false;

        return slot switch
        {
            CapeCarouselSlot.Left => PreviousCommand?.CanExecute(null) == true,
            CapeCarouselSlot.Right => NextCommand?.CanExecute(null) == true,
            _ => false
        };
    }

    private void UpdateHoverHint(CapeCarouselSlot? slot)
    {
        leftHoverHint.Visibility = slot is CapeCarouselSlot.Left ? Visibility.Visible : Visibility.Collapsed;
        rightHoverHint.Visibility = slot is CapeCarouselSlot.Right ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Grid CreateHoverLayer()
    {
        return new Grid
        {
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Stretch
        };
    }

    private static Border CreateHoverHint()
    {
        var hint = new Border
        {
            Width = 116,
            Height = 154,
            CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        hint.SetResourceReference(Border.BackgroundProperty, "Brush.Control.Hover");
        return hint;
    }

    private static void PositionHoverHint(Border hint, CapeCarouselSlot slot)
    {
        var offset = slot is CapeCarouselSlot.Left ? -175 : 175;
        hint.RenderTransform = new TranslateTransform(offset, 0);
    }

    private static void ExecuteCommand(ICommand? command)
    {
        if (command?.CanExecute(null) == true)
            command.Execute(null);
    }

    private void RegisterHitModels(Model3D model, CapeCarouselSlot slot)
    {
        hitSlots[model] = slot;
        if (model is Model3DGroup group)
        {
            foreach (var child in group.Children)
                RegisterHitModels(child, slot);
        }
    }

    private static bool CapesMatch(AccountCapeOption? left, AccountCapeOption? right)
    {
        return CapeCarousel3DLayout.CapesRepresentSameVisualItem(left, right);
    }

    private sealed record SlotVisual(
        AccountCapeOption Cape,
        CapeCarouselSlot Slot,
        ScaleTransform3D Scale,
        TranslateTransform3D Translate,
        CapeCarouselSlotPlacement TargetPlacement)
    {
        public CapeCarouselSlotPlacement GetCurrentPlacement()
        {
            return new CapeCarouselSlotPlacement(Translate.OffsetX, Scale.ScaleX);
        }
    }
}

public enum CapeCarouselSlot
{
    Left,
    Center,
    Right
}

public enum CapeCarouselDirection
{
    Previous,
    Next
}

public readonly record struct CapeCarouselSlotPlacement(double X, double Scale);

public static class CapeCarousel3DLayout
{
    private static readonly CapeCarouselSlotPlacement LeftPlacement = new(-7.5, 0.25);
    private static readonly CapeCarouselSlotPlacement CenterPlacement = new(0, 0.36);
    private static readonly CapeCarouselSlotPlacement RightPlacement = new(7.5, 0.25);
    private static readonly CapeCarouselSlotPlacement LeftEntryPlacement = new(-14.5, 0.25);
    private static readonly CapeCarouselSlotPlacement RightEntryPlacement = new(14.5, 0.25);

    public static CapeCarouselSlotPlacement GetPlacement(CapeCarouselSlot slot)
    {
        return slot switch
        {
            CapeCarouselSlot.Left => LeftPlacement,
            CapeCarouselSlot.Right => RightPlacement,
            _ => CenterPlacement
        };
    }

    public static CapeCarouselSlotPlacement GetEntryPlacement(CapeCarouselDirection direction)
    {
        return direction is CapeCarouselDirection.Previous
            ? LeftEntryPlacement
            : RightEntryPlacement;
    }

    public static bool CanAnimateTransition(
        CapeCarouselDirection? direction,
        AccountCapeOption? oldPreviousCape,
        AccountCapeOption? oldSelectedCape,
        AccountCapeOption? oldNextCape,
        AccountCapeOption? newPreviousCape,
        AccountCapeOption? newSelectedCape,
        AccountCapeOption? newNextCape)
    {
        return direction switch
        {
            CapeCarouselDirection.Next =>
                CapesRepresentSameVisualItem(newPreviousCape, oldSelectedCape)
                && CapesRepresentSameVisualItem(newSelectedCape, oldNextCape),
            CapeCarouselDirection.Previous =>
                CapesRepresentSameVisualItem(newSelectedCape, oldPreviousCape)
                && CapesRepresentSameVisualItem(newNextCape, oldSelectedCape),
            _ => false
        };
    }

    public static bool CapesRepresentSameVisualItem(AccountCapeOption? left, AccountCapeOption? right)
    {
        if (left is null || right is null)
            return false;

        if (left.IsNone || right.IsNone)
            return left.IsNone && right.IsNone;

        if (!string.IsNullOrWhiteSpace(left.Id) && !string.IsNullOrWhiteSpace(right.Id))
            return string.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase);

        return !string.IsNullOrWhiteSpace(left.ImageUrl)
            && string.Equals(left.ImageUrl, right.ImageUrl, StringComparison.OrdinalIgnoreCase);
    }
}

public static class MinecraftCapePreviewModelBuilder
{
    private const int PixelScale = 8;
    private const string NoneCapeIconResource = "/Assets/Icons/account_page/account_page_forbid.svg";

    public static AmbientLight CreateAmbientLight()
    {
        return new AmbientLight(Color.FromRgb(168, 168, 168));
    }

    public static DirectionalLight CreateDirectionalLight()
    {
        return new DirectionalLight(Color.FromRgb(132, 132, 132), new Vector3D(-0.2, -0.35, -0.9));
    }

    public static Model3DGroup BuildCapeModel(
        AccountCapeOption cape,
        double brightness = 1,
        BitmapSource? texture = null)
    {
        if (cape.IsNone)
            return BuildNoneCapeModel(brightness);

        var model = new Model3DGroup();
        model.Transform = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), -6), 0, 8, 0);
        var baseMaterial = CreateSolidMaterial(Color.FromArgb(
            160,
            ApplyBrightness(74, brightness),
            ApplyBrightness(92, brightness),
            ApplyBrightness(118, brightness)));

        AddSolidFace(model, new Rect3D(-5.15, -0.15, 0.42, 10.3, 16.3, 0), baseMaterial);
        AddSolidFace(model, new Rect3D(-5.15, -0.15, -0.58, 10.3, 16.3, 0), baseMaterial);

        if (string.IsNullOrWhiteSpace(cape.ImageUrl) || texture is null)
            return model;

        try
        {
            AddFace(model, texture, new Rect3D(-5, 0, 0.5, 10, 16, 0), new Int32Rect(1, 1, 10, 16), brightness);
            AddFace(model, texture, new Rect3D(-5, 0, -0.5, 10, 16, 0), new Int32Rect(12, 1, 10, 16), brightness);
            AddFace(model, texture, new Rect3D(-5, 16, -0.5, 10, 0, 1), new Int32Rect(1, 0, 10, 1), brightness);
            AddFace(model, texture, new Rect3D(-5, 0, -0.5, 10, 0, 1), new Int32Rect(11, 0, 10, 1), brightness);
            AddFace(model, texture, new Rect3D(-5, 0, -0.5, 0, 16, 1), new Int32Rect(0, 1, 1, 16), brightness);
            AddFace(model, texture, new Rect3D(5, 0, -0.5, 0, 16, 1), new Int32Rect(11, 1, 1, 16), brightness);
        }
        catch
        {
        }

        return model;
    }

    private static Model3DGroup BuildNoneCapeModel(double brightness)
    {
        var model = new Model3DGroup();
        model.Transform = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), -6), 0, 8, 0);
        var surface = CreateSolidMaterial(Color.FromArgb(
            42,
            ApplyBrightness(255, brightness),
            ApplyBrightness(255, brightness),
            ApplyBrightness(255, brightness)));
        var sign = CreateSvgIconMaterial(NoneCapeIconResource, brightness);

        AddSolidFace(model, new Rect3D(-5, 0, 0, 10, 16, 0), surface);
        if (sign is not null)
            AddSolidFace(model, new Rect3D(-2.2, 5.25, 0.08, 4.4, 4.4, 0), sign);
        return model;
    }

    private static void AddFace(
        Model3DGroup group,
        BitmapSource texture,
        Rect3D bounds,
        Int32Rect textureRect,
        double brightness)
    {
        var material = CreateImageMaterial(texture, textureRect, brightness);
        group.Children.Add(new GeometryModel3D
        {
            Geometry = CreateFaceMesh(bounds),
            Material = material,
            BackMaterial = material
        });
    }

    private static void AddSolidFace(Model3DGroup group, Rect3D bounds, Material material, double rotationDegrees = 0)
    {
        var model = new GeometryModel3D
        {
            Geometry = CreateFaceMesh(bounds),
            Material = material,
            BackMaterial = material
        };

        if (Math.Abs(rotationDegrees) > double.Epsilon)
            model.Transform = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), rotationDegrees));

        group.Children.Add(model);
    }

    private static Material CreateImageMaterial(BitmapSource texture, Int32Rect textureRect, double brightness)
    {
        var brush = new ImageBrush(CreatePixelSharpFaceBitmap(texture, textureRect, brightness))
        {
            Stretch = Stretch.Fill,
            TileMode = TileMode.None
        };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
        brush.Freeze();
        var material = new DiffuseMaterial(brush);
        material.Freeze();
        return material;
    }

    private static Material CreateSolidMaterial(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var material = new DiffuseMaterial(brush);
        material.Freeze();
        return material;
    }

    private static Material? CreateSvgIconMaterial(string resourcePath, double brightness)
    {
        try
        {
            var iconBitmap = RenderSvgIconBitmap(resourcePath, brightness);
            var brush = new ImageBrush(iconBitmap)
            {
                Stretch = Stretch.Fill,
                TileMode = TileMode.None
            };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
            brush.Freeze();
            var material = new DiffuseMaterial(brush);
            material.Freeze();
            return material;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource RenderSvgIconBitmap(string resourcePath, double brightness)
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri(resourcePath, UriKind.Relative));
        if (resource is null)
            throw new InvalidOperationException("SVG resource was not found.");

        using var stream = resource.Stream;
        var document = XDocument.Load(stream);
        var root = document.Root ?? throw new InvalidOperationException("SVG root was not found.");
        var viewBox = ParseViewBox(root.Attribute("viewBox")?.Value);
        var geometries = root
            .Descendants()
            .Where(element => element.Name.LocalName == "path")
            .Select(element => (
                Geometry: Geometry.Parse(element.Attribute("d")?.Value ?? string.Empty),
                StrokeWidth: ParseDouble(element.Attribute("stroke-width")?.Value, 4)))
            .ToList();

        const int size = 128;
        var colorValue = ApplyBrightness(255, brightness);
        var brush = new SolidColorBrush(Color.FromArgb(230, colorValue, colorValue, colorValue));
        brush.Freeze();
        var drawingVisual = new DrawingVisual();
        using (var context = drawingVisual.RenderOpen())
        {
            var scale = Math.Min(size / viewBox.Width, size / viewBox.Height);
            var offsetX = (size - viewBox.Width * scale) / 2;
            var offsetY = (size - viewBox.Height * scale) / 2;
            context.PushTransform(new TranslateTransform(offsetX, offsetY));
            context.PushTransform(new ScaleTransform(scale, scale));
            context.PushTransform(new TranslateTransform(-viewBox.X, -viewBox.Y));
            foreach (var item in geometries)
            {
                item.Geometry.Freeze();
                var pen = new Pen(brush, item.StrokeWidth)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round
                };
                pen.Freeze();
                context.DrawGeometry(null, pen, item.Geometry);
            }
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawingVisual);
        bitmap.Freeze();
        return bitmap;
    }

    private static Rect ParseViewBox(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new Rect(0, 0, 48, 48);

        var parts = value
            .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => double.Parse(part, CultureInfo.InvariantCulture))
            .ToArray();

        return parts.Length == 4
            ? new Rect(parts[0], parts[1], parts[2], parts[3])
            : new Rect(0, 0, 48, 48);
    }

    private static double ParseDouble(string? value, double fallback)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : fallback;
    }

    private static MeshGeometry3D CreateFaceMesh(Rect3D b)
    {
        var p0 = new Point3D(b.X, b.Y + b.SizeY, b.Z);
        var p1 = new Point3D(b.X + b.SizeX, b.Y + b.SizeY, b.Z + b.SizeZ);
        var p2 = new Point3D(b.X + b.SizeX, b.Y, b.Z + b.SizeZ);
        var p3 = new Point3D(b.X, b.Y, b.Z);
        var mesh = new MeshGeometry3D
        {
            Positions = [p0, p1, p2, p3],
            TextureCoordinates =
            [
                new Point(0, 0),
                new Point(1, 0),
                new Point(1, 1),
                new Point(0, 1)
            ],
            TriangleIndices = [0, 1, 2, 0, 2, 3]
        };
        mesh.Freeze();
        return mesh;
    }

    public static BitmapSource CreatePixelSharpFaceBitmap(
        BitmapSource texture,
        Int32Rect textureRect,
        double brightness)
    {
        brightness = Math.Clamp(brightness, 0, 1);
        var source = EnsureBgra32(texture);
        var x = Math.Clamp(textureRect.X, 0, source.PixelWidth - 1);
        var y = Math.Clamp(textureRect.Y, 0, source.PixelHeight - 1);
        var rect = new Int32Rect(
            x,
            y,
            Math.Max(1, Math.Min(textureRect.Width, source.PixelWidth - x)),
            Math.Max(1, Math.Min(textureRect.Height, source.PixelHeight - y)));
        var stride = rect.Width * 4;
        var pixels = new byte[stride * rect.Height];
        source.CopyPixels(rect, pixels, stride, 0);

        var outputWidth = rect.Width * PixelScale;
        var outputHeight = rect.Height * PixelScale;
        var outputStride = outputWidth * 4;
        var output = new byte[outputStride * outputHeight];
        for (var outputY = 0; outputY < outputHeight; outputY++)
        {
            var sourceY = outputY / PixelScale;
            for (var outputX = 0; outputX < outputWidth; outputX++)
            {
                var sourceX = outputX / PixelScale;
                var sourceIndex = sourceY * stride + sourceX * 4;
                var outputIndex = outputY * outputStride + outputX * 4;
                output[outputIndex] = ApplyBrightness(pixels[sourceIndex], brightness);
                output[outputIndex + 1] = ApplyBrightness(pixels[sourceIndex + 1], brightness);
                output[outputIndex + 2] = ApplyBrightness(pixels[sourceIndex + 2], brightness);
                output[outputIndex + 3] = pixels[sourceIndex + 3];
            }
        }

        var bitmap = BitmapSource.Create(
            outputWidth,
            outputHeight,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            output,
            outputStride);
        bitmap.Freeze();
        return bitmap;
    }

    private static byte ApplyBrightness(byte value, double brightness)
    {
        return (byte)Math.Round(value * brightness);
    }

    private static BitmapSource EnsureBgra32(BitmapSource source)
    {
        if (source.Format == PixelFormats.Bgra32)
            return source;

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }
}
