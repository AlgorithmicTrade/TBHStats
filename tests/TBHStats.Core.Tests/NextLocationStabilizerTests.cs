namespace TBHStats.Core.Tests;

using FluentAssertions;
using TBHStats.Core.Mechanics;
using TBHStats.Core.Models;
using TBHStats.Core.Parsing;
using Xunit;

/// <summary>
/// Тесты <see cref="NextLocationStabilizer"/> (оконное голосование + sanity-фильтр nextLocation).
/// Используется реальный <see cref="GameMechanicsConfig.CreateDefault()"/> — без моков.
/// </summary>
/// <remarks>
/// nextLocation — «следующая» локация (current+1). «Текущий этап» = nextLocation.Previous().
/// Чтобы чтение прошло sanity, его Previous() обязан резолвиться в ResolveStageId(cfg).
/// Примеры валидных nextLocation-значений (previous резолвится в cfg):
///   Act1/normal/3 → Previous = Act1/normal/2 → Stage Id в конфиге → OK.
///   Act1/normal/2 → Previous = Act1/normal/1 → Stage Id в конфиге → OK.
/// Пример невалидного (sanity-fail):
///   Act7/normal/9 → Previous = Act7/normal/8 → ResolveStageId = null (акт 7 не существует) → игнорируется.
/// </remarks>
public sealed class NextLocationStabilizerTests
{
    private readonly GameMechanicsConfig _cfg = GameMechanicsConfig.CreateDefault();

    private static StageRef Loc(int act, string diff, int stage) => new(act, diff, stage);

    // -- Удобные StageRef: валидные nextLocation (Previous резолвится в дефолтном конфиге) --
    private static readonly StageRef A = Loc(1, "normal", 3);  // current = Act1/normal/2 — OK
    private static readonly StageRef B = Loc(1, "normal", 5);  // current = Act1/normal/4 — OK
    private static readonly StageRef C = Loc(2, "normal", 4);  // current = Act2/normal/3 — OK

    // Невалидный nextLocation: Previous = Act7/normal/8 → не резолвится (акт 7 не существует).
    private static readonly StageRef Trash = Loc(7, "normal", 9);

    // =========================================================================
    // Конструктор — валидация параметров
    // =========================================================================

    [Fact]
    public void Constructor_WindowSize0_ThrowsArgumentOutOfRangeException()
    {
        Action act = () => _ = new NextLocationStabilizer(windowSize: 0, confirmCount: 1);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("windowSize");
    }

    [Fact]
    public void Constructor_ConfirmCount0_ThrowsArgumentOutOfRangeException()
    {
        Action act = () => _ = new NextLocationStabilizer(windowSize: 3, confirmCount: 0);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("confirmCount");
    }

    [Fact]
    public void Constructor_ConfirmCountGreaterThanWindowSize_ThrowsArgumentOutOfRangeException()
    {
        Action act = () => _ = new NextLocationStabilizer(windowSize: 3, confirmCount: 4);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("confirmCount");
    }

