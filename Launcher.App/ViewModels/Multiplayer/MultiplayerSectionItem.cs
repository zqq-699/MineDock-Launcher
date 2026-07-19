/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using CommunityToolkit.Mvvm.ComponentModel;

namespace Launcher.App.ViewModels.Multiplayer;

public sealed partial class MultiplayerSectionItem : ObservableObject
{
    public MultiplayerSectionItem(MultiplayerPageSection section, string title, string iconKey)
    {
        Section = section;
        Title = title;
        IconKey = iconKey;
    }

    public MultiplayerPageSection Section { get; }

    public string Title { get; }

    public string IconKey { get; }

    [ObservableProperty]
    private bool isSelected;
}
