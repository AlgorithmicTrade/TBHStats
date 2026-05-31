using FluentAssertions;
using TBHStats.Capture.Tabs;
using TBHStats.Core.Mechanics;
using Xunit;

namespace TBHStats.Capture.Tests;

/// <summary>
/// Юнит-тесты шва <see cref="ITabNameMatcher.Match"/> (TDD red-фаза, T019).
/// Реализация <see cref="TabNameMatcher"/> — заглушка, бросающая
/// <see cref="NotImplementedException"/>; все тесты ожидаемо падают.
///
/// Алгоритм (зафиксирован в &lt;remarks&gt; ITabNameMatcher):
///   1. Нормализация: trim + ToLowerInvariant + схлопывание пробелов.
///   2. Похожесть = 1.0 − Lev(norm1, norm2) / Max(len1, len2).
///   3. Матч по RecognitionText всех IsActive-вкладок; выбирается максимум.
///   4. ≥ minSimilarity → TabRef; иначе null.
///   5. Пустой / whitespace → немедленно null.
/// </summary>
public sealed class TabDetectorTests
{
    private readonly ITabNameMatcher _sut = new TabNameMatcher();
    private readonly GameMechanicsConfig _cfg = GameMechanicsConfig.CreateDefault();

    // ──────────────────────────────────────────────────────────────────────────
    // 1. Точное совпадение
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// «Hero» точно совпадает с RecognitionText="Hero".
    /// Ожидаемая похожесть: Lev("hero","hero")=0 → sim = 1 − 0/4 = 1.0.
    /// </summary>
    [Fact]
    public void Match_ExactHero_ReturnsKeyHeroWithConfidenceOne()
    {
        var result = _sut.Match("Hero", _cfg);

        result.Should().NotBeNull();
        result!.Value.Key.Should().Be("hero");
        result.Value.TabId.Should().Be(1);
        result.Value.Confidence.Should().BeApproximately(1.0, precision: 1e-9);
    }

    /// <summary>
    /// «Stash» точно совпадает с RecognitionText="Stash".
    /// Ожидаемая похожесть: Lev("stash","stash")=0 → sim=1.0.
    /// </summary>
    [Fact]
    public void Match_ExactStash_ReturnsKeyStashWithConfidenceOne()
    {
        var result = _sut.Match("Stash", _cfg);

        result.Should().NotBeNull();
        result!.Value.Key.Should().Be("stash");
        result.Value.Confidence.Should().BeApproximately(1.0, precision: 1e-9);
    }

