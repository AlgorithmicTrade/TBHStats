namespace TBHStats.Core.Models;

/// <summary>
/// Якорный RGB-цвет фона плашки сундука в MainZone для визуальной идентификации типа (T062-fix).
/// Не персистится в БД — используется только из in-memory конфига.
/// </summary>
/// <param name="R">Красный канал (0–255).</param>
/// <param name="G">Зелёный канал (0–255).</param>
/// <param name="B">Синий канал (0–255).</param>
public readonly record struct PanelColor(byte R, byte G, byte B);
