namespace TBHStats.Capture.Chests;

/// <summary>
/// Результат анализа одной ROI-области плашки сундука: распознанный тип и число точек.
/// </summary>
/// <param name="ChestTypeId">
/// Идентификатор типа сундука (<c>ChestType.Id</c>), определённый по цвету фона плашки.
/// <see langword="null"/> — тип не распознан (плашка не обнаружена, ROI на тёмной сцене).
/// </param>
/// <param name="DotCount">
/// Число заполненных (тёмных) точек внутри плашки. Имеет смысл только при
/// <c>ChestTypeId != null</c>.
/// </param>
/// <param name="PanelMatch">
/// Мера качества классификации цвета плашки: значение в диапазоне [0..1],
/// где 1.0 = идеальное совпадение с якорным цветом. Используется при мёрдже
/// нескольких ROI одного типа — принимается чтение с наибольшим <c>PanelMatch</c>.
/// Равен 0 при <c>ChestTypeId == null</c>.
/// </param>
public readonly record struct ChestPanelReading(int? ChestTypeId, int DotCount, double PanelMatch);
