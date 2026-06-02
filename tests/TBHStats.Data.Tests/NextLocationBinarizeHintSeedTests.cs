namespace TBHStats.Data.Tests;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TBHStats.Core.Models;
using Xunit;

/// <summary>
/// Регресс-тесты фикса «binarize_white» для поля <c>nextLocation</c> в <see cref="DatabaseInitializer"/>.
///
/// Фокус:
/// — свежая БД: ROI <c>nextLocation</c> засевается с <c>ParseHint="binarize_white"</c>;
///   другие поля (<c>gold</c>, <c>heroLevel</c>) получают <c>ParseHint=null</c>.
/// — self-heal: существующий ROI с пустым хинтом обновляется при повторном <c>InitializeAsync</c>.
/// — идемпотентность: явно заданный пользователем хинт (например, <c>"custom"</c>) не перезаписывается.
///
/// Все тесты работают на реальном временном файловом SQLite (не in-memory, research R5).
/// </summary>
public sealed class NextLocationBinarizeHintSeedTests : TempDbFixture
{
    // ─────────────────────────────────────────────────────────────────────────
    // Тест 1: Свежая БД — nextLocation засеян с ParseHint="binarize_white"
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Верифицирует, что после InitializeAsync (реальные миграции + сидинг)
    /// ROI <c>nextLocation</c> имеет <c>ParseHint="binarize_white"</c>.
    /// Регресс-гард против повторного появления пустого ParseHint у nextLocation.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_FreshDb_NextLocationRoi_HasBinarizeWhiteHint()
    {
        // БД уже инициализирована в InitializeAsync фикстуры TempDbFixture.

        RoiCalibration? nextLoc = await Db.RoiCalibrations
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.FieldKey == "nextLocation");

        nextLoc.Should().NotBeNull("ROI nextLocation должен быть засеян при инициализации");
        nextLoc!.ParseHint.Should().Be(
            "binarize_white",
            "nextLocation использует мелкий пиксельный шрифт — требует предобработки binarize_white (T066)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Тест 2: Свежая БД — другие поля НЕ получают binarize_white
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Верифицирует, что предобработка <c>binarize_white</c> применена ТОЛЬКО к <c>nextLocation</c>
    /// и не утекла на другие поля. <c>heroLevel</c> читается штатно без бинаризации (T066).
    /// </summary>
    [Fact]
    public async Task InitializeAsync_FreshDb_OtherFields_HaveNullParseHint()
    {
        // Проверяем несколько конкретных полей — gold и heroLevel:
        // — gold: обычный UI-шрифт, читается без бинаризации.
        // — heroLevel: обычный UI-шрифт; бинаризация даже чуть ухудшает (verified T066).
        string[] fieldsExpectedNull = ["gold", "heroLevel"];

        foreach (string fieldKey in fieldsExpectedNull)
        {
            RoiCalibration? roi = await Db.RoiCalibrations
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.FieldKey == fieldKey);

            if (roi is not null)
            {
                roi.ParseHint.Should().BeNull(
                    $"поле '{fieldKey}' не требует binarize_white и должно иметь ParseHint=null");
            }
        }

        // Общая проверка: среди всех ROI только nextLocation имеет ParseHint != null
        List<RoiCalibration> allWithHint = await Db.RoiCalibrations
            .AsNoTracking()
            .Where(r => r.ParseHint != null)
            .ToListAsync();

        allWithHint.Should().AllSatisfy(r =>
            r.FieldKey.Should().Be(
                "nextLocation",
                "ParseHint != null допустим ТОЛЬКО для nextLocation"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Тест 3: Self-heal — существующий nextLocation с пустым хинтом обновляется
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Симулирует «существующую БД пользователя» с пустым ParseHint у nextLocation
    /// (ситуация до фикса): вручную обнуляет хинт через SQL (bulk update — обходит init-only),
    /// затем вызывает InitializeAsync повторно.
    /// Верифицирует, что ParseHint становится "binarize_white" (self-heal).
    /// Координаты X/Y/W/H при этом НЕ должны измениться.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_ExistingNextLocationWithNullHint_SetsHintOnNextInit()
    {
        // Arrange: имитируем «старую БД» — вручную очищаем ParseHint у nextLocation.
        // RoiCalibration.ParseHint объявлен init-only → используем bulk ExecuteUpdateAsync для Arrange.

        // Сначала сохраним «пользовательские» координаты через bulk update.
        const double savedX = 0.1;
        const double savedY = 0.2;
        const double savedW = 0.3;
        const double savedH = 0.4;
        await Db.RoiCalibrations
            .Where(r => r.FieldKey == "nextLocation")
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.ParseHint, (string?)null)
                .SetProperty(r => r.X, savedX)
                .SetProperty(r => r.Y, savedY)
                .SetProperty(r => r.W, savedW)
                .SetProperty(r => r.H, savedH));

        // Подтверждаем: хинт действительно пуст
        RoiCalibration? nextLocBefore = await Db.RoiCalibrations
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.FieldKey == "nextLocation");

        nextLocBefore.Should().NotBeNull("ROI nextLocation должен существовать после первого Init");
        nextLocBefore!.ParseHint.Should().BeNull("подготовка: хинт должен быть пуст перед повторным Init");

        // Act: повторный вызов InitializeAsync (следующий запуск приложения)
        await DatabaseInitializer.InitializeAsync(Db, CancellationToken.None);

        // Assert: ParseHint восстановлен (self-heal)
        RoiCalibration? nextLocAfter = await Db.RoiCalibrations
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.FieldKey == "nextLocation");

