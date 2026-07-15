/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Resources;
using Launcher.App.ViewModels.Home;
using Launcher.Domain.Models;

namespace Launcher.Tests.ViewModels.Home;

public sealed class HomeLaunchProgressTests
{
    [Fact]
    public void OverallPercentNeverMovesBackwardAndNullKeepsCurrentValue()
    {
        Assert.Equal(70, HomePageViewModel.MergeLaunchProgressPercent(70, 20));
        Assert.Equal(70, HomePageViewModel.MergeLaunchProgressPercent(70, null));
        Assert.Equal(85, HomePageViewModel.MergeLaunchProgressPercent(70, 85));
    }

    [Theory]
    [InlineData(LaunchProgressStages.FinalizingLoaderVersion)]
    [InlineData(LaunchProgressStages.PublishingLoaderArtifacts)]
    [InlineData(LaunchProgressStages.RevalidatingFiles)]
    public void NewLaunchStagesResolveLocalizedText(string stage)
    {
        var text = HomePageViewModel.FormatLaunchProgress(new LauncherProgress(stage, string.Empty));

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.NotEqual(Strings.Status_LaunchPreparing, text);
    }
}
