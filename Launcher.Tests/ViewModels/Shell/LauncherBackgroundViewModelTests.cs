/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Shell;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.ViewModels.Shell;

public sealed class LauncherBackgroundViewModelTests : IDisposable
{
    private const string OnePixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

    private readonly string testDirectory = Path.Combine(
        Path.GetTempPath(),
        $"bhl-background-view-model-{Guid.NewGuid():N}");

    [Fact]
    public void ApplyEffectLoadsImageWithoutKeepingFileLockedAndOtherEffectsReleaseIt()
    {
        var imagePath = WriteValidImage("background.png");
        var catalog = new TestCatalog(testDirectory) { CandidatePaths = [imagePath] };
        var viewModel = CreateViewModel(catalog);

        viewModel.ApplyEffect(LauncherBackgroundEffects.Image, reportFailure: false);

        Assert.True(viewModel.IsActive);
        Assert.NotNull(viewModel.ImageSource);
        Assert.Equal(imagePath, viewModel.CurrentImagePath);
        using (new FileStream(imagePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
        }

        viewModel.ApplyEffect(LauncherBackgroundEffects.None, reportFailure: false);

        Assert.False(viewModel.IsActive);
        Assert.Null(viewModel.ImageSource);
        Assert.Null(viewModel.CurrentImagePath);
    }

    [Fact]
    public void EnumerationFailureFallsBackWithoutBlockingStartup()
    {
        var status = new TestStatusService();
        var catalog = new TestCatalog(testDirectory) { EnumerationException = new UnauthorizedAccessException() };
        var viewModel = CreateViewModel(catalog, statusService: status);

        viewModel.ApplyEffect(LauncherBackgroundEffects.Image, reportFailure: false);

        Assert.False(viewModel.IsActive);
        Assert.Null(viewModel.ImageSource);
        Assert.Null(status.LastMessage);
    }

    public void Dispose()
    {
        if (Directory.Exists(testDirectory))
            Directory.Delete(testDirectory, recursive: true);
    }

    private LauncherBackgroundViewModel CreateViewModel(
        TestCatalog catalog,
        Func<int, int>? nextRandomIndex = null,
        TestStatusService? statusService = null)
    {
        return new LauncherBackgroundViewModel(
            catalog,
            new LauncherBackgroundImageLoader(),
            new TestFolderService(),
            statusService ?? new TestStatusService(),
            logger: null,
            nextRandomIndex ?? (_ => 0));
    }

    private string WriteValidImage(string fileName)
    {
        Directory.CreateDirectory(testDirectory);
        var path = Path.Combine(testDirectory, fileName);
        File.WriteAllBytes(path, Convert.FromBase64String(OnePixelPng));
        return path;
    }

    private sealed class TestCatalog(string directoryPath) : ILauncherBackgroundImageCatalog
    {
        public string DirectoryPath { get; } = directoryPath;
        public IReadOnlyList<string> CandidatePaths { get; set; } = [];
        public Exception? EnumerationException { get; init; }
        public int ClearImagesCallCount { get; private set; }

        public string EnsureDirectoryExists()
        {
            Directory.CreateDirectory(DirectoryPath);
            return DirectoryPath;
        }

        public IReadOnlyList<string> GetCandidatePaths()
        {
            if (EnumerationException is not null)
                throw EnumerationException;
            return CandidatePaths;
        }

        public void ClearImages()
        {
            ClearImagesCallCount++;
        }
    }

    private sealed class TestFolderService : IInstanceFolderService
    {
        public bool DirectoryExists(string folderPath) => Directory.Exists(folderPath);
        public string EnsureDirectoryExists(string folderPath) => folderPath;
        public bool TryOpen(string folderPath) => true;
        public bool TryOpenFile(string filePath) => false;
        public bool TryRevealFile(string filePath) => false;
    }

    private sealed class TestStatusService : IStatusService
    {
#pragma warning disable CS0067
        public event Action<string>? MessageReported;
#pragma warning restore CS0067

        public string? LastMessage { get; private set; }

        public void Report(string message)
        {
            LastMessage = message;
        }
    }
}
