namespace TBHStats.Core.Parsing;

using System.Collections.Generic;
using TBHStats.Core.Mechanics;
using TBHStats.Core.Models;

/// <summary>
/// Стабилизатор значения «Следующая локация» (nextLocation) против транзиентного OCR-шума.
/// </summary>
/// <remarks>
/// <para>
/// На локациях ~3-6..3-10 фоновый огонь заставляет OCR выдавать мусорные чтения поля nextLocation.
/// Два механизма защиты:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Оконное голосование</b>: стабильным становится значение, встретившееся
///     ≥ <see cref="_confirmCount"/> раз в окне последних <see cref="_windowSize"/>
///     ВАЛИДНЫХ чтений (прошедших sanity-фильтр). Устойчиво к пропускам и одиночному шуму:
///     null-кадры (OCR ничего не вернул) окно НЕ изменяют; одиночный шумный кадр (1 раз в окне)
///     не проходит порог ≥ confirmCount; флакирующее, но реально присутствующее значение
///     (≥ 2 из 5, даже не подряд) становится стабильным.
///   </item>
///   <item>
///     <b>Sanity-фильтр</b>: чтение игнорируется, если его «предыдущий этап»
///     (current = nextLocation − 1) не резолвится в реальный Stage через
///     <see cref="GameMechanicsConfig.ResolveStageId"/>. Значения, выходящие за пределы
///     матрицы 3×2×10, отбрасываются как несуществующие.
///   </item>
/// </list>
/// <para>
/// Stateful объект: один экземпляр на <c>StatsOrchestrator</c> — состояние хранится внутри.
/// Без WinRT/EF/UI-зависимостей; пригоден для переиспользования в MAUI-клиенте (ADR-010).
/// </para>
/// <para>
/// <b>Ограничение:</b> если огонь даёт стабильно-неверное, но РЕЗОЛВИМОЕ чтение
/// ≥ <see cref="_confirmCount"/> раз в окне — sanity-фильтр его не отсеет.
/// Для таких случаев потребуется jump-distance escalation (следующий шаг при необходимости).
/// </para>
/// </remarks>
public sealed class NextLocationStabilizer
{
    private readonly int _windowSize;
    private readonly int _confirmCount;

    /// <summary>Скользящее окно последних ВАЛИДНЫХ чтений (прошедших sanity).</summary>
    private readonly List<StageRef> _window;

    /// <summary>Текущее принятое стабильное значение; null, пока ни одно не подтверждено.</summary>
    private StageRef? _stable;

    /// <param name="windowSize">
    /// Размер скользящего окна валидных чтений. Должен быть ≥ 1; дефолт 5.
    /// </param>
    /// <param name="confirmCount">
    /// Сколько раз значение должно встретиться в окне, чтобы стать стабильным.
    /// Должен быть ≥ 1 и ≤ <paramref name="windowSize"/>; дефолт 2.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Если <paramref name="windowSize"/> &lt; 1, <paramref name="confirmCount"/> &lt; 1,
    /// или <paramref name="confirmCount"/> &gt; <paramref name="windowSize"/>.
    /// </exception>
    public NextLocationStabilizer(int windowSize = 5, int confirmCount = 2)
    {
        if (windowSize < 1)
            throw new ArgumentOutOfRangeException(
                nameof(windowSize),
                windowSize,
                "windowSize должен быть ≥ 1.");

        if (confirmCount < 1)
            throw new ArgumentOutOfRangeException(
                nameof(confirmCount),
                confirmCount,
                "confirmCount должен быть ≥ 1.");

        if (confirmCount > windowSize)
            throw new ArgumentOutOfRangeException(
                nameof(confirmCount),
                confirmCount,
                "confirmCount не может превышать windowSize.");

        _windowSize   = windowSize;
        _confirmCount = confirmCount;
        _window       = new List<StageRef>(windowSize);
    }

    /// <summary>Текущее стабильное значение nextLocation (null, пока ни одно не подтверждено).</summary>
    public StageRef? Stable => _stable;

