namespace TBHStats.Capture.Progress;

/// <summary>
/// Результат считывания прогрессбара текущего этапа из MainZone (ADR-024).
/// </summary>
/// <param name="Progress">
/// Доля прохождения этапа в диапазоне [0.0..1.0], или <see langword="null"/> если ROI не читаема
/// (кадр пустой, нет данных).
/// <list type="bullet">
///   <item>0.0 — начало этапа (бар пуст).</item>
///   <item>0.95 — конец пути (бар полностью фиолетовый, впереди бой с боссом).</item>
///   <item>1.0 — идёт бой с боссом (бар полностью синий).</item>
/// </list>
/// </param>
/// <param name="BossPresent">
/// <see langword="true"/> если в ROI бара обнаружена значимая доля синих колонок (бой с боссом);
/// <see langword="false"/> если бар читаем, но синего нет;
/// <see langword="null"/> если ROI не читаема.
/// </param>
public readonly record struct StageProgressReading(double? Progress, bool? BossPresent);