        nextLocAfter.Should().NotBeNull();
        nextLocAfter!.ParseHint.Should().Be(
            "binarize_white",
            "self-heal должен проставить binarize_white при следующем запуске");

        // Assert: координаты пользователя НЕ затронуты
        nextLocAfter.X.Should().BeApproximately(savedX, 1e-9, "X пользователя не должен измениться");
        nextLocAfter.Y.Should().BeApproximately(savedY, 1e-9, "Y пользователя не должен измениться");
        nextLocAfter.W.Should().BeApproximately(savedW, 1e-9, "W пользователя не должен измениться");
        nextLocAfter.H.Should().BeApproximately(savedH, 1e-9, "H пользователя не должен измениться");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Тест 4: Идемпотентность / уважение явного выбора пользователя
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Верифицирует, что если у nextLocation-ROI уже выставлен непустой ParseHint
    /// (например, пользователь задал <c>"custom"</c>), повторный InitializeAsync его НЕ перезаписывает.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_ExistingNextLocationWithCustomHint_DoesNotOverwrite()
    {
        // Arrange: имитируем «явный выбор пользователя» — устанавливаем нестандартный хинт.
        // RoiCalibration.ParseHint объявлен init-only → используем bulk ExecuteUpdateAsync для Arrange.
        const string customHint = "custom";
        await Db.RoiCalibrations
            .Where(r => r.FieldKey == "nextLocation")
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ParseHint, customHint));

        // Убедимся, что хинт действительно "custom"
        RoiCalibration? before = await Db.RoiCalibrations
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.FieldKey == "nextLocation");

        before.Should().NotBeNull("ROI nextLocation должен существовать");
        before!.ParseHint.Should().Be(customHint, "подготовка: хинт должен быть 'custom'");

        // Act: повторный InitializeAsync
        await DatabaseInitializer.InitializeAsync(Db, CancellationToken.None);

        // Assert: хинт пользователя сохранён — НЕ перезаписан
        RoiCalibration? after = await Db.RoiCalibrations
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.FieldKey == "nextLocation");

        after.Should().NotBeNull();
        after!.ParseHint.Should().Be(
            customHint,
            "явно заданный ParseHint пользователя не должен перезаписываться при повторном Init");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Тест 5: Идемпотентность — повторный InitializeAsync не изменяет уже верный хинт
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Верифицирует, что повторный вызов InitializeAsync (следующий запуск приложения)
    /// не изменяет уже корректный ParseHint="binarize_white" у nextLocation.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_RepeatedInit_NextLocationHintRemainsUnchanged()
    {
        // Первый Init уже выполнен в фикстуре. Убеждаемся что хинт верный.
        RoiCalibration? nextLocBefore = await Db.RoiCalibrations
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.FieldKey == "nextLocation");

        nextLocBefore.Should().NotBeNull();
        nextLocBefore!.ParseHint.Should().Be("binarize_white");

        // Act: повторный Init
        await DatabaseInitializer.InitializeAsync(Db, CancellationToken.None);

        // Assert: хинт не изменился
        RoiCalibration? nextLocAfter = await Db.RoiCalibrations
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.FieldKey == "nextLocation");

        nextLocAfter.Should().NotBeNull();
        nextLocAfter!.ParseHint.Should().Be(
            "binarize_white",
            "повторный Init не должен менять уже корректный хинт");
    }
}
