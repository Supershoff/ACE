using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

namespace ACE.Cloud.AuthBridge.Tests;

/// <summary>Captures every formatted log message emitted during a test run, so redaction tests can assert on the exact text that would have reached a real log sink.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<string> Messages { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly CapturingLoggerProvider _provider;

        public CapturingLogger(CapturingLoggerProvider provider)
        {
            _provider = provider;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _provider.Messages.Enqueue(formatter(state, exception) + (exception?.ToString() ?? string.Empty));
        }
    }
}
