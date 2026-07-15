/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Minecraft;

internal interface ILoaderRepairPublicationProgress
{
    void ReportLoaderPublication(bool completed);
}

/// <summary>
/// Converts provider installation progress into one monotonic slice of the
/// launch progress contract without changing the providers' install UI contract.
/// </summary>
internal sealed class LaunchRepairProgressAdapter
    : IProgress<LauncherProgress>, ISpeedMeterProgress, ILoaderRepairPublicationProgress
{
    private const double InitialValidationEnd = 12;
    private const double LoaderDownloadStart = 12;
    private const double LoaderDownloadReported = 16;
    private const double LoaderProcessorStart = 22;
    private const double LoaderProcessorEnd = 50;
    private const double LoaderFinalizationStart = 50;
    private const double LoaderCompletionStart = 58;
    private const double LoaderFinalizationEnd = 65;
    private const double LoaderPublicationEnd = 70;
    private const double StandardRepairEnd = 84;

    private readonly IProgress<LauncherProgress> inner;
    private LoaderPhase loaderPhase;
    private bool loaderRepairObserved;
    private double lastPercent = InitialValidationEnd;

    public LaunchRepairProgressAdapter(IProgress<LauncherProgress> inner)
    {
        this.inner = inner;
        SpeedMeter = SpeedMeterProgress.TryGet(inner);
    }

    public SpeedMeter? SpeedMeter { get; }

    public void Report(LauncherProgress value)
    {
        if (value.DownloadSpeedTelemetry is not null)
        {
            inner.Report(value);
            return;
        }

        switch (value.Stage)
        {
            case InstallProgressStages.Preparing:
                loaderRepairObserved = true;
                loaderPhase = LoaderPhase.Preparing;
                Emit(value, LaunchProgressStages.RepairingLoaderInstaller, LoaderDownloadStart);
                return;
            case InstallProgressStages.DownloadingLoaderInstaller:
                loaderRepairObserved = true;
                loaderPhase = LoaderPhase.Preparing;
                Emit(value, LaunchProgressStages.RepairingLoaderInstaller, LoaderDownloadReported);
                return;
            case InstallProgressStages.RunningLoaderInstaller:
                loaderRepairObserved = true;
                loaderPhase = LoaderPhase.Running;
                Emit(value, LaunchProgressStages.RunningLoaderInstaller, LoaderProcessorStart);
                return;
            case InstallProgressStages.FinalizingVersion:
                loaderRepairObserved = true;
                loaderPhase = LoaderPhase.Finalizing;
                Emit(value, LaunchProgressStages.FinalizingLoaderVersion, LoaderFinalizationStart);
                return;
            case InstallProgressStages.CompletingFiles:
                loaderRepairObserved = true;
                loaderPhase = LoaderPhase.Completing;
                Emit(value, LaunchProgressStages.FinalizingLoaderVersion, LoaderCompletionStart);
                return;
            case LaunchProgressStages.CheckingFiles:
            case LaunchProgressStages.DownloadingFiles:
                if (TryMapLoaderLocalProgress(value, out var loaderStage, out var loaderPercent))
                {
                    Emit(value, loaderStage, loaderPercent);
                    return;
                }
                break;
        }

        if (TryMapStandardRepair(value.Stage, out var standardPercent))
        {
            loaderPhase = LoaderPhase.None;
            Emit(value, value.Stage, standardPercent);
            return;
        }

        Emit(value, value.Stage, value.Percent);
    }

    public void ReportLoaderPublication(bool completed)
    {
        loaderRepairObserved = true;
        loaderPhase = LoaderPhase.Publishing;
        Emit(
            new LauncherProgress(
                LaunchProgressStages.PublishingLoaderArtifacts,
                string.Empty,
                completed ? LoaderPublicationEnd : LoaderFinalizationEnd),
            LaunchProgressStages.PublishingLoaderArtifacts,
            completed ? LoaderPublicationEnd : LoaderFinalizationEnd);
    }

    private bool TryMapLoaderLocalProgress(
        LauncherProgress value,
        out string stage,
        out double? percent)
    {
        stage = value.Stage;
        percent = null;
        if (!loaderRepairObserved || value.Percent is not double localPercent)
            return false;

        var fraction = Math.Clamp(localPercent, 0, 100) / 100d;
        switch (loaderPhase)
        {
            case LoaderPhase.Preparing:
                stage = LaunchProgressStages.RepairingLoaderInstaller;
                percent = Scale(fraction, LoaderDownloadReported, LoaderProcessorStart);
                return true;
            case LoaderPhase.Running:
                stage = LaunchProgressStages.RunningLoaderInstaller;
                percent = Scale(fraction, LoaderProcessorStart, LoaderProcessorEnd);
                return true;
            case LoaderPhase.Finalizing:
                stage = LaunchProgressStages.FinalizingLoaderVersion;
                percent = Scale(fraction, LoaderFinalizationStart, LoaderCompletionStart);
                return true;
            case LoaderPhase.Completing:
                stage = LaunchProgressStages.FinalizingLoaderVersion;
                percent = Scale(fraction, LoaderCompletionStart, LoaderFinalizationEnd);
                return true;
            default:
                return false;
        }
    }

    private bool TryMapStandardRepair(string stage, out double percent)
    {
        var fraction = stage switch
        {
            LaunchProgressStages.CheckingInstance => 0d,
            LaunchProgressStages.RepairingMetadata => 0.08d,
            LaunchProgressStages.RepairingJar => 0.25d,
            LaunchProgressStages.RepairingLibraries => 0.47d,
            LaunchProgressStages.RepairingAssets => 0.70d,
            LaunchProgressStages.RepairingLogging => 0.90d,
            LaunchProgressStages.CheckingFiles => 1d,
            _ => -1d
        };
        if (fraction < 0)
        {
            percent = 0;
            return false;
        }

        var start = loaderRepairObserved ? LoaderPublicationEnd : InitialValidationEnd;
        percent = Scale(fraction, start, StandardRepairEnd);
        return true;
    }

    private void Emit(LauncherProgress source, string stage, double? percent)
    {
        double? mappedPercent = null;
        if (percent is double candidate)
        {
            lastPercent = Math.Clamp(Math.Max(lastPercent, candidate), 0, 100);
            mappedPercent = lastPercent;
        }
        inner.Report(source with { Stage = stage, Percent = mappedPercent });
    }

    private static double Scale(double fraction, double start, double end) =>
        start + Math.Clamp(fraction, 0, 1) * (end - start);

    private enum LoaderPhase
    {
        None,
        Preparing,
        Running,
        Finalizing,
        Completing,
        Publishing
    }
}

internal sealed class LaunchProcessBuildProgressAdapter : IProgress<LauncherProgress>, ISpeedMeterProgress
{
    private const double StartPercent = 94;
    private const double EndPercent = 99;
    private readonly IProgress<LauncherProgress> inner;
    private double lastPercent = StartPercent;

    public LaunchProcessBuildProgressAdapter(IProgress<LauncherProgress> inner)
    {
        this.inner = inner;
        SpeedMeter = SpeedMeterProgress.TryGet(inner);
    }

    public SpeedMeter? SpeedMeter { get; }

    public void Report(LauncherProgress value)
    {
        if (value.DownloadSpeedTelemetry is not null)
        {
            inner.Report(value);
            return;
        }

        double? percent = null;
        if (value.Percent is double localPercent)
        {
            var scaled = StartPercent + Math.Clamp(localPercent, 0, 100) / 100d * (EndPercent - StartPercent);
            lastPercent = Math.Clamp(Math.Max(lastPercent, scaled), StartPercent, EndPercent);
            percent = lastPercent;
        }
        inner.Report(value with { Percent = percent });
    }
}
