namespace TBHStats.Core.Models;

/// <summary>
/// Сохранённая геометрия окна приложения (позиция и размер) для восстановления между
/// сессиями (FR-016). Идентифицируется ключом окна <see cref="WindowKey"/> (например «compare»).
/// Обобщает персист геометрии на несколько окон без дублирования полей в каждой таблице.
/// </summary>
/// <remarks>
/// Виджет использует отдельную историческую модель <see cref="WidgetSettings"/>
/// (она хранит дополнительно topmost / тему / интервал опроса). <see cref="WindowPlacement"/> —
/// для прочих окон (Сравнение, в перспективе Графики/Калибровка): только геометрия.
/// </remarks>
public sealed class WindowPlacement
{
    /// <summary>Ключ окна — первичный ключ записи (например «compare», «charts», «calibration»).</summary>
    public required string WindowKey { get; init; }

    /// <summary>Позиция окна по горизонтали (пиксели экрана).</summary>
    public double PosX { get; init; }

    /// <summary>Позиция окна по вертикали (пиксели экрана).</summary>
    public double PosY { get; init; }

    /// <summary>Ширина окна (пиксели).</summary>
    public double Width { get; init; }

    /// <summary>Высота окна (пиксели).</summary>
    public double Height { get; init; }
}
