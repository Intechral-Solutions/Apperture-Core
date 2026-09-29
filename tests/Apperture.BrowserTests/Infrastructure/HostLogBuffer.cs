using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Apperture.BrowserTests.Infrastructure;

/// <summary>
/// Keeps the hosted Management.Host's warnings and errors (with exceptions) in memory so a failing run can
/// print WHY the host misbehaved. Log text can contain user-supplied route data but the host never logs
/// cookie or token values; nothing here is written to disk.
/// </summary>
public sealed class HostLogBuffer : ILoggerProvider
{
    private const int Capacity = 200;
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyList<string> Lines => _lines.ToArray();

    public string Dump(int max = 12) =>
        string.Join(Environment.NewLine + "---" + Environment.NewLine, _lines.TakeLast(max));

    public ILogger CreateLogger(string categoryName) => new BufferLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Add(string line)
    {
        _lines.Enqueue(line);
        while (_lines.Count > Capacity)
            _lines.TryDequeue(out _);
    }

    private sealed class BufferLogger(HostLogBuffer buffer, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var text = formatter(state, exception);
            buffer.Add($"[{logLevel}] {category}: {text}{(exception is null ? "" : Environment.NewLine + exception)}");
        }
    }
}
