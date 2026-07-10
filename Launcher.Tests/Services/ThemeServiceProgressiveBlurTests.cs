using System.Windows;
using Launcher.App.Effects;
using Launcher.App.Services;
using Launcher.Tests.Helpers;
using Microsoft.Extensions.Logging;
using WpfApplication = System.Windows.Application;

namespace Launcher.Tests.Services;

[Collection(WpfTestCollection.Name)]
public sealed class ThemeServiceProgressiveBlurTests
{
    [Fact]
    public void ProgressiveBlurResourceTracksPreferenceAndRuntimeAvailability()
    {
        StaTest.Run(() =>
        {
            WpfApplicationTestHelper.GetOrCreateApplication();
            var support = new FakeProgressiveBlurSupport(AvailableSnapshot());
            using var service = new ThemeService(ImmediateUiDispatcher.Instance, null, support);

            try
            {
                service.ApplyBackgroundBlurDisabled(false);
                Assert.True(ReadEnabledResource());
                Assert.False(service.BackgroundBlurDisabled);

                service.ApplyBackgroundBlurDisabled(true);
                Assert.False(ReadEnabledResource());
                Assert.True(service.BackgroundBlurDisabled);

                service.ApplyBackgroundBlurDisabled(false);
                support.Set(UnavailableSnapshot(ProgressiveBlurUnavailableReason.RenderingTierTooLow));
                Assert.False(ReadEnabledResource());
                Assert.False(service.BackgroundBlurDisabled);

                support.Set(AvailableSnapshot());
                Assert.True(ReadEnabledResource());
            }
            finally
            {
                WpfApplicationTestHelper.ShutdownAndResetCurrentApplication();
            }
        });
    }

    [Fact]
    public void ShaderInitializationFailureIsLoggedOnlyOnce()
    {
        StaTest.Run(() =>
        {
            WpfApplicationTestHelper.GetOrCreateApplication();
            var initializationException = new InvalidOperationException("shader-test-failure");
            var failedSnapshot = new ProgressiveBlurCapabilitySnapshot(
                false,
                2,
                true,
                ProgressiveBlurUnavailableReason.ShaderLoadFailed,
                initializationException);
            var support = new FakeProgressiveBlurSupport(failedSnapshot);
            var logger = new CollectingLogger<ThemeService>();
            using var service = new ThemeService(ImmediateUiDispatcher.Instance, logger, support);

            try
            {
                service.ApplyBackgroundBlurDisabled(false);
                service.ApplyBackgroundBlurDisabled(false);
                support.NotifyWithoutChangingState();

                Assert.Single(logger.Entries.Where(entry =>
                    entry.Level is LogLevel.Warning
                    && entry.Message.Contains("shader initialization failed", StringComparison.OrdinalIgnoreCase)));
            }
            finally
            {
                WpfApplicationTestHelper.ShutdownAndResetCurrentApplication();
            }
        });
    }

    private static bool ReadEnabledResource()
    {
        return Assert.IsType<bool>(WpfApplication.Current.Resources[ProgressiveBlurResourceKeys.IsEnabled]);
    }

    private static ProgressiveBlurCapabilitySnapshot AvailableSnapshot()
    {
        return new ProgressiveBlurCapabilitySnapshot(
            true,
            2,
            true,
            ProgressiveBlurUnavailableReason.None,
            null);
    }

    private static ProgressiveBlurCapabilitySnapshot UnavailableSnapshot(ProgressiveBlurUnavailableReason reason)
    {
        return new ProgressiveBlurCapabilitySnapshot(false, 0, false, reason, null);
    }

    private sealed class FakeProgressiveBlurSupport(ProgressiveBlurCapabilitySnapshot current) : IProgressiveBlurSupport
    {
        public ProgressiveBlurCapabilitySnapshot Current { get; private set; } = current;

        public event EventHandler? AvailabilityChanged;

        public void Set(ProgressiveBlurCapabilitySnapshot next)
        {
            Current = next;
            AvailabilityChanged?.Invoke(this, EventArgs.Empty);
        }

        public void NotifyWithoutChangingState()
        {
            AvailabilityChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
        }
    }

    private sealed class CollectingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
