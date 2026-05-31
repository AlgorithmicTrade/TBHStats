using TBHStats.Core.Models;

namespace TBHStats.Capture;

/// <summary>
/// Детерминированная машина состояний для детекции завершения этапа
/// по последовательности наблюдений <see cref="RawObservation"/>
/// (FR-002, data-model §Детекция завершения этапа, ADR-012).
/// </summary>
/// <remarks>
/// <para>
/// <b>Состояния:</b>
/// <list type="bullet">
///   <item>
///     <b>InProgress</b> — начальное состояние. Этап идёт, босс ещё не был замечен.
///     Переход в <c>BossEngaged</c> происходит при явном <c>BossPresent == true</c>
///     и/или <c>StageProgress &gt;= 0.99</c>. null-значения не меняют состояние.
///   </item>
///   <item>
///     <b>BossEngaged</b> — босс появился. Детектор ожидает сигнала его гибели.
///     Сигнал = явный переход <c>BossPresent → false</c>
///     ИЛИ появление ненулевого <c>StageTimeSeconds</c> (итоговое время этапа в MainZone).
///     null-значения НЕ являются сигналом завершения — только явные данные.
///   </item>
/// </list>
/// </para>
/// <para>
/// <b>Идемпотентность:</b> после выдачи события состояние автоматически сбрасывается
/// в <c>InProgress</c>; повторные кадры с <c>BossPresent == false</c> не дадут нового события
/// до тех пор, пока не произойдёт новый переход через <c>BossEngaged</c>.
/// </para>
/// <para>
/// <b>null-семантика:</b> все null-значения полей <c>BossPresent</c>, <c>StageProgress</c>
/// и <c>StageTimeSeconds</c> трактуются как «поле не считано» (вкладка не та, OCR пропущен).
/// Отсутствие данных ≠ завершение и ≠ false; состояние не меняется.
/// Точные пороги уточняются эмпирически на фикстурах в T049.
/// </para>
/// </remarks>
public sealed class StageCompletionDetector : IStageCompletionDetector
{
    /// <summary>Порог прогрессбара, при котором босс считается появившимся (≈ конец полоски).</summary>
    /// <remarks>
    /// Используется как дополнительный триггер перехода в <c>BossEngaged</c>
    /// на случай, если <c>BossPresent</c> не считывается, но прогресс достиг конца.
    /// Эмпирически уточняется в T049.
    /// </remarks>
    private const double BossEngageProgressThreshold = 0.99;

    private DetectorState _state = DetectorState.InProgress;

    /// <inheritdoc/>
    public StageCompletionEvent? Observe(RawObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        switch (_state)
        {
            case DetectorState.InProgress:
                // Переход в BossEngaged: босс явно присутствует или прогресс достиг конца.
                // null не меняет состояние.
                bool bossVisible = observation.BossPresent == true;
                bool progressMaxed = observation.StageProgress.HasValue
                    && observation.StageProgress.Value >= BossEngageProgressThreshold;

                if (bossVisible || progressMaxed)
                    _state = DetectorState.BossEngaged;

                return null;

            case DetectorState.BossEngaged:
                // Сигнал завершения: ТОЛЬКО явный false (Boss исчез) ИЛИ ненулевое StageTimeSeconds.
                // null BossPresent и null StageTimeSeconds = «данных нет», не триггерим.
                bool bossGone = observation.BossPresent == false;
                bool stageTimeAppeared = observation.StageTimeSeconds.HasValue
                    && observation.StageTimeSeconds.Value > 0;

                if (bossGone || stageTimeAppeared)
                {
                    // Выдаём событие и немедленно сбрасываем в InProgress (идемпотентность).
                    _state = DetectorState.InProgress;
                    return new StageCompletionEvent(
                        CompletedAtUtc: observation.TakenAtUtc,
                        StageTimeSeconds: observation.StageTimeSeconds,
                        StageProgressAtCompletion: observation.StageProgress);
                }

                // Если снова BossPresent == true — остаёмся в BossEngaged (нормально).
                return null;

            default:
                // Защитная ветка от будущих расширений enum.
                return null;
        }
    }

    /// <inheritdoc/>
    public void Reset()
    {
        _state = DetectorState.InProgress;
    }

    /// <summary>Внутренние состояния машины детектора.</summary>
    private enum DetectorState
    {
        /// <summary>Этап идёт, босс ещё не появлялся.</summary>
        InProgress,

        /// <summary>Босс был замечен; ожидается сигнал его гибели.</summary>
        BossEngaged,
    }
}
