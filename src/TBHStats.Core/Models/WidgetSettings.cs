namespace TBHStats.Core.Models;

/// <summary>
/// Настройки виджета: позиция, размер, поведение и оформление (FR-015, FR-016).
/// </summary>
public sealed class WidgetSettings
{
    /// <summary>Позиция виджета по горизонтали (пиксели экрана).</summary>
    public double PosX { get; init; }

    /// <summary>Позиция виджета по вертикали (пиксели экрана).</summary>
    public double PosY { get; init; }

    /// <summary>Ширина виджета (пиксели).</summary>
    public double Width { get; init; }

    /// <summary>Высота виджета (пиксели).</summary>
    public double Height { get; init; }

    /// <summary>Поверх всех окон (FR-015).</summary>
    public bool AlwaysOnTop { get; init; }

    /// <summary>Тема оформления виджета.</summary>
    public Theme Theme { get; init; } = Theme.System;

    /// <summary>Интервал опроса OCR в миллисекундах (по умолчанию ~1500 мс).</summary>
    public int PollIntervalMs { get; init; } = 1500;
}
