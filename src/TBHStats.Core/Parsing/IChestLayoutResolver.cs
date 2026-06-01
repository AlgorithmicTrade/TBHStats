namespace TBHStats.Core.Parsing;

/// <summary>
/// Одно OCR-показание из калиброванного @N-ROI сундука.
/// </summary>
/// <remarks>
/// Считается, что тип сундука присутствует в текущей раскладке тогда и только тогда,
/// когда <see cref="Count"/> ≥ 1 (иконка горит).
/// При Count == 0 иконка отсутствует/погасла — тип в раскладку не входит.
/// </remarks>
/// <param name="ChestTypeId">Идентификатор типа сундука (<c>ChestType.Id</c>).</param>
/// <param name="SlotCount">
/// N из суффикса @N ROI-ключа (1..3 в рамках TBH, в общем случае ≥ 1).
/// Обозначает, под раскладку с каким числом одновременных типов откалиброван данный ROI.
/// </param>
/// <param name="Count">Распознанное OCR количество точек (≥ 0).</param>
public readonly record struct ChestLayoutReading(int ChestTypeId, int SlotCount, int Count);

/// <summary>
/// Контракт алгоритма выбора активной раскладки сундуков.
/// </summary>
/// <remarks>
/// <para>
/// В MainZone игры Task Bar Hero иконка каждого типа сундука располагается
/// на разных ROI в зависимости от числа одновременно отображаемых типов (N = 1, 2, 3).
/// Калибровка хранит ROI с ключами вида «chest:brown@1», «chest:brown@2» и т.д.
/// </para>
/// <para>
/// Реализация принимает набор показаний от всех калиброванных ROI и выбирает,
/// какая раскладка (значение N) является активной в данный момент.
/// </para>
/// </remarks>
public interface IChestLayoutResolver
{
    /// <summary>
    /// Определяет активную раскладку сундуков по совокупности OCR-показаний.
    /// </summary>
    /// <param name="readings">
    /// Список показаний от всех доступных @N-ROI.
    /// Может содержать несколько показаний для одного и того же N и разных <c>ChestTypeId</c>.
    /// </param>
    /// <returns>
    /// Результат выбора раскладки: <see cref="ChestLayoutResolution.ChosenSlotCount"/> = 0
    /// означает «ни одного горящего сундука не обнаружено».
    /// </returns>
    ChestLayoutResolution Resolve(IReadOnlyList<ChestLayoutReading> readings);
}
