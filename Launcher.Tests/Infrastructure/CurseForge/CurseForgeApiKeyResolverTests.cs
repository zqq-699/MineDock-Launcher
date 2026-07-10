/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure;
using Launcher.Infrastructure.CurseForge;
using Microsoft.Extensions.Logging;

namespace Launcher.Tests.Infrastructure.CurseForge;

public sealed class CurseForgeApiKeyResolverTests
{
    private static readonly SemaphoreSlim EnvironmentGate = new(1, 1);

    [Fact]
    public async Task TryResolveAsyncUsesEmbeddedResourceBeforeLocalSecretFileAndEnvironmentVariable()
    {
        await EnvironmentGate.WaitAsync();
        var tempRoot = CreateTempDirectory();
        var previousValue = Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", "environment-secret");
            await WriteSecretAsync(tempRoot, "current-directory-secret");
            var logger = new CollectingLogger<CurseForgeApiKeyResolver>();
            var resolver = new CurseForgeApiKeyResolver(
                new LauncherPathProvider(Path.Combine(tempRoot, "appdata")),
                logger: logger,
                currentDirectoryProvider: () => tempRoot,
                userProfileDirectoryProvider: () => Path.Combine(tempRoot, "profile"),
                environmentApiKeyProvider: () => "environment-secret",
                embeddedApiKeyProvider: _ => Task.FromResult<string?>("embedded-secret"));

            var apiKey = await resolver.TryResolveAsync();

            Assert.Equal("embedded-secret", apiKey);
            Assert.DoesNotContain(logger.Messages, message => message.Contains("embedded-secret", StringComparison.Ordinal));
            Assert.Single(logger.Messages.Where(message => message.Contains("embedded resource", StringComparison.Ordinal)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", previousValue);
            TryDeleteDirectory(tempRoot);
            EnvironmentGate.Release();
        }
    }

    [Fact]
    public async Task TryResolveAsyncFallsBackToLocalSecretFileWhenEmbeddedResourceIsMissing()
    {
        await EnvironmentGate.WaitAsync();
        var tempRoot = CreateTempDirectory();
        var previousValue = Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", "environment-secret");
            await WriteSecretAsync(tempRoot, "current-directory-secret");

            var resolver = CreateResolver(tempRoot, environmentApiKeyProvider: () => "environment-secret");

            var apiKey = await resolver.TryResolveAsync();

            Assert.Equal("current-directory-secret", apiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", previousValue);
            TryDeleteDirectory(tempRoot);
            EnvironmentGate.Release();
        }
    }

    [Fact]
    public async Task TryResolveAsyncUsesCurrentDirectoryLocalSecretFile()
    {
        await EnvironmentGate.WaitAsync();
        var tempRoot = CreateTempDirectory();
        var previousValue = Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", null);
            await WriteSecretAsync(tempRoot, "current-directory-secret");

            var resolver = CreateResolver(tempRoot, environmentApiKeyProvider: () => null);

            var apiKey = await resolver.TryResolveAsync();

            Assert.Equal("current-directory-secret", apiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", previousValue);
            TryDeleteDirectory(tempRoot);
            EnvironmentGate.Release();
        }
    }

    [Fact]
    public async Task TryResolveAsyncIgnoresEmptyFileAndFallsBackToEnvironmentVariable()
    {
        await EnvironmentGate.WaitAsync();
        var tempRoot = CreateTempDirectory();
        var previousValue = Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", "environment-secret");
            await WriteSecretAsync(tempRoot, "   ");

            var resolver = CreateResolver(tempRoot, environmentApiKeyProvider: () => "environment-secret");

            var apiKey = await resolver.TryResolveAsync();

            Assert.Equal("environment-secret", apiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", previousValue);
            TryDeleteDirectory(tempRoot);
            EnvironmentGate.Release();
        }
    }

    [Fact]
    public async Task TryResolveAsyncDoesNotUseLegacySecretsDirectory()
    {
        await EnvironmentGate.WaitAsync();
        var tempRoot = CreateTempDirectory();
        var previousValue = Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", null);
            var legacySecretsDirectory = Path.Combine(tempRoot, ".secrets");
            Directory.CreateDirectory(legacySecretsDirectory);
            await File.WriteAllTextAsync(Path.Combine(legacySecretsDirectory, "curseforge.key"), "legacy-secret");

            var resolver = CreateResolver(tempRoot, environmentApiKeyProvider: () => null);

            var apiKey = await resolver.TryResolveAsync();

            Assert.Null(apiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", previousValue);
            TryDeleteDirectory(tempRoot);
            EnvironmentGate.Release();
        }
    }

    [Fact]
    public async Task TryResolveAsyncDeduplicatesDirectoriesAndDoesNotLogSecretValue()
    {
        await EnvironmentGate.WaitAsync();
        var tempRoot = CreateTempDirectory();
        var previousValue = Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", null);
            var pathProvider = new LauncherPathProvider(tempRoot);
            var dataDirectory = pathProvider.DefaultDataDirectory;
            Directory.CreateDirectory(dataDirectory);
            await WriteSecretAsync(dataDirectory, "super-secret-key");
            var logger = new CollectingLogger<CurseForgeApiKeyResolver>();
            var settingsService = new StubSettingsService(dataDirectory);
            var resolver = new CurseForgeApiKeyResolver(
                pathProvider,
                settingsService,
                logger,
                currentDirectoryProvider: () => dataDirectory,
                userProfileDirectoryProvider: () => Path.Combine(tempRoot, "profile"),
                embeddedApiKeyProvider: _ => Task.FromResult<string?>(null));

            var apiKey = await resolver.TryResolveAsync();

            Assert.Equal("super-secret-key", apiKey);
            Assert.DoesNotContain(logger.Messages, message => message.Contains("super-secret-key", StringComparison.Ordinal));
            Assert.Single(logger.Messages.Where(message => message.Contains("Resolved CurseForge API key", StringComparison.Ordinal)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", previousValue);
            TryDeleteDirectory(tempRoot);
            EnvironmentGate.Release();
        }
    }

    private static async Task WriteSecretAsync(string root, string apiKey)
    {
        var secretsDirectory = Path.Combine(root, ".local-secrets");
        Directory.CreateDirectory(secretsDirectory);
        await File.WriteAllTextAsync(Path.Combine(secretsDirectory, "curseforge.key"), apiKey);
    }

    private static CurseForgeApiKeyResolver CreateResolver(string tempRoot, Func<string?>? environmentApiKeyProvider = null)
    {
        return new CurseForgeApiKeyResolver(
            new LauncherPathProvider(Path.Combine(tempRoot, "appdata")),
            currentDirectoryProvider: () => tempRoot,
            userProfileDirectoryProvider: () => Path.Combine(tempRoot, "profile"),
            environmentApiKeyProvider: environmentApiKeyProvider ?? (() => Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY")),
            embeddedApiKeyProvider: _ => Task.FromResult<string?>(null));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "launcher-curseforge-key-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class StubSettingsService(string dataDirectory) : ISettingsService
    {
        public Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LauncherSettings { DataDirectory = dataDirectory });
        }

        public Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class CollectingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
