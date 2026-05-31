namespace TBHStats.App.Services;

/// <summary>
/// Фоновый оркестратор живой статистики TBHStats (FR-004).
/// Управляет петлёй захвата: окно → кадр → детекция вкладки → извлечение полей →
/// валидация → сэмпл → темпы → публикация снимка.
/// </summary>
/// <remarks>
/// <para>
/// Сервис является singleton'ом; потребители (ViewModel) подписываются на
/// <see cref="SnapshotUpdated"/> или читают <see cref="Current"/> напрямую.
/// ViewModel сам маршалит изменения в UI-поток (DispatcherQueue) — сервис не имеет
/// зависимости от UI-диспетчера.
/// </para>
/// <para>
/// Жизненный цикл: вызвать <see cref="StartAsync"/> после инициализации DI-контейнера,
/// <see cref="StopAsync"/> при завершении приложения.
/// </para>
/// </remarks>
public interface IStatsOrchestrator
{
    /// <summary>
    /// Последний опубликованный снимок статистики.
    /// До первой итерации петли — <see cref="LiveStatsSnapshot.Empty"/>.
    /// Потокобезопасен для чтения.
    /// </summary>
    LiveStatsSnapshot Current { get; }

    /// <summary>
    /// Событие, поднимаемое при публикации каждого нового снимка.
    /// Вызывается из фонового потока петли; подписчики обязаны маршалировать в UI-поток самостоятельно.
    /// </summary>
    event EventHandler<LiveStatsSnapshot>? SnapshotUpdated;

    /// <summary>
    /// Запустить фоновую петлю захвата и обработки данных.
    /// Возвращает управление сразу после запуска петли (не блокирует до её завершения).
    /// </summary>
    /// <param name="ct">Токен отмены: при отмене петля завершается gracefully.</param>
    Task StartAsync(CancellationToken ct);

    /// <summary>
    /// Остановить фоновую петлю и дождаться её завершения.
    /// Безопасно вызывать, даже если петля не была запущена.
    /// </summary>
    Task StopAsync();
}
