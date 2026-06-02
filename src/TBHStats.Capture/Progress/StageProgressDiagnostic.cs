namespace TBHStats.Capture.Progress;

/// <summary>
/// Диагностический результат анализа ROI прогрессбара (ADR-024): итоговые
/// <see cref="Progress"/>/<see cref="BossPresent"/> плюс «сырые» счётчики и образец цвета.
/// Используется экраном калибровки для визуальной проверки правильности ROI
/// (число фиолетовых/синих/тёмных колонок и средний цвет заливки на живом кадре).
/// </summary>
/// <param name="RoiWidthPx">Ширина ROI в пикселях кадра.</param>
/// <param name="RoiHeightPx">Высота ROI в пикселях кадра.</param>
/// <param name="PurpleColumns">Число колонок, классифицированных как фиолетовые (заливка пути).</param>
/// <param name="BlueColumns">Число колонок, классифицированных как синие (бой с боссом).</param>
/// <param name="DarkColumns">Число колонок тёмного трека (пустая часть бара).</param>
/// <param name="OtherColumns">Число колонок без признаков бара (ни цвета, ни тёмного трека).</param>
/// <param name="SampleR">Средний R по цветным (фиол/син) пикселям ROI (0, если цветных нет).</param>
/// <param name="SampleG">Средний G по цветным пикселям ROI.</param>
/// <param name="SampleB">Средний B по цветным пикселям ROI.</param>
/// <param name="Progress">Итоговый прогресс этапа ∈ [0..1] или null (ROI нечитаем).</param>
/// <param name="BossPresent">Итоговый признак боя с боссом или null (ROI нечитаем).</param>
public readonly record struct StageProgressDiagnostic(
    int RoiWidthPx,
    int RoiHeightPx,
    int PurpleColumns,
    int BlueColumns,
    int DarkColumns,
    int OtherColumns,
    int SampleR,
    int SampleG,
    int SampleB,
    double? Progress,
    bool? BossPresent)
{
    /// <summary>Пустой результат для нечитаемого/вырожденного ROI (всё нулевое, Progress/BossPresent null).</summary>
    public static readonly StageProgressDiagnostic Empty =
        new(0, 0, 0, 0, 0, 0, 0, 0, 0, null, null);
}
