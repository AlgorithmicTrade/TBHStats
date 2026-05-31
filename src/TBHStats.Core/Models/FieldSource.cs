namespace TBHStats.Core.Models;

/// <summary>
/// Местоположение считываемого поля (FR-002b).
/// Определяет, из какой зоны экрана берётся значение.
/// </summary>
public enum FieldSource
{
    /// <summary>Поле находится в главной зоне игры (MainZone) — видно всегда.</summary>
    MainZone,

    /// <summary>Поле находится на одной из вкладок интерфейса — видно только при активной вкладке.</summary>
    Tab,
}
