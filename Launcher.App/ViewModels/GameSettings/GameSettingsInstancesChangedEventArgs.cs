using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.GameSettings;

public sealed class GameSettingsInstancesChangedEventArgs : EventArgs
{
    private GameSettingsInstancesChangedEventArgs(
        GameSettingsInstancesChangedKind kind,
        GameInstance? updatedInstance,
        string? deletedInstanceId)
    {
        Kind = kind;
        UpdatedInstance = updatedInstance;
        DeletedInstanceId = deletedInstanceId;
    }

    public GameSettingsInstancesChangedKind Kind { get; }

    public GameInstance? UpdatedInstance { get; }

    public string? DeletedInstanceId { get; }

    public static GameSettingsInstancesChangedEventArgs Updated(GameInstance instance)
    {
        return new GameSettingsInstancesChangedEventArgs(
            GameSettingsInstancesChangedKind.Updated,
            instance,
            null);
    }

    public static GameSettingsInstancesChangedEventArgs Deleted(string instanceId)
    {
        return new GameSettingsInstancesChangedEventArgs(
            GameSettingsInstancesChangedKind.Deleted,
            null,
            instanceId);
    }

    public static GameSettingsInstancesChangedEventArgs ReloadRequired()
    {
        return new GameSettingsInstancesChangedEventArgs(
            GameSettingsInstancesChangedKind.ReloadRequired,
            null,
            null);
    }
}
