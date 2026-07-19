using Launcher.App.Resources;
using Launcher.App.ViewModels.Multiplayer;
using Launcher.Application.Services;

namespace Launcher.Tests.ViewModels.Multiplayer;

public sealed class MultiplayerPageViewModelTests
{
    [Fact]
    public void Constructor_SelectsCreateLobby()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(2, viewModel.Sections.Count);
        Assert.Equal(MultiplayerPageSection.CreateLobby, viewModel.SelectedSection?.Section);
        Assert.Equal(Strings.Multiplayer_SectionCreateLobby, viewModel.SectionTitle);
        Assert.Equal(MultiplayerCreateLobbyStep.Setup, viewModel.CreateLobbyStep);
        Assert.Equal(3, viewModel.LobbyPlayers.Count);
        Assert.Equal(
            string.Format(Strings.Multiplayer_LobbyPlayerPlaceholderFormat, 1),
            viewModel.LobbyPlayers[0].DisplayName);
        Assert.Equal(
            string.Format(Strings.Multiplayer_LobbyClientIdPlaceholderFormat, 1),
            viewModel.LobbyPlayers[0].ClientId);
        Assert.Equal(Strings.Multiplayer_LobbyPlayerRoleHost, viewModel.LobbyPlayers[0].Role);
        Assert.True(viewModel.LobbyPlayers[0].IsHost);
        Assert.Equal(Strings.Multiplayer_LobbyPlayerRolePlayer, viewModel.LobbyPlayers[1].Role);
        Assert.False(viewModel.LobbyPlayers[1].IsHost);
        Assert.True(viewModel.IsCreateLobbySection);
        Assert.True(viewModel.Sections[0].IsSelected);
        Assert.False(viewModel.Sections[1].IsSelected);
        Assert.Empty(viewModel.LanWorlds);
        Assert.Equal(Strings.Multiplayer_Create_LanWorldRefreshHint, viewModel.LanWorldDiscoveryStatus);
        Assert.False(viewModel.CreateLobbyCommand.CanExecute(null));
    }

    [Fact]
    public void SelectSectionCommand_SelectsJoinLobby()
    {
        var viewModel = CreateViewModel();
        var joinLobby = viewModel.Sections[1];

        viewModel.SelectSectionCommand.Execute(joinLobby);

        Assert.Same(joinLobby, viewModel.SelectedSection);
        Assert.Equal(Strings.Multiplayer_SectionJoinLobby, viewModel.SectionTitle);
        Assert.False(viewModel.IsCreateLobbySection);
        Assert.False(viewModel.Sections[0].IsSelected);
        Assert.True(joinLobby.IsSelected);
    }

    [Fact]
    public void CreateLobbyCommand_OpensLobbyStep()
    {
        var viewModel = CreateViewModel();
        AddSelectedWorld(viewModel, "Test World", "127.0.0.1", 51234);

        viewModel.CreateLobbyCommand.Execute(null);

        Assert.Equal(MultiplayerCreateLobbyStep.Lobby, viewModel.CreateLobbyStep);
        Assert.True(viewModel.IsLobbyStep);
        Assert.Equal(
            string.Format(Strings.Multiplayer_LobbyTitleFormat, Strings.Multiplayer_LobbyOwnerPlaceholder),
            viewModel.LobbyTitle);
        Assert.Equal(viewModel.LobbyTitle, viewModel.SectionTitle);

        viewModel.RequestLeaveLobbyCommand.Execute(null);

        Assert.True(viewModel.IsLeaveLobbyDialogOpen);
        Assert.Equal(MultiplayerCreateLobbyStep.Lobby, viewModel.CreateLobbyStep);

        viewModel.CancelLeaveLobbyCommand.Execute(null);

        Assert.False(viewModel.IsLeaveLobbyDialogOpen);
        Assert.Equal(MultiplayerCreateLobbyStep.Lobby, viewModel.CreateLobbyStep);

        viewModel.RequestLeaveLobbyCommand.Execute(null);
        viewModel.ConfirmLeaveLobbyCommand.Execute(null);

        Assert.Equal(MultiplayerCreateLobbyStep.Setup, viewModel.CreateLobbyStep);
        Assert.False(viewModel.IsLobbyStep);
        Assert.False(viewModel.IsLeaveLobbyDialogOpen);
        Assert.Equal(Strings.Multiplayer_SectionCreateLobby, viewModel.SectionTitle);
    }

    [Fact]
    public async Task RefreshLanWorldsCommand_PopulatesWorldsAndRequiresSelection()
    {
        var service = new FakeLanWorldDiscoveryService(
            [new MinecraftLanWorld("Test World", "192.168.1.5", 52345)]);
        var viewModel = new MultiplayerPageViewModel(service);

        await viewModel.RefreshLanWorldsCommand.ExecuteAsync(null);

        var item = Assert.Single(viewModel.LanWorlds);
        Assert.Contains("Test World", item.DisplayText, StringComparison.Ordinal);
        Assert.Contains("52345", item.DisplayText, StringComparison.Ordinal);
        Assert.Equal(item.DisplayText, item.ToString());
        Assert.Null(viewModel.SelectedLanWorld);
        Assert.Equal(string.Empty, viewModel.LanWorldDiscoveryStatus);
        Assert.False(viewModel.CreateLobbyCommand.CanExecute(null));

        viewModel.SelectedLanWorld = item;

        Assert.True(viewModel.CreateLobbyCommand.CanExecute(null));
    }

    [Fact]
    public async Task RefreshLanWorldsCommand_PreservesSelectionByPort()
    {
        var service = new FakeLanWorldDiscoveryService(
            [new MinecraftLanWorld("First Name", "192.168.1.5", 52345)]);
        var viewModel = new MultiplayerPageViewModel(service);
        await viewModel.RefreshLanWorldsCommand.ExecuteAsync(null);
        viewModel.SelectedLanWorld = Assert.Single(viewModel.LanWorlds);
        service.Worlds = [new MinecraftLanWorld("Updated Name", "10.0.0.5", 52345)];

        await viewModel.RefreshLanWorldsCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.SelectedLanWorld);
        Assert.Equal("Updated Name", viewModel.SelectedLanWorld.World.Name);
        Assert.Equal(52345, viewModel.SelectedLanWorld.World.Port);
    }

    [Fact]
    public async Task RefreshLanWorldsCommand_EmptyResultClearsSelectionAndShowsHint()
    {
        var service = new FakeLanWorldDiscoveryService(
            [new MinecraftLanWorld("Test World", "127.0.0.1", 51234)]);
        var viewModel = new MultiplayerPageViewModel(service);
        await viewModel.RefreshLanWorldsCommand.ExecuteAsync(null);
        viewModel.SelectedLanWorld = Assert.Single(viewModel.LanWorlds);
        service.Worlds = [];

        await viewModel.RefreshLanWorldsCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.LanWorlds);
        Assert.Null(viewModel.SelectedLanWorld);
        Assert.Equal(Strings.Multiplayer_Create_LanWorldNotFound, viewModel.LanWorldDiscoveryStatus);
        Assert.False(viewModel.CreateLobbyCommand.CanExecute(null));
    }

    [Fact]
    public async Task RefreshLanWorldsCommand_FailurePreservesPreviousResult()
    {
        var service = new FakeLanWorldDiscoveryService(
            [new MinecraftLanWorld("Test World", "127.0.0.1", 51234)]);
        var viewModel = new MultiplayerPageViewModel(service);
        await viewModel.RefreshLanWorldsCommand.ExecuteAsync(null);
        var selected = Assert.Single(viewModel.LanWorlds);
        viewModel.SelectedLanWorld = selected;
        service.Exception = new IOException("socket failed");

        await viewModel.RefreshLanWorldsCommand.ExecuteAsync(null);

        Assert.Same(selected, Assert.Single(viewModel.LanWorlds));
        Assert.Same(selected, viewModel.SelectedLanWorld);
        Assert.Equal(Strings.Multiplayer_Create_LanWorldDiscoveryFailed, viewModel.LanWorldDiscoveryStatus);
        Assert.True(viewModel.CreateLobbyCommand.CanExecute(null));
    }

    [Fact]
    public async Task RefreshLanWorldsCommand_DisablesCommandsWhileRunning()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<MinecraftLanWorld>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeLanWorldDiscoveryService([])
        {
            PendingResult = completion.Task
        };
        var viewModel = new MultiplayerPageViewModel(service);
        AddSelectedWorld(viewModel, "Test World", "127.0.0.1", 51234);

        var refreshTask = viewModel.RefreshLanWorldsCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsRefreshingLanWorlds);
        Assert.False(viewModel.RefreshLanWorldsCommand.CanExecute(null));
        Assert.False(viewModel.CreateLobbyCommand.CanExecute(null));
        Assert.False(viewModel.CanSelectLanWorld);
        Assert.Equal(Strings.Multiplayer_Create_LanWorldDiscovering, viewModel.LanWorldDiscoveryStatus);

        completion.SetResult([]);
        await refreshTask;

        Assert.False(viewModel.IsRefreshingLanWorlds);
        Assert.True(viewModel.RefreshLanWorldsCommand.CanExecute(null));
    }

    [Fact]
    public async Task RefreshLanWorldsCommand_PublishesWorldBeforeDiscoveryCompletes()
    {
        var world = new MinecraftLanWorld("Streaming World", "127.0.0.1", 51234);
        var completion = new TaskCompletionSource<IReadOnlyList<MinecraftLanWorld>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeLanWorldDiscoveryService([])
        {
            ProgressWorlds = [world],
            PendingResult = completion.Task
        };
        var viewModel = new MultiplayerPageViewModel(service);

        var refreshTask = viewModel.RefreshLanWorldsCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => viewModel.LanWorlds.Count == 1);

        Assert.True(viewModel.IsRefreshingLanWorlds);
        Assert.Equal("Streaming World", Assert.Single(viewModel.LanWorlds).World.Name);
        Assert.Equal(Strings.Multiplayer_Create_LanWorldDiscovering, viewModel.LanWorldDiscoveryStatus);

        completion.SetResult([world]);
        await refreshTask;

        Assert.False(viewModel.IsRefreshingLanWorlds);
        Assert.Equal(string.Empty, viewModel.LanWorldDiscoveryStatus);
    }

    private static MultiplayerPageViewModel CreateViewModel()
    {
        return new MultiplayerPageViewModel(new FakeLanWorldDiscoveryService([]));
    }

    private static void AddSelectedWorld(
        MultiplayerPageViewModel viewModel,
        string name,
        string hostAddress,
        int port)
    {
        var world = new MinecraftLanWorld(name, hostAddress, port);
        var item = new MultiplayerLanWorldItem(world, $"{name} · {port}");
        viewModel.LanWorlds.Add(item);
        viewModel.SelectedLanWorld = item;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class FakeLanWorldDiscoveryService(IReadOnlyList<MinecraftLanWorld> worlds)
        : IMinecraftLanWorldDiscoveryService
    {
        public IReadOnlyList<MinecraftLanWorld> Worlds { get; set; } = worlds;

        public Exception? Exception { get; set; }

        public IReadOnlyList<MinecraftLanWorld> ProgressWorlds { get; set; } = [];

        public Task<IReadOnlyList<MinecraftLanWorld>>? PendingResult { get; set; }

        public Task<IReadOnlyList<MinecraftLanWorld>> DiscoverLocalWorldsAsync(
            CancellationToken cancellationToken = default,
            IProgress<MinecraftLanWorld>? progress = null)
        {
            if (Exception is not null)
                return Task.FromException<IReadOnlyList<MinecraftLanWorld>>(Exception);

            foreach (var world in ProgressWorlds)
                progress?.Report(world);

            return PendingResult ?? Task.FromResult(Worlds);
        }
    }
}
