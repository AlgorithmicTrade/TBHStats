namespace TBHStats.Core.Parsing;

using TBHStats.Core.Models;

/// <summary>
/// Контракт валидации сырого наблюдения (кадра) и преобразования его в надёжный сэмпл (T024).
/// </summary>
public interface IObservationValidator
{
    /// <summary>
    /// Превращает сырое наблюдение в <see cref="MetricSample"/>, проставляя
    /// <see cref="MetricSample.IsReliable"/> по результатам sanity- и confidence-проверок.
    /// </summary>
    /// <param name="observation">
    /// Сырое наблюдение одного кадра, произведённое <c>IFieldExtractor</c>.
    /// </param>
    /// <param name="prevReliable">
    /// Последний НАДЁЖНЫЙ сэмпл того же этапа (<c>StageId</c> совпадает), использованный
    /// для проверки монотонности. Передавать <see langword="null"/>, если это первая точка этапа.
    /// </param>
    /// <param name="confidenceThreshold">
    /// Минимальная уверенность OCR-распознавания (∈ [0.0..1.0]) для принятия значения поля.
    /// Поле с <c>PerFieldConfidence[ключ] &lt; confidenceThreshold</c> считается ненадёжным:
    /// соответствующее значение в результате = null (не переносится в сэмпл).
    /// </param>
    /// <returns>
    /// Сэмпл <see cref="MetricSample"/> с заполненными полями, прошедшими порог confidence,
    /// и проставленным <see cref="MetricSample.IsReliable"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Confidence-фильтр:</b> поле принимается, только если
    /// <c>observation.PerFieldConfidence[ключ] &gt;= confidenceThreshold</c>;
    /// иначе соответствующее поле результата = null.
    /// </para>
    /// <para>
    /// <b>Монотонность золота:</b> если <c>observation.Gold</c> и <c>prevReliable.Gold</c>
    /// оба заданы, <c>StageId</c> совпадают, и <c>observation.Gold &lt; prevReliable.Gold</c>
    /// без объяснения — необъяснённое убывание → точка НЕнадёжна (<c>IsReliable = false</c>).
    /// </para>
    /// <para>
    /// <b>EXP-reset как level-up:</b> если <c>observation.Xp &lt; prevReliable.Xp</c>,
    /// НО <c>observation.HeroLevel &gt; prevReliable.HeroLevel</c> — это легитимный level-up,
    /// НЕ убыль опыта (точка надёжна при отсутствии других нарушений).
    /// </para>
    /// <para>
    /// <b>Транзиентные сундуки:</b> уменьшение числа «точек» MainZone = открытие сундука,
    /// НЕ ошибка sanity (≥0 всегда валидно). Значения копируются в
    /// <see cref="MetricSample.Chests"/> как <see cref="MetricSampleChest"/>.
    /// </para>
    /// <para>
    /// <b>IsReliable = true</b>, если нет нарушений sanity и есть хотя бы одно
    /// принятое значимое поле (Gold, Xp или HeroLevel).
    /// </para>
    /// </remarks>
    MetricSample Validate(RawObservation observation, MetricSample? prevReliable, double confidenceThreshold);
}
