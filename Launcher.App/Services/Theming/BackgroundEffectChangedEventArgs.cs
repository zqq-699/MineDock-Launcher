/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.App.Services;

public sealed class BackgroundEffectChangedEventArgs : EventArgs
{
    public BackgroundEffectChangedEventArgs(string oldEffect, string newEffect)
    {
        OldEffect = oldEffect;
        NewEffect = newEffect;
    }

    public string OldEffect { get; }

    public string NewEffect { get; }
}
