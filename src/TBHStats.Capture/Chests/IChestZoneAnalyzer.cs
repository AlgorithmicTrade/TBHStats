using TBHStats.Core.Mechanics;
using TBHStats.Core.Models;

namespace TBHStats.Capture.Chests;

/// <summary>
/// Зонный анализатор плашек сундуков: в одной широкой ROI, охватывающей всю группу плашек
/// MainZone, локализует отдельные плашки по цвету фона и считает заполненные точки
/// по рядам в каждой плашке (ADR-023).
/// </summary>
/// <remarks>
/// Решает задачу, которую не решают позиционные ROI-схемы (ADR-018) и per-плашечный
/// <see cref="IChestPanelAnalyzer"/> (ADR-022): при переуплотнении группы (1/2/3 типа)
/// фиксированные координаты плашек смещаются, а ручные ROI обрезают ряды точек.
/// Зонная ROI накрывает всю группу целиком; тип каждой плашки определяется по цвету
/// фона (колонки классифицируются к ближайшему якорю из <see cref="GameMechanicsConfig"/>),
/// счёт точек — масштабонезависимый run-алгоритм по рядам.
/// </remarks>
public interface IChestZoneAnalyzer
{
    /// <summary>
    /// Анализирует зонную ROI кадра: локализует все присутствующие плашки сундуков
    /// по цвету и считает заполненные точки в каждой.
    /// </summary>
    /// <param name="frame">Захваченный кадр игры.</param>
    /// <param name="zoneRoi">
    /// Нормализованные координаты зоны, охватывающей всю горизонтальную группу плашек.
    /// Должен накрывать не только точки, но и фон плашек (для цветовой сегментации).
    /// </param>
    /// <param name="cfg">
    /// Конфиг механик: якорные цвета (<see cref="ChestType.PanelColor"/>) и список типов.
    /// </param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>
    /// Словарь <c>ChestTypeId → число заполненных точек</c> для всех найденных плашек.
    /// Типы, плашки которых не обнаружены в зоне, в словаре отсутствуют.
    /// Возвращает пустой словарь, если ни одна плашка не распознана.
    /// </returns>
    Task<IReadOnlyDictionary<int, int>> AnalyzeZoneAsync(
        CapturedFrame frame,
        RoiCalibration zoneRoi,
        GameMechanicsConfig cfg,
        CancellationToken ct);
}
