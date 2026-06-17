namespace Launcher.Application.Services;

public sealed class DuplicateGameInstanceNameException : Exception
{
    public DuplicateGameInstanceNameException(string instanceName)
        : base($"A game instance named '{instanceName}' already exists.")
    {
        InstanceName = instanceName;
    }

    public string InstanceName { get; }
}
