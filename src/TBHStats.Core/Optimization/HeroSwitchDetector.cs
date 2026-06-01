namespace TBHStats.Core.Optimization;

using TBHStats.Core.Models;

/// <summary>
/// Чистый детектор смены героя по структурным сигналам из пары сэмплов.
/// </summary>
/// <remarks>
/// Single Source of Truth для логики детекции смены героя (Принцип II конституции):
/// определяется в Core, переиспользуется <c>StatsOrchestrator</c> и тестами без UI-зависимостей.
/// Смена героя = разрыв непрерывности одного героя, определяемый по двум инвариантам:
/// <list type="number">
///   <item>
///     <term>Падение уровня:</term>
///     <description>
///     Герой не теряет уровни; падение <c>HeroLevel</c> означает другого героя или misread.
///     </description>
///   </item>
///   <item>
///     <term>Смена потолка опыта без сигнатуры level-up:</term>
///     <description>
///     Потолок <c>XpToLevel</c> зависит от уровня; его изменение легитимно только при level-up,
///     а level-up происходит при почти полном опыте (≥ 80 % потолка предыдущего сэмпла).
///     Изменение потолка при не-полном опыте — другой герой (или мусор OCR).
///     </description>
///   </item>
/// </list>
/// Метод НЕ возвращает <c>true</c> на одиночный или множественный рост уровня сам по себе —
/// это может быть легитимный level-up или idle-набор за свёрнутое окно (FR-005a).
/// </remarks>
public static class HeroSwitchDetector
{
    /// <summary>
    /// Определяет, является ли переход от <paramref name="previous"/> к <paramref name="current"/>
    /// сменой героя.
    /// </summary>
    /// <param name="previous">Предыдущий надёжный сэмпл (до текущего кадра).</param>
    /// <param name="current">Текущий надёжный сэмпл (новый кадр).</param>
    /// <returns>
    /// <c>true</c>, если сигнал указывает на смену героя; <c>false</c> в случае нормальной
    /// непрерывности или когда данных недостаточно для вынесения суждения.
    /// </returns>
    public static bool IsHeroSwitch(MetricSample previous, MetricSample current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        // Правило 1: падение уровня — герой не теряет уровни, это другой герой/misread.
        if (previous.HeroLevel.HasValue && current.HeroLevel.HasValue
            && current.HeroLevel.Value < previous.HeroLevel.Value)
        {
            return true;
        }

        // Правило 2: смена потолка опыта без сигнатуры level-up.
        // Потолок (XpToLevel) привязан к уровню. Изменение легитимно ТОЛЬКО при level-up,
        // а level-up происходит при почти полном опыте (≥ 80 % потолка previous).
        // Если потолок изменился, а previous.Xp НЕ был близко к потолку — это другой герой.
        if (previous.XpToLevel.HasValue
            && current.XpToLevel.HasValue
            && previous.Xp.HasValue
            && previous.XpToLevel.Value != current.XpToLevel.Value)
        {
            bool nearFull = previous.Xp.Value >= previous.XpToLevel.Value * 0.8;
            if (!nearFull)
            {
                return true;
            }
        }

        return false;
    }
}
