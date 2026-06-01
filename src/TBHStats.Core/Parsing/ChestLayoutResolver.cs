namespace TBHStats.Core.Parsing;

/// <summary>
/// Результат выбора активной раскладки сундуков.
/// </summary>
/// <remarks>
/// <para>
/// Содержит выбранное число одновременных типов (<see cref="ChosenSlotCount"/>)
/// и словарь ChestTypeId → Count только для «горящих» типов (Count ≥ 1).
/// </para>
/// <para>
/// <see cref="ChosenSlotCount"/> == 0 означает, что ни одного активного сундука не обнаружено
/// (все Count == 0 или список показаний пуст).
/// </para>
/// </remarks>
public sealed class ChestLayoutResolution
{
    /// <summary>
    /// Выбранное число одновременно присутствующих типов сундуков (N).
    /// 0 — активных сундуков нет.
    /// </summary>
    public int ChosenSlotCount { get; init; }

    /// <summary>
    /// Словарь ChestTypeId → Count для типов с Count ≥ 1 в выбранной раскладке.
    /// При <see cref="ChosenSlotCount"/> == 0 — пустой.
    /// </summary>
    public IReadOnlyDictionary<int, int> Counts { get; init; } =
        new Dictionary<int, int>();
}

/// <summary>
/// Детерминированный алгоритм выбора активной раскладки сундуков MainZone.
/// </summary>
/// <remarks>
/// <para>
/// Алгоритм основан на инварианте «litCount == N»:
/// при N одновременно отображаемых типах ровно N из N ROI данной раскладки должны гореть
/// (Count ≥ 1). Перебор ведётся от большего N к меньшему, чтобы при неоднозначности
/// предпочесть более полную раскладку.
/// </para>
/// <para>
/// Присутствие типа ⟺ Count ≥ 1 (иконка видна).
/// Отсутствие типа ⟺ Count == 0 (иконка погасла/не отображается).
/// </para>
/// <para>
/// Реализация чистая: без статического состояния, без WinRT/EF-зависимостей.
/// </para>
/// </remarks>
public sealed class ChestLayoutResolver : IChestLayoutResolver
{
    /// <inheritdoc />
    public ChestLayoutResolution Resolve(IReadOnlyList<ChestLayoutReading> readings)
    {
        if (readings.Count == 0)
            return Empty();

        // Группировка по SlotCount: N → список показаний с этим N.
        // Используем Dictionary для детерминированности; порядок обхода — убывающий N.
        Dictionary<int, List<ChestLayoutReading>> bySlotCount = [];

        foreach (ChestLayoutReading r in readings)
        {
            if (!bySlotCount.TryGetValue(r.SlotCount, out List<ChestLayoutReading>? list))
            {
                list = [];
                bySlotCount[r.SlotCount] = list;
            }
            list.Add(r);
        }

        // Перебираем N от большего к меньшему.
        IEnumerable<int> descendingKeys = bySlotCount.Keys.OrderByDescending(k => k);

        // Шаг 1 — найти N с точным совпадением litCount == N.
        foreach (int n in descendingKeys)
        {
            List<ChestLayoutReading> group = bySlotCount[n];
            Dictionary<int, int> litMap = BuildLitMap(group);

            if (litMap.Count == n)
                return new ChestLayoutResolution
                {
                    ChosenSlotCount = n,
                    Counts          = litMap,
                };
        }

        // Шаг 2 — fallback: N с максимальным litCount > 0; при равенстве — большее N.
        int bestN       = 0;
        int bestLitCount = 0;
        Dictionary<int, int>? bestLitMap = null;

        foreach (int n in descendingKeys)
        {
            List<ChestLayoutReading> group = bySlotCount[n];
            Dictionary<int, int> litMap = BuildLitMap(group);
            int litCount = litMap.Count;

            if (litCount > 0 && (litCount > bestLitCount || (litCount == bestLitCount && n > bestN)))
            {
                bestN        = n;
                bestLitCount = litCount;
                bestLitMap   = litMap;
            }
        }

        if (bestLitMap is not null)
            return new ChestLayoutResolution
            {
                ChosenSlotCount = bestN,
                Counts          = bestLitMap,
            };

        return Empty();
    }

    // ── Вспомогательные ─────────────────────────────────────────────────────

    /// <summary>
    /// Строит словарь ChestTypeId → Count только для «горящих» показаний (Count ≥ 1).
    /// При дублирующемся ChestTypeId в одной группе берётся последнее по порядку значение.
    /// </summary>
    private static Dictionary<int, int> BuildLitMap(List<ChestLayoutReading> group)
    {
        Dictionary<int, int> map = [];
        foreach (ChestLayoutReading r in group)
        {
            if (r.Count >= 1)
                map[r.ChestTypeId] = r.Count;  // перезапись — последнее детерминировано
        }
        return map;
    }

    private static ChestLayoutResolution Empty() =>
        new() { ChosenSlotCount = 0, Counts = new Dictionary<int, int>() };
}
