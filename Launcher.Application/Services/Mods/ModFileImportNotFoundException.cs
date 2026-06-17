using System.IO;

namespace Launcher.Application.Services;

public sealed class ModFileImportNotFoundException : FileNotFoundException
{
    public ModFileImportNotFoundException(string sourcePath)
        : base($"Mod source file was not found: {sourcePath}", sourcePath)
    {
    }
}
