using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public sealed class NoCompatibleModFileException : Exception
{
    public NoCompatibleModFileException(string projectId, string minecraftVersion, LoaderKind loader)
        : base($"No compatible mod file was found for project '{projectId}' on Minecraft {minecraftVersion} with loader {loader}.")
    {
        ProjectId = projectId;
        MinecraftVersion = minecraftVersion;
        Loader = loader;
    }

    public string ProjectId { get; }
    public string MinecraftVersion { get; }
    public LoaderKind Loader { get; }
}
