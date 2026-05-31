namespace TBHStats.Core.Optimization;

/// <summary>
/// Результат ранжирования одного этапа по выбранной метрике и scope (FR-009, FR-019).
/// </summary>
/// <param name="StageId">Идентификатор этапа (<see cref="TBHStats.Core.Models.Stage.Id"/>).</param>
/// <param name="Score">
/// Значение выбранной метрики в выбранном scope
/// (например, среднее золото/час по свежему окну при GoldPerHour + Recent).
/// </param>
/// <param name="Rank">1-based место в ранжированном списке (1 — лучший).</param>
/// <param name="Reason">
/// Текстовое пояснение ранга («лучший по золото/час, свежее окно»).
/// Формируется сервисом оптимизации для отображения в UI.
/// </param>
/// <param name="Power">
/// Контекст силы забегов свежего окна.
/// Позволяет UI пометить рекомендации, собранные при существенно другой силе отряда.
/// </param>
public sealed record StageRanking(
    int StageId,
    double Score,
    int Rank,
    string Reason,
    StagePowerContext Power);
