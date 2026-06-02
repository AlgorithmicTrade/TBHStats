namespace TBHStats.App.ViewModels;

/// <summary>
/// Строка таблицы сравнения этапов (экран US2, FR-008/FR-009/FR-019).
/// Immutable: создаётся одним разом в <see cref="CompareViewModel.LoadAsync"/> при каждом обновлении.
/// </summary>
public sealed class CompareStageRow
{
    /// <summary>Идентификатор этапа (<see cref="TBHStats.Core.Models.Stage.Id"/>).</summary>
    public required int StageId { get; init; }

    /// <summary>
    /// Метка этапа в формате «{actNumber}-{stageNumber} {difficultyDisplayName}»,
    /// например «1-5 Nightmare». Строится по join Stage/Act/Difficulty.
    /// Используется в <see cref="TBHStats.App.ViewModels.CompareViewModel.StatusText"/>.
    /// </summary>
    public required string StageLabel { get; init; }

    /// <summary>
    /// Номерная часть метки этапа, без сложности: «{actNumber}-{stageNumber}»,
    /// например «1-5». Отображается в колонке «Этап» таблицы сравнения.
    /// </summary>
    public required string StageNumberLabel { get; init; }

    /// <summary>
    /// Сложность этапа («Normal» / «Nightmare»).
    /// Данные подготовлены в модели, колонка сложности визуально скрыта (требование UI).
    /// </summary>
    public required string DifficultyLabel { get; init; }

    /// <summary>1-based место в ранжированном списке (1 — лучший).</summary>
    public required int Rank { get; init; }

    /// <summary>true, если этот этап является рекомендованным.</summary>
    public required bool IsRecommended { get; init; }

    /// <summary>Золото/час по выбранному scope (числовое значение для сортировки).</summary>
    public required double GoldPerHour { get; init; }

    /// <summary>Опыт/час по выбранному scope (числовое значение для сортировки).</summary>
    public required double XpPerHour { get; init; }

    /// <summary>Золото/час, отформатированное для отображения («1 234 567/ч»).</summary>
    public required string GoldPerHourText { get; init; }

    /// <summary>Опыт/час, отформатированное для отображения («1 234 567/ч»).</summary>
    public required string XpPerHourText { get; init; }

    /// <summary>Среднее абсолютное золото за забег по выбранному scope (числовое значение для сортировки).</summary>
    public required double AvgGoldGained { get; init; }

    /// <summary>Средний абсолютный опыт за забег по выбранному scope (числовое значение для сортировки).</summary>
    public required double AvgXpGained { get; init; }

    /// <summary>Среднее золото за забег, отформатированное для отображения («12 345»).</summary>
    public required string AvgGoldText { get; init; }

    /// <summary>Средний опыт за забег, отформатированный для отображения («12 345»).</summary>
    public required string AvgXpText { get; init; }

    /// <summary>Число забегов в выбранном scope.</summary>
    public required int RunCount { get; init; }

    /// <summary>
    /// Диапазон силы отряда для строки — формат «ур. {min}–{max}, урон {minFmt}–{maxFmt}»
    /// или «—», если данные недоступны (пустое окно).
    /// </summary>
    public required string PowerText { get; init; }

    /// <summary>
    /// true, если данные строки собраны при существенно меньшей силе отряда, чем текущая.
    /// Логика эвристики — см. <see cref="CompareViewModel.ComputeIsStalePower"/>.
    /// </summary>
    public required bool IsStalePower { get; init; }

    /// <summary>
    /// Текстовое пояснение ранга из <see cref="TBHStats.Core.Optimization.StageRanking.Reason"/>,
    /// например «лучший по золото/час, свежее окно».
    /// </summary>
    public required string Reason { get; init; }
}
