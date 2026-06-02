using TBHStats.Core.Mechanics;
using TBHStats.Core.Models;

namespace TBHStats.Capture.Progress;

/// <summary>
/// Визуальный детектор прогрессбара этапа: читает долю прохождения и наличие боссового боя
/// из горизонтального прогрессбара в правом-нижнем углу MainZone (ADR-024).
/// </summary>
/// <remarks>
/// Реализация работает без OCR: определяет состояние бара по цвету пикселей
/// (фиолетовый = заливка пути, синий = бой с боссом, тёмный = пустой трек).
/// Координаты ROI передаются через <see cref="RoiCalibration"/> с FieldKey <c>"stageProgress"</c>.
/// </remarks>
public interface IStageProgressReader
{
    /// <summary>
    /// Считывает прогресс этапа из кадра по заданному ROI.
    /// </summary>
    /// <param name="frame">Захваченный кадр игры.</param>
    /// <param name="roi">
    /// Нормализованные координаты зоны прогрессбара (FieldKey <c>"stageProgress"</c>).
    /// </param>
    /// <param name="cfg">Конфиг механик (передаётся для совместимости сигнатуры; цветовые якоря захардкожены как пиксельные константы рендера).</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>
    /// <see cref="StageProgressReading"/> с полями <c>Progress</c> и <c>BossPresent</c>.
    /// При нечитаемом ROI оба поля — <see langword="null"/>.
    /// </returns>
    Task<StageProgressReading> ReadAsync(
        CapturedFrame frame,
        RoiCalibration roi,
        GameMechanicsConfig cfg,
        CancellationToken ct);

    /// <summary>
    /// Диагностический анализ ROI прогрессбара для экрана калибровки: помимо итоговых
    /// <c>Progress</c>/<c>BossPresent</c> возвращает «сырые» счётчики колонок и образец цвета
    /// заливки, чтобы пользователь мог визуально проверить правильность ROI на живом кадре.
    /// </summary>
    /// <param name="frame">Захваченный кадр игры.</param>
    /// <param name="roi">Нормализованные координаты зоны прогрессбара.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns><see cref="StageProgressDiagnostic"/> со счётчиками и итоговыми значениями.</returns>
    Task<StageProgressDiagnostic> DiagnoseAsync(
        CapturedFrame frame,
        RoiCalibration roi,
        CancellationToken ct);
}