    /// <summary>
    /// «Portal» точно совпадает с RecognitionText="Portal".
    /// Ожидаемая похожесть: sim=1.0.
    /// </summary>
    [Fact]
    public void Match_ExactPortal_ReturnsKeyPortalWithConfidenceOne()
    {
        var result = _sut.Match("Portal", _cfg);

        result.Should().NotBeNull();
        result!.Value.Key.Should().Be("portal");
        result.Value.Confidence.Should().BeApproximately(1.0, precision: 1e-9);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. Нормализация регистра и пробелов
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// «  hero  » — лишние пробелы по краям убираются trim'ом.
    /// После нормализации "hero" vs "hero" → sim=1.0.
    /// </summary>
    [Fact]
    public void Match_LeadingTrailingSpaces_ReturnsHeroWithConfidenceOne()
    {
        var result = _sut.Match("  hero  ", _cfg);

        result.Should().NotBeNull();
        result!.Value.Key.Should().Be("hero");
        result.Value.Confidence.Should().BeApproximately(1.0, precision: 1e-9);
    }

    /// <summary>
    /// «HERO» — все буквы верхнего регистра; ToLowerInvariant нормализует до "hero".
    /// sim=1.0.
    /// </summary>
    [Fact]
    public void Match_AllUpperCaseHero_ReturnsHeroWithConfidenceOne()
    {
        var result = _sut.Match("HERO", _cfg);

        result.Should().NotBeNull();
        result!.Value.Key.Should().Be("hero");
        result.Value.Confidence.Should().BeApproximately(1.0, precision: 1e-9);
    }

    /// <summary>
    /// «Trade  ship» — двойной пробел внутри схлопывается регекcпом \s+ → " ".
    /// После нормализации "trade ship" vs "trade ship" → sim=1.0.
    /// </summary>
    [Fact]
    public void Match_TradeShipWithDoubleSpace_ReturnsTradeshipWithConfidenceOne()
    {
        var result = _sut.Match("Trade  ship", _cfg);

        result.Should().NotBeNull();
        result!.Value.Key.Should().Be("tradeship");
        result.Value.Confidence.Should().BeApproximately(1.0, precision: 1e-9);
    }

    /// <summary>
    /// «  Mail  Box  » — несколько пробелов + trim + схлопывание.
    /// После нормализации "mail box" vs "mail box" → sim=1.0.
    /// </summary>
    [Fact]
    public void Match_MailBoxMultipleSpaces_ReturnsMailboxWithConfidenceOne()
    {
        var result = _sut.Match("  Mail  Box  ", _cfg);

        result.Should().NotBeNull();
        result!.Value.Key.Should().Be("mailbox");
        result.Value.Confidence.Should().BeApproximately(1.0, precision: 1e-9);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. Лёгкий OCR-шум (1 опечатка)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// «Statvs» — 1 замена (v→u).
    /// Lev("statvs","status")=1, max("statvs","status")=6 → sim=1−1/6≈0.8333.
    /// Уверенность в диапазоне (0.6..1.0), ближайшая вкладка — "status".
    /// </summary>
    [Fact]
    public void Match_StatusWithOcrNoise_ReturnsStatusWithinRange()
    {
        // Ручной расчёт: "statvs" vs "status"
        //   s=s, t=t, a=a, t=t, v→u (subst), s=s → Lev=1
        //   max(6, 6)=6 → sim = 1 − 1/6 ≈ 0.83333
        const double expectedSim = 1.0 - 1.0 / 6.0;

        var result = _sut.Match("Statvs", _cfg);

        result.Should().NotBeNull();
        result!.Value.Key.Should().Be("status");
        result.Value.Confidence.Should().BeApproximately(expectedSim, precision: 1e-9);
        result.Value.Confidence.Should().BeGreaterThan(0.6);
        result.Value.Confidence.Should().BeLessThan(1.0);
    }

    /// <summary>
    /// «Heroo» — 1 вставка (лишняя 'o').
    /// Lev("heroo","hero")=1, max(5,4)=5 → sim=1−1/5=0.8.
    /// Ближайшая вкладка — "hero".
    /// </summary>
    [Fact]
    public void Match_HerooWithExtraChar_ReturnsHeroWithinRange()
    {
        // Ручной расчёт: "heroo" vs "hero"
        //   h=h, e=e, r=r, o=o, o→∅ (delete) → Lev=1
        //   max(5, 4)=5 → sim = 1 − 1/5 = 0.8
        const double expectedSim = 1.0 - 1.0 / 5.0;

        var result = _sut.Match("Heroo", _cfg);

        result.Should().NotBeNull();
        result!.Value.Key.Should().Be("hero");
        result.Value.Confidence.Should().BeApproximately(expectedSim, precision: 1e-9);
        result.Value.Confidence.Should().BeGreaterThan(0.6);
        result.Value.Confidence.Should().BeLessThan(1.0);
    }

    /// <summary>
    /// «PortaI» — OCR заменяет 'l' на 'I' (прописную i).
    /// После ToLowerInvariant: "portai" vs "portal".
    /// Lev("portai","portal")=1 (i→l), max(6,6)=6 → sim=1−1/6≈0.8333.
    /// Ближайшая вкладка — "portal".
    /// </summary>
    [Fact]
    public void Match_PortalWithCapitalI_ReturnsPortalWithinRange()
    {
        // Ручной расчёт: "portai" vs "portal"
        //   p=p, o=o, r=r, t=t, a=a, i→l (subst) → Lev=1
        //   max(6, 6)=6 → sim = 1 − 1/6 ≈ 0.83333
        const double expectedSim = 1.0 - 1.0 / 6.0;

        var result = _sut.Match("PortaI", _cfg);

        result.Should().NotBeNull();
        result!.Value.Key.Should().Be("portal");
        result.Value.Confidence.Should().BeApproximately(expectedSim, precision: 1e-9);
        result.Value.Confidence.Should().BeGreaterThan(0.6);
        result.Value.Confidence.Should().BeLessThan(1.0);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. Нет совпадения — ниже порога
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// «xyzqwerty» — не похоже ни на одну вкладку; все сходства ниже 0.6.
    /// Ожидаем null при minSimilarity=0.6 (по умолчанию).
    /// </summary>
    [Fact]
    public void Match_RandomString_ReturnsNull()
    {
        var result = _sut.Match("xyzqwerty", _cfg);

        result.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. Пустой / whitespace recognizedText → немедленно null
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Пустая строка → немедленно null (шаг 5 алгоритма).
    /// </summary>
    [Fact]
    public void Match_EmptyString_ReturnsNull()
    {
        var result = _sut.Match(string.Empty, _cfg);

        result.Should().BeNull();
    }

    /// <summary>
    /// Строка только из пробелов → немедленно null.
    /// </summary>
    [Fact]
    public void Match_WhitespaceOnly_ReturnsNull()
    {
        var result = _sut.Match("   ", _cfg);

        result.Should().BeNull();
    }

    /// <summary>
    /// Строка из смешанных пробелов (пробел + tab + newline) → null.
    /// </summary>
    [Fact]
    public void Match_MixedWhitespace_ReturnsNull()
    {
        var result = _sut.Match(" \t\n ", _cfg);

        result.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. Влияние параметра minSimilarity
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// «por» vs «portal»: Lev("por","portal")=3 (вставить t,a,l), max(3,6)=6 → sim=1−3/6=0.5.
    /// При minSimilarity=0.6 → null (0.5 &lt; 0.6).
    /// При minSimilarity=0.4 → не-null (0.5 ≥ 0.4).
    ///
    /// Ручной расчёт по матрице Вагнера–Фишера:
    ///   "por" → "portal": вставка 't' (cost 1), вставка 'a' (cost 1), вставка 'l' (cost 1) → Lev=3.
    ///   max(len("por")=3, len("portal")=6) = 6 → sim = 1 − 3/6 = 0.5.
    /// </summary>
    [Fact]
    public void Match_PartialPortal_AboveDefaultThreshold_ReturnsNull()
    {
        // sim("por", "portal") = 0.5 < minSimilarity=0.6 → null
        var result = _sut.Match("por", _cfg, minSimilarity: 0.6);

        result.Should().BeNull();
    }

    [Fact]
    public void Match_PartialPortal_AboveLowerThreshold_ReturnsPortal()
    {
        // sim("por", "portal") = 0.5 ≥ minSimilarity=0.4 → TabRef(portal)
        const double expectedSim = 1.0 - 3.0 / 6.0; // 0.5

        var result = _sut.Match("por", _cfg, minSimilarity: 0.4);

        result.Should().NotBeNull();
        result!.Value.Key.Should().Be("portal");
        result.Value.Confidence.Should().BeApproximately(expectedSim, precision: 1e-9);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. Возвращаемый TabRef валиден — значения соответствуют конфигу
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Для любого матча: TabId и Key соответствуют записи в GameMechanicsConfig.Tabs.
    /// Проверяем на «Settings» (Id=7, Key="settings").
    /// </summary>
    [Fact]
    public void Match_ExactSettings_TabIdAndKeyMatchConfig()
    {
        var result = _sut.Match("Settings", _cfg);

        result.Should().NotBeNull();

        var tab = _cfg.Tabs.Single(t => t.Key == result!.Value.Key);
        result!.Value.TabId.Should().Be(tab.Id);
        result.Value.Key.Should().Be(tab.Key);
    }

    /// <summary>
    /// Для матча «Runes» (Id=4, Key="runes"): TabId == 4.
    /// </summary>
    [Fact]
    public void Match_ExactRunes_TabIdEqualsConfigId()
    {
        var result = _sut.Match("Runes", _cfg);

        result.Should().NotBeNull();
        result!.Value.TabId.Should().Be(4);
        result.Value.Key.Should().Be("runes");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 8. minSimilarity=0.0 — любая строка с хотя бы одной буквой → матч
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// При minSimilarity=0.0 любая непустая строка должна вернуть ненулевой результат
    /// (какая-то вкладка наберёт похожесть > 0).
    /// Исключение: строки, совпадающие с нормализацией в пустую строку, — там похожесть = 0.
    /// Используем «a» — хоть что-то общее с любой вкладкой найдётся.
    /// </summary>
    [Fact]
    public void Match_SingleCharWithZeroThreshold_ReturnsAnyMatch()
    {
        var result = _sut.Match("a", _cfg, minSimilarity: 0.0);

        // Sim("a", "stash") = 1 − Lev("a","stash")/max(1,5) = 1−4/5=0.2 → ≥ 0.0 → non-null
        result.Should().NotBeNull();
        result!.Value.Confidence.Should().BeGreaterThanOrEqualTo(0.0);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 9. Только IsActive=true вкладки участвуют в матче
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Конфиг с единственной вкладкой IsActive=false — любая строка → null.
    /// Проверяет, что неактивные вкладки исключаются из перебора.
    /// </summary>
    [Fact]
    public void Match_AllTabsInactive_ReturnsNull()
    {
        // Создаём конфиг, где все вкладки неактивны
        var inactiveTabs = _cfg.Tabs
            .Select(t => new TBHStats.Core.Models.Tab
            {
                Id              = t.Id,
                Key             = t.Key,
                DisplayName     = t.DisplayName,
                RecognitionText = t.RecognitionText,
                SortOrder       = t.SortOrder,
                IsActive        = false,         // деактивируем
                IsDataSource    = t.IsDataSource,
            })
            .ToArray();

        var cfgAllInactive = new GameMechanicsConfig(
            chestTypes:          _cfg.ChestTypes,
            heroClasses:         _cfg.HeroClasses,
            tabs:                inactiveTabs,
            acts:                _cfg.Acts,
            difficulties:        _cfg.Difficulties,
            stages:              _cfg.Stages,
            fieldSourceBindings: _cfg.FieldSourceBindings);

        var result = _sut.Match("Hero", cfgAllInactive);

        result.Should().BeNull();
    }
}
