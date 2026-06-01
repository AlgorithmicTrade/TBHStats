namespace TBHStats.Core.Optimization;

/// <summary>
/// Детектор выброса ставки опыт/час относительно установившейся EMA — признак переходного
/// OCR-misread XP (одиночный кадр), независимо от триггера (mid-run глюк, смена этапа/панели).
/// </summary>
/// <remarks>
/// <para>
/// Проблема: переходные OCR-misread XP дают ложную дельту в несколько млн, которая
/// меньше потолка уровня — существующие магнитудный (delta &gt; XpToLevel) и level-guard
/// (levelDelta != 0 &amp;&amp; != 1) её не ловят. При этом raw опыт/ч оказывается в 100–500×
/// выше установившейся EMA, что является источник-агностичным признаком выброса.
/// </para>
/// <para>
/// Single Source of Truth для логики детекции выброса (Принцип II конституции):
/// определяется в Core, переиспользуется <c>StatsOrchestrator</c> и тестами без UI-зависимостей.
/// Аналог <c>HeroSwitchDetector</c> — чистый статический детектор без состояния.
/// </para>
/// </remarks>
public static class RateOutlierDetector
{
    /// <summary>
    /// Множитель EMA, при превышении которого raw-ставка считается выбросом.
    /// </summary>
    public const double OutlierFactor = 6.0;

    /// <summary>
    /// Абсолютный минимальный порог (опыт/ч), ниже которого правило не срабатывает —
    /// защита от ложных срабатываний при очень низкой EMA (старт, смена героя).
    /// </summary>
    public const double OutlierFloorPerHour = 5_000_000.0;

    /// <summary>
    /// Определяет, является ли <paramref name="rawXpPerHour"/> выбросом
    /// относительно установившейся EMA <paramref name="emaXpPerHour"/>.
    /// </summary>
    /// <param name="rawXpPerHour">
    /// Сырой темп опыт/ч, вычисленный по текущему интервалу.
    /// </param>
    /// <param name="emaXpPerHour">
    /// Установившаяся EMA темпа опыт/ч; <c>null</c> или ≤0 означает «база ещё не установлена»
    /// (начало сессии, после сброса при смене героя).
    /// </param>
    /// <returns>
    /// <c>true</c>, если EMA установлена и положительна, и raw резко превышает порог
    /// <c>max(EMA × OutlierFactor, OutlierFloorPerHour)</c>; иначе <c>false</c>.
    /// </returns>
    public static bool IsXpRateOutlier(double rawXpPerHour, double? emaXpPerHour)
    {
        if (emaXpPerHour is not double ema || ema <= 0.0)
            return false; // нет базы для суждения (старт/после сброса)

        double threshold = Math.Max(ema * OutlierFactor, OutlierFloorPerHour);
        return rawXpPerHour > threshold;
    }
}