    [Fact]
    public void Constructor_ConfirmCountEqualsWindowSize_DoesNotThrow()
    {
        Action act = () => _ = new NextLocationStabilizer(windowSize: 3, confirmCount: 3);
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_ValidParams_DoesNotThrow()
    {
        Action act = () => _ = new NextLocationStabilizer(windowSize: 4, confirmCount: 2);
        act.Should().NotThrow();
    }

    // =========================================================================
    // Первое значение — требует confirmCount появлений в окне
    // =========================================================================

    [Fact]
    public void FirstValue_OneFrame_StableRemainsNull()
    {
        // windowSize=5, confirmCount=2 → один кадр недостаточен
        var sut = new NextLocationStabilizer(windowSize: 5, confirmCount: 2);

        StageRef? result = sut.Observe(A, _cfg);

        result.Should().BeNull("одного кадра недостаточно для confirmCount=2");
        sut.Stable.Should().BeNull();
    }

    [Fact]
    public void FirstValue_TwoFramesSameValue_StableBecomesValue()
    {
        var sut = new NextLocationStabilizer(windowSize: 5, confirmCount: 2);
        sut.Observe(A, _cfg);

        StageRef? result = sut.Observe(A, _cfg);

        result.Should().Be(A, "два кадра A в окне 5 → частота 2 ≥ confirmCount 2 → стабильно");
        sut.Stable.Should().Be(A);
    }

    // =========================================================================
    // Ключевой кейс: 2 раза НЕ подряд → стабильно (защита от зависания)
    // =========================================================================

    [Fact]
    public void TwoOccurrences_NotConsecutive_WithNoiseBetween_BecomesStable()
    {
        // windowSize=5, confirmCount=2. A встречается 2 раза, между ними B (валидный мисрид).
        // Ожидание: после второго A окно = [A, B, A] → freq(A)=2 ≥ 2 → стабильно A.
        // Это ключевой кейс против зависания при флакирующем OCR.
        var sut = new NextLocationStabilizer(windowSize: 5, confirmCount: 2);

        sut.Observe(A, _cfg);  // окно=[A]
        sut.Observe(B, _cfg);  // окно=[A,B]  — B — валидный мисрид (шум)
        StageRef? result = sut.Observe(A, _cfg);  // окно=[A,B,A] → freq(A)=2 ≥ 2

        result.Should().Be(A, "A встретилось 2 раза из 3 в окне, хотя не подряд (шум B между ними)");
        sut.Stable.Should().Be(A);
    }

    [Fact]
    public void TwoOccurrences_NotConsecutive_WithNullBetween_BecomesStable()
    {
        // null-кадр между двумя A не ломает накопление окна.
        var sut = new NextLocationStabilizer(windowSize: 5, confirmCount: 2);

        sut.Observe(A, _cfg);   // окно=[A]
        sut.Observe(null, _cfg); // null — окно НЕ меняется: [A]
        StageRef? result = sut.Observe(A, _cfg);  // окно=[A,A] → freq(A)=2 ≥ 2

        result.Should().Be(A, "null-кадр между двумя A не мешает накоплению: окно [A,A]");
        sut.Stable.Should().Be(A);
    }

    [Fact]
    public void TwoOccurrences_WithMixedNoiseAndNulls_BecomesStable()
    {
        // Окно: A, null, B (шум), null, A → последнее состояние [A, B, A] → freq(A)=2
        var sut = new NextLocationStabilizer(windowSize: 5, confirmCount: 2);

        sut.Observe(A, _cfg);
        sut.Observe(null, _cfg);
        sut.Observe(B, _cfg);   // шум
        sut.Observe(null, _cfg);
        StageRef? result = sut.Observe(A, _cfg);  // окно=[A,B,A]

        result.Should().Be(A, "A дважды в окне несмотря на шум и null-пропуски");
        sut.Stable.Should().Be(A);
    }

    // =========================================================================
    // Одиночный шум не меняет стабильное значение
    // =========================================================================

    [Fact]
    public void SingleFrameNoise_StableA_StableRemainsA()
    {
        // Стабильное A, потом окно: [A,A,B] → freq(A)=2, freq(B)=1 → A остаётся стабильным.
        var sut = MakeStabilized(A);  // windowSize=5, окно=[A,A]

        StageRef? result = sut.Observe(B, _cfg);  // окно=[A,A,B]

        result.Should().Be(A, "одиночный B в окне [A,A,B] → freq(B)=1 < 2 → A сохраняется");
        sut.Stable.Should().Be(A);
    }

    // =========================================================================
    // Sanity-фильтр: мусорные чтения игнорируются (в окно не добавляются)
    // =========================================================================

    [Fact]
    public void Sanity_TrashReading_SingleFrame_StableUnchanged()
    {
        var sut = MakeStabilized(A);

        StageRef? result = sut.Observe(Trash, _cfg);

        result.Should().Be(A, "мусорное чтение игнорируется — в окно не добавляется");
        sut.Stable.Should().Be(A);
    }

    [Fact]
    public void Sanity_TrashReading_ThreeTimesInRow_StableUnchanged()
    {
        // Мусор 3 раза подряд — в окно не добавляется → freq(Trash)=0 → никогда не стабилен.
        var sut = MakeStabilized(A);

        sut.Observe(Trash, _cfg);
        sut.Observe(Trash, _cfg);
        StageRef? result = sut.Observe(Trash, _cfg);

        result.Should().Be(A, "мусор не попадает в окно, не набирает голоса даже 3 раза подряд");
        sut.Stable.Should().Be(A);
    }

    [Fact]
    public void Sanity_TrashReading_NoStableYet_StableRemainsNull()
    {
        var sut = new NextLocationStabilizer(windowSize: 5, confirmCount: 2);

        sut.Observe(Trash, _cfg);
        sut.Observe(Trash, _cfg);
        sut.Observe(Trash, _cfg);

        sut.Stable.Should().BeNull("мусорные кадры без стабильного не создают стабильное");
    }

    // =========================================================================
    // null-кадры не ломают накопление окна
    // =========================================================================

    [Fact]
    public void NullFrame_DoesNotChangeWindow_AccumulationContinues()
    {
        var sut = new NextLocationStabilizer(windowSize: 5, confirmCount: 2);

        sut.Observe(A, _cfg);    // окно=[A]
        sut.Observe(null, _cfg); // окно=[A] (не изменилось)
        sut.Observe(null, _cfg); // окно=[A] (не изменилось)
        StageRef? result = sut.Observe(A, _cfg); // окно=[A,A] → freq(A)=2 ≥ 2

        result.Should().Be(A, "два null не мешают накоплению: после них второй A замыкает порог");
        sut.Stable.Should().Be(A);
    }

    [Fact]
    public void NullFrame_ReturnsCurrentStable()
    {
        var sut = MakeStabilized(A);

        StageRef? result = sut.Observe(null, _cfg);

        result.Should().Be(A, "null-кадр возвращает текущее стабильное без изменений");
    }

    [Fact]
    public void NullFrame_NoStable_ReturnsNull()
    {
        var sut = new NextLocationStabilizer(windowSize: 5, confirmCount: 2);

        StageRef? result = sut.Observe(null, _cfg);

        result.Should().BeNull("null при пустом состоянии → null");
    }

    // =========================================================================
    // Вытеснение из окна: старое значение, выпавшее за windowSize, теряет вклад
    // =========================================================================

    [Fact]
    public void WindowEviction_OldValueFallsOut_LosesVote()
    {
        // windowSize=3, confirmCount=2. Добавляем A, B, B → окно [A,B,B] → freq(B)=2 ≥ 2 → stable=B.
        // Затем добавляем C, C → окно [B,C,C] → freq(C)=2 ≥ 2 → stable=C.
        // A был только 1 раз и давно — после вытеснения его частота = 0 (не мешает).
        var sut = new NextLocationStabilizer(windowSize: 3, confirmCount: 2);

        sut.Observe(A, _cfg);  // [A]
        sut.Observe(B, _cfg);  // [A,B]
        sut.Observe(B, _cfg);  // [A,B,B] → freq(B)=2 → stable=B
        sut.Stable.Should().Be(B);

        sut.Observe(C, _cfg);  // [B,B,C] → stable=B (freq(B)=2 ещё держится)
        sut.Observe(C, _cfg);  // [B,C,C] → freq(C)=2 → stable=C

        sut.Stable.Should().Be(C, "C вытеснила B, набрав 2 голоса в новом окне");
    }

    [Fact]
    public void WindowEviction_ValueOccurredOnceEarly_DoesNotAccumulateWithLaterSame()
    {
        // windowSize=3. A встречается в позиции 0 и потом снова в позиции 3 (уже за окном).
        // Между ними B,B — заполняет окно. Затем A снова:
        //   окно становится [B,B,A] → freq(A)=1 (только последний) — позиция 0 уже вытеснена.
        var sut = new NextLocationStabilizer(windowSize: 3, confirmCount: 2);

        sut.Observe(A, _cfg);  // [A]
        sut.Observe(B, _cfg);  // [A,B]
        sut.Observe(B, _cfg);  // [A,B,B] → stable=B
        StageRef? result = sut.Observe(A, _cfg);  // [B,B,A] → freq(A)=1 (старый A вытеснен) → stable остаётся B

        result.Should().Be(B, "A встреченное давно (до вытеснения из окна) + A сейчас = freq 1, не 2 → B сохраняется");
    }

    // =========================================================================
    // Смена этапа A → B
    // =========================================================================

    [Fact]
    public void StageChange_AtoB_BBecomesStableAfterConfirmCount()
    {
        // Стабильное A (окно [A,A]). Затем B дважды → окно [A,A,B,B] → freq(A)=2, freq(B)=2.
        // Tie: берём самое недавнее → B.
        var sut = MakeStabilized(A);  // окно=[A,A]

        sut.Observe(B, _cfg);  // [A,A,B]  → freq(B)=1 → stable=A
        StageRef? result = sut.Observe(B, _cfg);  // [A,A,B,B] → freq(A)=2, freq(B)=2 → tie → B (недавнее)

        result.Should().Be(B, "B набрал confirmCount=2 в окне, при ничье выигрывает недавнее B");
        sut.Stable.Should().Be(B);
    }

    [Fact]
    public void StageChange_AtoB_ThreeB_ClearlyWins()
    {
        // windowSize=5, confirmCount=2. Стабильное A (окно [A,A]). Три B подряд → окно [A,A,B,B,B].
        // freq(B)=3 > freq(A)=2 → B стабильно.
        var sut = MakeStabilized(A);  // окно=[A,A]

        sut.Observe(B, _cfg);
        sut.Observe(B, _cfg);
        StageRef? result = sut.Observe(B, _cfg);  // [A,A,B,B,B] → freq(B)=3 → stable=B

        result.Should().Be(B, "три B в окне [A,A,B,B,B] → freq(B)=3 > freq(A)=2 → B победил");
        sut.Stable.Should().Be(B);
    }

    // =========================================================================
    // Tie-break по «самому недавнему»
    // =========================================================================

    [Fact]
    public void TieBreak_ReturnsMoreRecentValue()
    {
        // windowSize=4, confirmCount=2. Окно: [A, A, B, B] → freq равны по 2.
        // Самое недавнее из лидеров — B (последнее в окне) → stable=B.
        var sut = new NextLocationStabilizer(windowSize: 4, confirmCount: 2);

        sut.Observe(A, _cfg);  // [A]
        sut.Observe(A, _cfg);  // [A,A]
        sut.Observe(B, _cfg);  // [A,A,B]
        StageRef? result = sut.Observe(B, _cfg);  // [A,A,B,B] → freq(A)=2, freq(B)=2 → tie → B (недавнее)

        result.Should().Be(B, "при ничье (A×2, B×2) выигрывает самое недавнее — B");
        sut.Stable.Should().Be(B);
    }

    // =========================================================================
    // Параметр confirmCount=1: первый валидный кадр сразу стабилен
    // =========================================================================

    [Fact]
    public void ConfirmCount1_AcceptsFirstValidFrame()
    {
        var sut = new NextLocationStabilizer(windowSize: 5, confirmCount: 1);

        StageRef? result = sut.Observe(A, _cfg);

        result.Should().Be(A, "confirmCount=1 → первый валидный кадр сразу становится стабильным");
        sut.Stable.Should().Be(A);
    }

    // =========================================================================
    // Параметр windowSize=4, confirmCount=2
    // =========================================================================

    [Fact]
    public void Params_WindowSize4ConfirmCount2_WorksCorrectly()
    {
        var sut = new NextLocationStabilizer(windowSize: 4, confirmCount: 2);

        sut.Observe(A, _cfg);  // [A]
        sut.Stable.Should().BeNull("1 кадр < confirmCount=2");

        sut.Observe(A, _cfg);  // [A,A] → freq(A)=2 ≥ 2
        sut.Stable.Should().Be(A);

        // B 2 раза → [A,A,B,B] → tie → B (недавнее)
        sut.Observe(B, _cfg);
        sut.Observe(B, _cfg);
        sut.Stable.Should().Be(B);
    }

    // =========================================================================
    // Reset
    // =========================================================================

    [Fact]
    public void Reset_ClearsStableAndWindow()
    {
        var sut = MakeStabilized(A);  // окно=[A,A], stable=A

        sut.Reset();

        sut.Stable.Should().BeNull("Reset обнуляет стабильное");

        // После сброса нужен полный новый набор confirmCount кадров
        StageRef? after1 = sut.Observe(A, _cfg);
        after1.Should().BeNull("после Reset первый кадр A ещё не набрал confirmCount=2");

        StageRef? after2 = sut.Observe(A, _cfg);
        after2.Should().Be(A, "после Reset два кадра A → A снова стабильно");
    }

    [Fact]
    public void Reset_ClearsWindow_OldVotesLost()
    {
        // Проверяем, что Reset сбрасывает ОКНО, а не только _stable.
        // Если окно не сброшено, один дополнительный кадр B после Reset сразу дал бы stable=B.
        var sut = MakeStabilized(A);  // окно=[A,A]
        sut.Observe(B, _cfg);         // окно=[A,A,B]

        sut.Reset();  // окно должно полностью очиститься

        // Теперь один кадр B не должен делать B стабильным (нужно 2)
        StageRef? result = sut.Observe(B, _cfg);
        result.Should().BeNull("после Reset один кадр B не стабилен (окно очищено, freq(B)=1 < 2)");
    }

    [Fact]
    public void Reset_OnEmptyState_DoesNotThrow()
    {
        var sut = new NextLocationStabilizer(windowSize: 5, confirmCount: 2);

        Action act = () => sut.Reset();
        act.Should().NotThrow();
        sut.Stable.Should().BeNull();
    }

    // =========================================================================
    // Вспомогательный метод: быстро установить стабильное значение
    // =========================================================================

    /// <summary>
    /// Инициализирует стабилайзер (windowSize=5, confirmCount=2) так, чтобы
    /// <paramref name="value"/> стало стабильным (два кадра подряд).
    /// </summary>
    private NextLocationStabilizer MakeStabilized(StageRef value)
    {
        var sut = new NextLocationStabilizer(windowSize: 5, confirmCount: 2);
        sut.Observe(value, _cfg);
        sut.Observe(value, _cfg);
        sut.Stable.Should().Be(value,
            $"MakeStabilized: после 2 кадров {value} должно быть стабильным");
        return sut;
    }
}
