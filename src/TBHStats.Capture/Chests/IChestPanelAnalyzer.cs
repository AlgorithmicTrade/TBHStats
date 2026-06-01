using TBHStats.Core.Mechanics;
using TBHStats.Core.Models;

namespace TBHStats.Capture.Chests;

/// <summary>
/// Визуальный анализатор плашки сундука: определяет тип сундука по цвету фона плашки
/// и считает заполненные точки (ADR-022).
/// </summary>
/// <remarks>
/// Заменяет позиционную @N-схему идентификации типа: тип определяется по доминирующему
/// яркому цвету плашки (сравнение с якорями в <see cref="GameMechanicsConfig"/>),
/// а не по <c>FieldKey</c> ROI. Счёт точек — прежний run-алгоритм по яркости.
/// </remarks>
public interface IChestPanelAnalyzer
{
    /// <summary>
    /// Анализирует ROI-область плашки сундука: распознаёт тип по цвету и считает точки.
    /// </summary>
    /// <param name="frame">Захваченный кадр.</param>
    /// <param name="roi">Нормализованные координаты ROI, накрывающего всю плашку целиком.</param>
    /// <param name="cfg">Конфиг механик с якорными цветами плашек (<see cref="ChestType.PanelColor"/>).</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>
    /// <see cref="ChestPanelReading"/> с распознанным типом и числом точек.
    /// Если <see cref="ChestPanelReading.ChestTypeId"/> равен <see langword="null"/> — плашка
    /// не распознана (reject); счёт и <c>PanelMatch</c> не достоверны.
    /// </returns>
    Task<ChestPanelReading> AnalyzeChestPanelAsync(
        CapturedFrame frame,
        RoiCalibration roi,
        GameMechanicsConfig cfg,
        CancellationToken ct);
}
