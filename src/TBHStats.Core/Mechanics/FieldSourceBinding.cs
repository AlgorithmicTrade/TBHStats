namespace TBHStats.Core.Mechanics;

using TBHStats.Core.Models;

/// <summary>
/// Связка «поле → источник данных» (FR-002b).
/// Определяет, из какой зоны экрана считывается конкретное поле:
/// MainZone (всегда видно) или конкретная вкладка Tab (только при её активации).
/// </summary>
/// <param name="FieldKey">Ключ поля, например "gold", "stageId", "chest:brown".</param>
/// <param name="Source">Источник поля: MainZone или Tab.</param>
/// <param name="TabId">
/// Id вкладки (<see cref="Tab.Id"/>) — заполняется только при Source=Tab.
/// При Source=MainZone должен быть null.
/// </param>
public sealed record FieldSourceBinding(
    string FieldKey,
    FieldSource Source,
    int? TabId);
