using System.Reflection;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Settings;
using Launcher.Application;
using Launcher.Application.Services;

namespace Launcher.Tests.ViewModels.Settings;

public sealed class SettingsFeedbackDialogViewModelTests
{
    [Fact]
    public void OpenAndCancelUpdateDialogState()
    {
        var viewModel = CreateFeedbackViewModel();

        viewModel.Open();
        Assert.True(viewModel.IsOpen);

        viewModel.CancelCommand.Execute(null);
        Assert.False(viewModel.IsOpen);
    }

    [Theory]
    [InlineData(true, LauncherProjectLinks.GitHubFeatureSuggestionsUrl)]
    [InlineData(false, LauncherProjectLinks.GitHubIssuesUrl)]
    public void LinkCommandsOpenExpectedPageAndKeepDialogOpen(bool featureSuggestions, string expectedUrl)
    {
        var externalLinks = new RecordingExternalLinkService();
        var viewModel = CreateFeedbackViewModel(externalLinks: externalLinks);
        viewModel.Open();

        if (featureSuggestions)
            viewModel.OpenFeatureSuggestionsCommand.Execute(null);
        else
            viewModel.OpenBugReportsCommand.Execute(null);

        Assert.Equal(expectedUrl, externalLinks.LastUrl);
        Assert.True(viewModel.IsOpen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LinkFailureKeepsDialogOpenAndReportsFriendlyMessage(bool throwException)
    {
        var messages = new RecordingMessageService();
        var externalLinks = new RecordingExternalLinkService
        {
            Result = false,
            ThrowException = throwException
        };
        var viewModel = CreateFeedbackViewModel(messages, externalLinks);
        viewModel.Open();

        viewModel.OpenBugReportsCommand.Execute(null);

        Assert.True(viewModel.IsOpen);
        Assert.Equal(Strings.Status_OpenFeedbackPageFailed, messages.StatusMessage);
        Assert.Equal(Strings.Status_OpenFeedbackPageFailed, messages.FloatingMessage);
    }

    [Fact]
    public void FeedbackMenuOpensDialogWithoutChangingSelectedSettingsSection()
    {
        var messages = new RecordingMessageService();
        var externalLinks = new RecordingExternalLinkService();
        using var viewModel = new SettingsPageViewModel(
            Stub<ISettingsService>(),
            messages,
            Stub<ISystemMemoryService>(),
            Stub<IJavaRuntimeDiscoveryService>(),
            Stub<IFilePickerService>(),
            Stub<IInstanceFolderService>(),
            messages,
            Stub<IThemeService>(),
            externalLinks,
            Stub<ILauncherUpdateService>(),
            Stub<ILauncherSelfUpdateService>(),
            Stub<IApplicationExitService>());
        var selectedSection = viewModel.SelectedSection;
        var currentSection = viewModel.CurrentSectionViewModel;
        var sectionTitle = viewModel.SectionTitle;
        var feedbackItem = Assert.Single(
            viewModel.Sections,
            item => item.Section is SettingsPageSection.Feedback);

        viewModel.SelectSectionCommand.Execute(feedbackItem);

        Assert.True(viewModel.Feedback.IsOpen);
        Assert.Same(selectedSection, viewModel.SelectedSection);
        Assert.Same(currentSection, viewModel.CurrentSectionViewModel);
        Assert.Equal(sectionTitle, viewModel.SectionTitle);
        Assert.False(feedbackItem.IsSelected);

        viewModel.Feedback.CancelCommand.Execute(null);
        viewModel.SelectSectionCommand.Execute(feedbackItem);
        Assert.True(viewModel.Feedback.IsOpen);
    }

    private static SettingsFeedbackDialogViewModel CreateFeedbackViewModel(
        RecordingMessageService? messages = null,
        RecordingExternalLinkService? externalLinks = null)
    {
        messages ??= new RecordingMessageService();
        externalLinks ??= new RecordingExternalLinkService();
        return new SettingsFeedbackDialogViewModel(messages, messages, externalLinks);
    }

    private static T Stub<T>() where T : class
    {
        return DispatchProxy.Create<T, DefaultInterfaceProxy>();
    }

    public class DefaultInterfaceProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var returnType = targetMethod?.ReturnType;
            if (returnType is null || returnType == typeof(void))
                return null;
            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }

    private sealed class RecordingExternalLinkService : IExternalLinkService
    {
        public bool Result { get; set; } = true;
        public bool ThrowException { get; set; }
        public string? LastUrl { get; private set; }

        public bool TryOpen(string url)
        {
            LastUrl = url;
            if (ThrowException)
                throw new InvalidOperationException("Test failure.");
            return Result;
        }
    }

    private sealed class RecordingMessageService : IStatusService, IFloatingMessageService
    {
        public event Action<string>? MessageReported;
        public event Action<string>? MessageRequested;

        public string? StatusMessage { get; private set; }
        public string? FloatingMessage { get; private set; }

        public void Report(string message)
        {
            StatusMessage = message;
            MessageReported?.Invoke(message);
        }

        public void Show(string message)
        {
            FloatingMessage = message;
            MessageRequested?.Invoke(message);
        }
    }
}
