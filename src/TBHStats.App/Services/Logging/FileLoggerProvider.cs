using Microsoft.Extensions.Logging;

namespace TBHStats.App.Services.Logging;

/// <summary>
/// Провайдер локального файлового логирования для TBHStats.
/// </summary>
/// <remarks>
/// <para>
/// Записывает лог в файл вида <c>%LOCALAPPDATA%\TBHStats\logs\tbhstats-YYYY-MM-DD.log</c>.
/// Ротация происходит автоматически при смене даты UTC — новый файл открывается в следующей записи.
/// </para>
/// <para>
/// Потокобезопасность: все операции I/O синхронизированы через <c>lock (_writeLock)</c>.
/// </para>
/// <para>
/// Внешних сетевых зависимостей нет (FR-012: только локальное хранение).
/// Путь к лог-директории намеренно не логируется через сам логгер, чтобы не выдавать
/// пути с именем пользователя (PII) на уровне Information/Warning.
/// </para>
/// </remarks>
public sealed class FileLoggerProvider : ILoggerProvider
{
    // ── Конфигурация ─────────────────────────────────────────────────────────

    /// <summary>Минимальный уровень событий, попадающих в файл (по умолчанию Information).</summary>
    public LogLevel MinimumLevel { get; }

    // ── Внутреннее состояние ──────────────────────────────────────────────────

    private readonly string _logDirectory;
    private StreamWriter? _writer;
    private string? _currentDateLabel;     // формат "yyyy-MM-dd"
    private readonly object _writeLock = new();
    private bool _disposed;

    // ── Конструктор ───────────────────────────────────────────────────────────

    /// <summary>
    /// Создаёт провайдер. Директория логов создаётся немедленно, если не существует.
    /// </summary>
    /// <param name="logDirectory">
    /// Полный путь к директории логов (например, <c>%LOCALAPPDATA%\TBHStats\logs</c>).
    /// Передаётся из <c>App.xaml.cs</c>; computed от <c>DatabaseInitializer.GetDbPath()</c> базового каталога.
    /// </param>
    /// <param name="minimumLevel">Минимальный уровень для записи в файл.</param>
    public FileLoggerProvider(string logDirectory, LogLevel minimumLevel = LogLevel.Information)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);

        _logDirectory = logDirectory;
        MinimumLevel  = minimumLevel;

        // Гарантировать наличие директории при создании провайдера.
        Directory.CreateDirectory(_logDirectory);
    }

    // ── ILoggerProvider ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName)
        => new FileLogger(categoryName, this);

    // ── Запись ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Форматирует и записывает одну строку лога в файл.
    /// Потокобезопасно; при смене даты открывает новый файл (ротация).
    /// </summary>
    internal void Write(LogLevel logLevel, string categoryName, string message, Exception? exception)
    {
        // Формируем строку лога; имя файла содержит дату UTC.
        string nowUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string dateLabel = nowUtc[..10]; // "yyyy-MM-dd"

        // Короткое имя категории: берём только последний сегмент (после последней точки).
        string shortCategory = GetShortCategory(categoryName);

        string level = logLevel switch
        {
            LogLevel.Trace       => "TRC",
            LogLevel.Debug       => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning     => "WRN",
            LogLevel.Error       => "ERR",
            LogLevel.Critical    => "CRT",
            _                    => "???",
        };

        lock (_writeLock)
        {
            if (_disposed) return;

            // Ротация по дате: если дата изменилась — открыть новый файл.
            EnsureWriter(dateLabel);

            _writer!.WriteLine($"[{nowUtc}] [{level}] [{shortCategory}] {message}");

            if (exception is not null)
                _writer.WriteLine($"  Exception: {exception}");

            _writer.Flush();
        }
    }

    // ── Вспомогательные методы ────────────────────────────────────────────────

    /// <summary>
    /// Гарантирует открытый StreamWriter для текущей даты. При смене даты — переоткрывает файл.
    /// Вызывается под <c>_writeLock</c>.
    /// </summary>
    private void EnsureWriter(string dateLabel)
    {
        if (_writer is not null && _currentDateLabel == dateLabel)
            return;

        // Закрыть старый writer (если был).
        try { _writer?.Close(); } catch { /* не роняем при ротации */ }

        _currentDateLabel = dateLabel;
        string filePath   = Path.Combine(_logDirectory, $"tbhstats-{dateLabel}.log");

        // append: true — продолжаем в тот же файл, если приложение перезапустилось в тот же день.
        _writer = new StreamWriter(filePath, append: true, encoding: System.Text.Encoding.UTF8)
        {
            AutoFlush = false, // Flush вызываем явно после каждой записи
        };
    }

    /// <summary>
    /// Возвращает короткое имя категории — последний сегмент пространства имён.
    /// </summary>
    private static string GetShortCategory(string categoryName)
    {
        int lastDot = categoryName.LastIndexOf('.');
        return lastDot >= 0 ? categoryName[(lastDot + 1)..] : categoryName;
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_writeLock)
        {
            if (_disposed) return;
            _disposed = true;

            try { _writer?.Close(); } catch { /* не роняем при dispose */ }
            _writer = null;
        }
    }
}
