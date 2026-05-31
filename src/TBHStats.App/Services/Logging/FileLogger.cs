using Microsoft.Extensions.Logging;

namespace TBHStats.App.Services.Logging;

/// <summary>
/// Реализация <see cref="ILogger"/> с записью в локальный файл.
/// Создаётся <see cref="FileLoggerProvider"/>; один экземпляр на категорию.
/// Делегирует форматирование и запись обратно в провайдер (единый StreamWriter).
/// </summary>
internal sealed class FileLogger : ILogger
{
    private readonly string _categoryName;
    private readonly FileLoggerProvider _provider;

    internal FileLogger(string categoryName, FileLoggerProvider provider)
    {
        _categoryName = categoryName;
        _provider     = provider;
    }

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => logLevel >= _provider.MinimumLevel;

    /// <inheritdoc/>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        ArgumentNullException.ThrowIfNull(formatter);

        string message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception is null)
            return;

        _provider.Write(logLevel, _categoryName, message, exception);
    }
}
