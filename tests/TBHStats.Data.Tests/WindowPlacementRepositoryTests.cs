namespace TBHStats.Data.Tests;

using FluentAssertions;
using TBHStats.Core.Models;
using TBHStats.Data.Repositories;
using Xunit;

/// <summary>
/// Интеграционные тесты репозитория геометрии окон (<see cref="ISettingsRepository"/>):
/// GetWindowPlacementAsync / SaveWindowPlacementAsync на реальном временном SQLite-файле (FR-016).
/// </summary>
public sealed class WindowPlacementRepositoryTests : TempDbFixture
{
    private ISettingsRepository CreateRepo() => new SettingsRepository(Db);

    // ─────────────────────────────────────────────────────────────────────────
    // 1. Get при отсутствующей записи возвращает null
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetWindowPlacementAsync_NoRecord_ReturnsNull()
    {
        ISettingsRepository repo = CreateRepo();

        WindowPlacement? result = await repo.GetWindowPlacementAsync("compare");

        result.Should().BeNull("запись для ключа «compare» ещё не создавалась");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. Save затем Get по тому же ключу возвращает те же координаты
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveAndGet_RoundTripsCoordinates()
    {
        ISettingsRepository repo = CreateRepo();

        var placement = new WindowPlacement
        {
            WindowKey = "compare",
            PosX      = 120.5,
            PosY      = 80.0,
            Width     = 960.0,
            Height    = 540.0,
        };

        await repo.SaveWindowPlacementAsync(placement);

        // Act — читаем в новом контексте (симуляция «перезапуска»)
        await using var ctx2 = CreateContext();
        ISettingsRepository repo2 = new SettingsRepository(ctx2);
        WindowPlacement? loaded = await repo2.GetWindowPlacementAsync("compare");

        // Assert
        loaded.Should().NotBeNull();
        loaded!.WindowKey.Should().Be("compare");
        loaded.PosX.Should().BeApproximately(120.5, 1e-9);
        loaded.PosY.Should().BeApproximately(80.0, 1e-9);
        loaded.Width.Should().BeApproximately(960.0, 1e-9);
        loaded.Height.Should().BeApproximately(540.0, 1e-9);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. Upsert: повторный Save с тем же ключом обновляет, не дублирует
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveWindowPlacementAsync_Upsert_DoesNotCreateDuplicate()
    {
        ISettingsRepository repo = CreateRepo();

        var first = new WindowPlacement
        {
            WindowKey = "compare",
            PosX      = 10.0,
            PosY      = 20.0,
            Width     = 800.0,
            Height    = 600.0,
        };

        await repo.SaveWindowPlacementAsync(first);

        // Upsert — новые координаты, тот же ключ
        var updated = new WindowPlacement
        {
            WindowKey = "compare",
            PosX      = 200.0,
            PosY      = 150.0,
            Width     = 1280.0,
            Height    = 720.0,
        };

        await using var ctx2 = CreateContext();
        ISettingsRepository repo2 = new SettingsRepository(ctx2);
        await repo2.SaveWindowPlacementAsync(updated);

        // Assert — ровно одна запись с обновлёнными координатами
        await using var ctx3 = CreateContext();
        int count = ctx3.WindowPlacements.Count(p => p.WindowKey == "compare");
        count.Should().Be(1, "upsert не должен создавать дубликат");

        ISettingsRepository repo3 = new SettingsRepository(ctx3);
        WindowPlacement? loaded = await repo3.GetWindowPlacementAsync("compare");
        loaded.Should().NotBeNull();
        loaded!.PosX.Should().BeApproximately(200.0, 1e-9, "PosX должен обновиться до нового значения");
        loaded.PosY.Should().BeApproximately(150.0, 1e-9);
        loaded.Width.Should().BeApproximately(1280.0, 1e-9);
        loaded.Height.Should().BeApproximately(720.0, 1e-9);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. Несколько окон хранятся независимо по разным ключам
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveMultipleWindowKeys_AreStoredIndependently()
    {
        ISettingsRepository repo = CreateRepo();

        var compareWin = new WindowPlacement
        {
            WindowKey = "compare",
            PosX = 0.0, PosY = 0.0, Width = 900.0, Height = 500.0,
        };
        var chartsWin = new WindowPlacement
        {
            WindowKey = "charts",
            PosX = 100.0, PosY = 50.0, Width = 1200.0, Height = 700.0,
        };

        await repo.SaveWindowPlacementAsync(compareWin);

        await using var ctx2 = CreateContext();
        ISettingsRepository repo2 = new SettingsRepository(ctx2);
        await repo2.SaveWindowPlacementAsync(chartsWin);

        await using var ctx3 = CreateContext();
        ISettingsRepository repo3 = new SettingsRepository(ctx3);

        WindowPlacement? loadedCompare = await repo3.GetWindowPlacementAsync("compare");
        WindowPlacement? loadedCharts  = await repo3.GetWindowPlacementAsync("charts");
        WindowPlacement? loadedMissing = await repo3.GetWindowPlacementAsync("nonexistent");

        loadedCompare.Should().NotBeNull();
        loadedCompare!.Width.Should().BeApproximately(900.0, 1e-9);

        loadedCharts.Should().NotBeNull();
        loadedCharts!.Width.Should().BeApproximately(1200.0, 1e-9);

        loadedMissing.Should().BeNull("ключ «nonexistent» не сохранялся");
    }
}
