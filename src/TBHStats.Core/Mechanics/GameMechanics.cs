namespace TBHStats.Core.Mechanics;

/// <summary>
/// Реализация <see cref="IGameMechanics"/> — держатель текущего конфига игровой механики (FR-021).
/// По умолчанию инициализируется дефолтным сидом через <see cref="GameMechanicsConfig.CreateDefault"/>.
/// Потокобезопасность чтения <see cref="Current"/> — volatile-семантика через Interlocked/замену ссылки;
/// для v1 однопоточного использования достаточно прямого присваивания.
/// </summary>
public sealed class GameMechanics : IGameMechanics
{
    private GameMechanicsConfig _current;

    /// <summary>
    /// Создать экземпляр с дефолтным конфигом (<see cref="GameMechanicsConfig.CreateDefault"/>).
    /// </summary>
    public GameMechanics()
    {
        _current = GameMechanicsConfig.CreateDefault();
    }

    /// <summary>
    /// Создать экземпляр с явно переданным конфигом (для тестирования или кастомного сида).
    /// </summary>
    /// <param name="initialConfig">Начальный конфиг. Не может быть null.</param>
    /// <exception cref="ArgumentNullException">Если <paramref name="initialConfig"/> равен null.</exception>
    public GameMechanics(GameMechanicsConfig initialConfig)
    {
        ArgumentNullException.ThrowIfNull(initialConfig);
        _current = initialConfig;
    }

    /// <inheritdoc />
    public GameMechanicsConfig Current => _current;

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Если <paramref name="cfg"/> равен null.</exception>
    public void Reload(GameMechanicsConfig cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        _current = cfg;
    }
}
