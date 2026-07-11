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
using Launcher.App.Controls;

namespace Launcher.Tests.Controls.Lists;

public sealed class ProgressiveBlurLayoutCalculatorTests
{
    [Fact]
    public void Calculate_UsesBlurLengthForVisibleAndDirectBoundaries()
    {
        var layout = ProgressiveBlurLayoutCalculator.Calculate(
            width: 1000d,
            height: 700d,
            blurLength: 140d,
            visibleBlurBandHeight: 164d,
            maximumRadius: 24d,
            renderScale: 0.4d,
            new DpiScale(1d, 1d));

        Assert.Equal(140d, layout.PresentationHeight);
        Assert.Equal(140d, layout.DirectListStart);
        Assert.Equal(168d, layout.TextureHeight);
    }

    [Fact]
    public void Calculate_AlignsBothBoundariesToDevicePixels()
    {
        var layout = ProgressiveBlurLayoutCalculator.Calculate(
            width: 1000d,
            height: 700d,
            blurLength: 140.4d,
            visibleBlurBandHeight: 164.2d,
            maximumRadius: 24d,
            renderScale: 0.4d,
            new DpiScale(1.5d, 1.5d));

        Assert.Equal(211d / 1.5d, layout.PresentationHeight, precision: 10);
        Assert.Equal(211d / 1.5d, layout.DirectListStart, precision: 10);
        Assert.Equal(168d, layout.TextureHeight, precision: 10);
    }

    [Fact]
    public void Calculate_ClampsBothBoundariesToAvailableHeight()
    {
        var layout = ProgressiveBlurLayoutCalculator.Calculate(
            width: 1000d,
            height: 120d,
            blurLength: 140d,
            visibleBlurBandHeight: 164d,
            maximumRadius: 24d,
            renderScale: 0.4d,
            new DpiScale(1d, 1d));

        Assert.Equal(120d, layout.PresentationHeight);
        Assert.Equal(120d, layout.DirectListStart);
    }
}
