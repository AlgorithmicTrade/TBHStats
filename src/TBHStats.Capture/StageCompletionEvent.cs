namespace TBHStats.Capture;

/// <summary>
/// Событие завершения этапа, генерируемое <see cref="IStageCompletionDetector"/>
/// при детектировании гибели босса (FR-002, data-model §Детекция завершения этапа).
/// </summary>
/// <param name="CompletedAtUtc">
/// Момент завершения этапа (UTC), взятый из <c>RawObservation.TakenAtUtc</c>
/// кадра, в котором зафиксирован сигнал.
/// </param>
/// <param name="StageTimeSeconds">
/// Время прохождения этапа в секундах из <c>RawObservation.StageTimeSeconds</c>.
/// Null, если поле не считано в кадре-триггере (данные недоступны в этом кадре).
/// </param>
/// <param name="StageProgressAtCompletion">
/// Значение прогрессбара на момент завершения (≈ 1.0 или null, если не считано).
/// Сохраняется для диагностики и тестовых фикстур (T049).
/// </param>
public readonly record struct StageCompletionEvent(
    DateTime CompletedAtUtc,
    int? StageTimeSeconds,
    double? StageProgressAtCompletion);
