namespace TBHStats.Core.Mechanics;

/// <summary>
/// Контракт доступа к конфигурации игровой механики (FR-021, services.md).
/// Позволяет добавлять новые типы сундуков и классы героя без правки кода —
/// путём вызова <see cref="Reload"/> с обновлённым конфигом.
/// </summary>
public interface IGameMechanics
{
    /// <summary>Текущая конфигурация механик (типы сундуков, классы, акты/сложности/этапы).</summary>
    GameMechanicsConfig Current { get; }

    /// <summary>
    /// Заменить текущий конфиг на новый (например, после загрузки из БД или файла).
    /// </summary>
    /// <param name="cfg">Новый конфиг. Не может быть null.</param>
    /// <exception cref="ArgumentNullException">Если <paramref name="cfg"/> равен null.</exception>
    void Reload(GameMechanicsConfig cfg);
}
