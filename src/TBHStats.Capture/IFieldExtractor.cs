using TBHStats.Core.Mechanics;
using TBHStats.Core.Models;

namespace TBHStats.Capture;

/// <summary>
/// Считывает поля одного кадра, ДОСТУПНЫЕ при текущей активной вкладке,
/// и собирает <see cref="RawObservation"/> (FR-002/FR-002b).
/// </summary>
/// <remarks>
/// <para>
/// Правило доступности поля (FR-002b):
/// <list type="bullet">
///   <item><see cref="FieldSource.MainZone"/> — доступно всегда.</item>
///   <item><see cref="FieldSource.Tab"/> — доступно только если активная вкладка
///         совпадает с <see cref="RoiCalibration.TabId"/>.</item>
/// </list>
/// Недоступные поля не читаются и остаются <c>null</c> в наблюдении.
/// </para>
/// <para>
/// Поля <c>stageProgress</c> и <c>bossPresent</c> являются визуальными (не текстовыми)
/// и не распознаются здесь — детекция завершения этапа выполняется отдельным компонентом
/// (T035 StageCompletionDetector).
/// </para>
/// </remarks>
public interface IFieldExtractor
{
    /// <summary>
    /// Извлекает доступные поля из <paramref name="frame"/> и возвращает <see cref="RawObservation"/>.
    /// </summary>
    /// <param name="frame">Кадр содержимого игрового окна.</param>
    /// <param name="rois">Список ROI-калибровок всех полей.</param>
    /// <param name="activeTab">Активная вкладка, определённая <c>ITabDetector</c>; null — вкладка неизвестна.</param>
    /// <param name="cfg">Конфиг игровых механик (используется для маппинга chest-ключей в ChestType.Id).</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>
    /// <see cref="RawObservation"/> с заполненными доступными полями.
    /// Недоступные поля равны <c>null</c>; <see cref="RawObservation.Chests"/> и
    /// <see cref="RawObservation.PerFieldConfidence"/> — пустые словари, если ничего не считано.
    /// </returns>
    Task<RawObservation> ExtractAsync(
        CapturedFrame frame,
        IReadOnlyList<RoiCalibration> rois,
        TabRef? activeTab,
        GameMechanicsConfig cfg,
        CancellationToken ct);
}
