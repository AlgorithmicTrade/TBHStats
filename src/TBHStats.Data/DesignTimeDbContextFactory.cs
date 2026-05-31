namespace TBHStats.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

/// <summary>
/// Фабрика контекста EF Core для design-time (dotnet ef migrations add).
/// Создаёт <see cref="TbhStatsDbContext"/> с временной SQLite-строкой подключения,
/// которая используется исключительно инструментом миграций — не рантаймом.
/// </summary>
/// <remarks>
/// Рантаймовый путь БД вычисляется через <see cref="DatabaseInitializer.GetDbPath"/>
/// и передаётся при регистрации DbContext в composition root (TBHStats.App).
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TbhStatsDbContext>
{
    /// <inheritdoc />
    public TbhStatsDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<TbhStatsDbContext> options = new DbContextOptionsBuilder<TbhStatsDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        return new TbhStatsDbContext(options);
    }
}
