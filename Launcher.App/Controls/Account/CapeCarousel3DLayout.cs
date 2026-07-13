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