    /// <summary>
    /// Принимает «сырое» чтение nextLocation за один кадр и возвращает текущее СТАБИЛЬНОЕ значение.
    /// </summary>
    /// <remarks>
    /// <para>Алгоритм:</para>
    /// <list type="number">
    ///   <item>
    ///     <b>null-кадр</b> — OCR ничего не вернул в этом кадре.
    ///     Скользящее окно и стабильное значение не изменяются; возвращается <see cref="Stable"/>.
    ///     null-кадры не мешают накоплению валидных чтений в окне.
    ///   </item>
    ///   <item>
    ///     <b>Sanity-проверка</b> — вычисляем «текущий этап» (current = reading − 1)
    ///     и пробуем резолвить его через <paramref name="cfg"/>.
    ///     Если current == null (т.е. nextLocation == Act1/*/1, предыдущего нет) ИЛИ
    ///     <c>ResolveStageId(current)</c> == null — чтение мусорное, игнорируется;
    ///     в окно НЕ добавляется.
    ///   </item>
    ///   <item>
    ///     <b>Добавление в окно</b> — валидное чтение добавляется в скользящее окно;
    ///     самое старое вытесняется, если размер превышает <see cref="_windowSize"/>.
    ///   </item>
    ///   <item>
    ///     <b>Голосование</b> — подсчёт частот значений в окне; <c>best</c> = значение
    ///     с максимальной частотой. При равных частотах выбирается самое <b>недавнее</b>
    ///     (последнее по позиции в окне) из лидеров.
    ///     Если частота <c>best</c> ≥ <see cref="_confirmCount"/> → <c>_stable = best</c>.
    ///   </item>
    /// </list>
    /// </remarks>
    /// <param name="reading">Сырое чтение nextLocation за кадр (null = OCR ничего не дал).</param>
    /// <param name="cfg">Конфиг игровой механики для sanity-резолва (реальный, не мок).</param>
    /// <returns>Текущее стабильное значение после применения оконного голосования + sanity.</returns>
    public StageRef? Observe(StageRef? reading, GameMechanicsConfig cfg)
    {
        // 1. null-кадр: нет данных — окно и stable не трогаем, пропуск не ломает накопление.
        if (reading is null)
            return _stable;

        // 2. Sanity: «текущий этап» = nextLocation − 1.
        // Если предыдущего нет (Act1/*/stage1) или он не резолвится — мусорное чтение; в окно не добавляем.
        StageRef? current = reading.Value.Previous();
        if (current is null || cfg.ResolveStageId(current.Value) is null)
            return _stable;

        // 3. Добавить валидное чтение в скользящее окно (вытеснить самое старое при переполнении).
        if (_window.Count >= _windowSize)
            _window.RemoveAt(0);
        _window.Add(reading.Value);

        // 4. Голосование: подсчёт частот; при ничье — самое недавнее (последнее в _window) из лидеров.
        Dictionary<StageRef, int> freq = new(_window.Count);
        foreach (StageRef v in _window)
        {
            if (!freq.TryAdd(v, 1))
                freq[v]++;
        }

        StageRef? best = null;
        int bestFreq   = 0;

        // Обходим _window с конца — чтобы при одинаковых частотах первым «лидером» оказалось САМОЕ НЕДАВНЕЕ.
        for (int i = _window.Count - 1; i >= 0; i--)
        {
            StageRef v = _window[i];
            int f = freq[v];
            if (f > bestFreq)
            {
                bestFreq = f;
                best     = v;
            }
        }

        // 5. Если лидер набрал достаточно голосов — обновляем стабильное.
        if (best.HasValue && bestFreq >= _confirmCount)
            _stable = best.Value;

        return _stable;
    }

    /// <summary>
    /// Сброс всего состояния: скользящее окно и стабильное значение обнуляются.
    /// Вызывается при потере окна игры (NotFound), чтобы на другом этапе не тянулось
    /// устаревшее накопленное окно.
    /// </summary>
    public void Reset()
    {
        _stable = null;
        _window.Clear();
    }
}
