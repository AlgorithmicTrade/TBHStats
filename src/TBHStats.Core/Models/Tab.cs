namespace TBHStats.Core.Models;

/// <summary>
/// Справочник вкладок интерфейса игры (ADR-008, FR-002b).
/// Известные ключи: "hero", "stash", "status", "runes", "cube",
/// "portal", "settings", "tradeship", "mailbox".
/// IsDataSource=true: hero, status, portal — основные источники данных.
/// </summary>
public sealed class Tab
{
    /// <summary>Суррогатный первичный ключ.</summary>
    public int Id { get; init; }

    /// <summary>Уникальный машинный ключ вкладки.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Отображаемое название вкладки в UI.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Эталонный текст для OCR-матчинга названия вкладки на экране.
    /// Используется <c>ITabDetector</c> для определения активной вкладки.
    /// </summary>
    public string RecognitionText { get; init; } = string.Empty;

    /// <summary>Порядок отображения в UI.</summary>
    public int SortOrder { get; init; }

    /// <summary>Активна ли запись (soft-delete).</summary>
    public bool IsActive { get; init; } = true;

    /// <summary>
    /// Является ли вкладка источником данных (true: hero, status, portal).
    /// false: stash, runes, cube, settings, tradeship, mailbox.
    /// </summary>
    public bool IsDataSource { get; init; }
}
