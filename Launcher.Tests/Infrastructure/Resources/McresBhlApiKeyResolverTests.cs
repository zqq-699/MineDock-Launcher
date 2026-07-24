/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Infrastructure;
using Launcher.Infrastructure.Resources;
using Microsoft.Extensions.Logging;

namespace Launcher.Tests.Infrastructure.Resources;

public sealed class McresBhlApiKeyResolverTests : TestTempDirectory
{
    [Fact]
    public async Task EmbeddedKeyOverridesLocalAndEnvironmentKeysWithoutLoggingValue()
    {
        await WriteLocalSecretAsync("local-test-key");
        var logger = new CollectingLogger<McresBhlApiKeyResolver>();
        var resolver = CreateResolver(
            logger,
            environmentApiKeyProvider: () => "environment-test-key",
            embeddedApiKeyProvider: _ => Task.FromResult<string?>("embedded-test-key"));

        var apiKey = await resolver.TryResolveAsync();

        Assert.Equal("embedded-test-key", apiKey);
        Assert.DoesNotContain(logger.Messages, message =>
            message.Contains("embedded-test-key", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LocalSecretOverridesEnvironmentKey()
    {
        await WriteLocalSecretAsync("local-test-key");
        var resolver = CreateResolver(
            environmentApiKeyProvider: () => "environment-test-key",
            embeddedApiKeyProvider: _ => Task.FromResult<string?>(null));

        var apiKey = await resolver.TryResolveAsync();

        Assert.Equal("local-test-key", apiKey);
    }

    [Fact]
    public async Task EnvironmentKeyIsUsedAsFinalFallback()
    {
        var resolver = CreateResolver(
            environmentApiKeyProvider: () => "environment-test-key",
            embeddedApiKeyProvider: _ => Task.FromResult<string?>(null));

        var apiKey = await resolver.TryResolveAsync();

        Assert.Equal("environment-test-key", apiKey);
    }

    [Fact]
    public async Task MissingKeyReturnsNull()
    {
        var resolver = CreateResolver(
            environmentApiKeyProvider: () => null,
            embeddedApiKeyProvider: _ => Task.FromResult<string?>(null));

        Assert.Null(await resolver.TryResolveAsync());
    }

    private McresBhlApiKeyResolver CreateResolver(
        ILogger<McresBhlApiKeyResolver>? logger = null,
        Func<string?>? environmentApiKeyProvider = null,
        Func<CancellationToken, Task<string?>>? embeddedApiKeyProvider = null) =>
        new(
            new LauncherPathProvider(Path.Combine(TempRoot, "data")),
            logger: logger,
            currentDirectoryProvider: () => TempRoot,
            userProfileDirectoryProvider: () => Path.Combine(TempRoot, "profile"),
            environmentApiKeyProvider: environmentApiKeyProvider,
            embeddedApiKeyProvider: embeddedApiKeyProvider);

    private async Task WriteLocalSecretAsync(string value)
    {
        var directory = Path.Combine(TempRoot, ".local-secrets");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "mcres-bhl.key"), value);
    }

    private sealed class CollectingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
