namespace Library.IntegrationTests.Infrastructure;

public sealed record CapturedLogEntry(string Category, LogLevel Level, string Message, IReadOnlyList<KeyValuePair<string, object?>> Properties);

// Captura os eventos de log emitidos pelo host de teste para os testes verificarem que
// identificadores de negócio chegam como propriedades estruturadas, não interpoladas na mensagem.
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public List<CapturedLogEntry> Entries { get; } = [];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string categoryName, List<CapturedLogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (state is IReadOnlyList<KeyValuePair<string, object?>> properties)
            {
                lock (entries)
                {
                    entries.Add(new CapturedLogEntry(categoryName, logLevel, formatter(state, exception), properties));
                }
            }
        }
    }
}
