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
using System.Windows.Threading;

namespace Launcher.App.Views.GameSettings;

public partial class InstanceGeneralSettingsView : UserControl
{
    public InstanceGeneralSettingsView()
    {
        InitializeComponent();
    }

    private void DescriptionTextBox_OnLoaded(object sender, RoutedEventArgs e)
    {
        QueueDescriptionTextBoxHeightUpdate();
    }

    private void DescriptionTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        QueueDescriptionTextBoxHeightUpdate();
    }

    private void DescriptionTextBox_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
            QueueDescriptionTextBoxHeightUpdate();
    }

    private void QueueDescriptionTextBoxHeightUpdate()
    {
        Dispatcher.BeginInvoke(UpdateDescriptionTextBoxHeight, DispatcherPriority.Background);
    }

    private void UpdateDescriptionTextBoxHeight()
    {
        DescriptionTextBox.UpdateLayout();
        var lineCount = Math.Max(1, DescriptionTextBox.LineCount);
        DescriptionTextBox.VerticalContentAlignment = lineCount > 1 ? VerticalAlignment.Top : VerticalAlignment.Center;
        var lineHeight = TextBlock.GetLineHeight(DescriptionTextBox);
        if (double.IsNaN(lineHeight) || lineHeight <= 0)
        {
            lineHeight = Math.Max(
                DescriptionTextBox.FontFamily.LineSpacing * DescriptionTextBox.FontSize,
                DescriptionTextBox.FontSize * 1.35);
        }
        var chromeAllowance = 16d;
        DescriptionTextBox.Height = Math.Max(34, Math.Ceiling((lineCount * lineHeight) + chromeAllowance));
    }
}
