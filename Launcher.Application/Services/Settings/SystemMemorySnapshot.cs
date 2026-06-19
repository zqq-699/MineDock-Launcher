namespace Launcher.Application.Services;

public sealed record SystemMemorySnapshot(long TotalMemoryBytes, long AvailableMemoryBytes);
