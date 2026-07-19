using Launcher.App.Resources;
using Launcher.App.ViewModels.Multiplayer;

namespace Launcher.Tests.ViewModels.Multiplayer;

public sealed class MultiplayerPageViewModelTests
{
    [Fact]
    public void Constructor_SelectsCreateLobby()
    {
        var viewModel = new MultiplayerPageViewModel();

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
    }

    [Fact]
    public void SelectSectionCommand_SelectsJoinLobby()
    {
        var viewModel = new MultiplayerPageViewModel();
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
        var viewModel = new MultiplayerPageViewModel();

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
}
