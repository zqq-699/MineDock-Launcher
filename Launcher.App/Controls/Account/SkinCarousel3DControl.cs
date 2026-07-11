/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

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

/// <summary>
/// 用三个可复用 3D 槽位展示相邻皮肤，并统一管理鼠标命中、悬停提示和轮播动画。
/// </summary>
public sealed class SkinCarousel3DControl : Grid
{
    // Viewport3D 负责皮肤模型，普通 WPF 覆盖层负责文字提示；两层通过逻辑槽位保持一致。
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
        // 三个皮肤属性常在一次选择中连续变化，统一排队重建可避免渲染中间组合。
        ((SkinCarousel3DControl)d).QueueRebuild();
    }

    private static void OnSelectedSkinChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (SkinCarousel3DControl)d;
        var newSkin = e.NewValue as LauncherSkinRecord;
        // 用上一帧的左右项推断切换方向；命令执行后集合可能已经整体轮换，不能只比较索引。
        if (newSkin is not null)
        {
            if (SkinCarousel3DLayout.SkinsRepresentSameVisualItem(newSkin, control.nextRenderedSkin))
                control.pendingDirection = SkinCarouselDirection.Next;
            else if (SkinCarousel3DLayout.SkinsRepresentSameVisualItem(newSkin, control.previousRenderedSkin))
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

        // Background 优先级让同一 UI 周期的 Previous/Selected/Next 更新合并成一次模型构建。
        rebuildQueued = true;
        Dispatcher.BeginInvoke(Rebuild, DispatcherPriority.Background);
    }

    private void Rebuild()
    {
        // 保存旧槽位的当前动画位置，新模型可从该位置接续，避免快速切换时跳回固定起点。
        rebuildQueued = false;
        var shouldRebuildAgain = rebuildRequestedWhileQueued;
        rebuildRequestedWhileQueued = false;

        var oldSlotVisuals = currentSlotVisuals.Values.ToList();
        var direction = SkinCarousel3DLayout.CanAnimateTransition(
            pendingDirection,
            previousRenderedSkin,
            selectedRenderedSkin,
            nextRenderedSkin,
            PreviousSkin,
            SelectedSkin,
            NextSkin)
            ? pendingDirection
            : null;
        pendingDirection = null;

        // 命中映射与模型一起清空，否则已不可见的 Model3D 仍可能映射到旧皮肤槽位。
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

        // 单个皮肤加载失败只跳过该槽位，不应让中间皮肤或整个控件无法显示。
        BitmapImage skinBitmap;
        try
        {
            skinBitmap = MinecraftSkinPreviewModelBuilder.LoadSkinBitmap(skin.Source);
        }
        catch
        {
            return;
        }

        // 网格只构建一次，动画只修改 Scale/Translate，避免每帧重建 3D 几何和纹理。
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
        if (direction is null)
            return SkinCarousel3DLayout.GetPlacement(targetSlot);

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
        // 所有槽位共享缓动和时长，视觉上表现为一次整体轮播。
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
        // 中间槽位只是当前项；只有两侧槽位承担导航语义。
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
        // WPF 返回实际 GeometryModel3D，通过建模时登记的映射还原为稳定的逻辑槽位。
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
        var hint = new Border
        {
            Width = 96,
            Height = 148,
            CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        hint.SetResourceReference(Border.BackgroundProperty, "Brush.Control.Hover");
        return hint;
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
        return SkinCarousel3DLayout.SkinsRepresentSameVisualItem(left, right);
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
    // 布局和过渡判断保持为无状态计算，控件重建视觉树时不依赖旧 Model3D 对象。
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

    public static bool CanAnimateTransition(
        SkinCarouselDirection? direction,
        LauncherSkinRecord? oldPreviousSkin,
        LauncherSkinRecord? oldSelectedSkin,
        LauncherSkinRecord? oldNextSkin,
        LauncherSkinRecord? newPreviousSkin,
        LauncherSkinRecord? newSelectedSkin,
        LauncherSkinRecord? newNextSkin)
    {
        return direction switch
        {
            SkinCarouselDirection.Next =>
                SkinsRepresentSameVisualItem(newPreviousSkin, oldSelectedSkin)
                && SkinsRepresentSameVisualItem(newSelectedSkin, oldNextSkin),
            SkinCarouselDirection.Previous =>
                SkinsRepresentSameVisualItem(newSelectedSkin, oldPreviousSkin)
                && SkinsRepresentSameVisualItem(newNextSkin, oldSelectedSkin),
            _ => false
        };
    }

    public static bool SkinsRepresentSameVisualItem(LauncherSkinRecord? left, LauncherSkinRecord? right)
    {
        if (left is null || right is null)
            return false;

        if (!string.IsNullOrWhiteSpace(left.Id) && !string.IsNullOrWhiteSpace(right.Id))
            return string.Equals(left.Id, right.Id, StringComparison.Ordinal);

        if (!string.IsNullOrWhiteSpace(left.ContentHash) && !string.IsNullOrWhiteSpace(right.ContentHash))
        {
            return left.SkinModel == right.SkinModel
                && string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);
        }

        return left.SkinModel == right.SkinModel
            && !string.IsNullOrWhiteSpace(left.Source)
            && string.Equals(left.Source, right.Source, StringComparison.Ordinal);
    }
}
