namespace TBHStats.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TBHStats.Core.Models;

/// <summary>
/// Fluent-конфигурация EF Core для <see cref="StageAggregate"/> (data-model §StageAggregate).
/// StageId — одновременно PK и FK → Stage (отношение 1:1).
/// </summary>
internal sealed class StageAggregateConfiguration : IEntityTypeConfiguration<StageAggregate>
{
    public void Configure(EntityTypeBuilder<StageAggregate> builder)
    {
        builder.ToTable("StageAggregates");

        // StageId — PK (одновременно FK → Stage, 1:1).
        builder.HasKey(e => e.StageId);
        builder.Property(e => e.StageId).ValueGeneratedNever();

        builder.Property(e => e.RunCount).IsRequired();
        builder.Property(e => e.AvgGoldPerHour).IsRequired();
        builder.Property(e => e.BestGoldPerHour).IsRequired();
        builder.Property(e => e.AvgGoldGained).IsRequired();
        builder.Property(e => e.AvgXpPerHour).IsRequired();
        builder.Property(e => e.BestXpPerHour).IsRequired();
        builder.Property(e => e.AvgXpGained).IsRequired();
        builder.Property(e => e.AvgDurationSeconds).IsRequired();
        builder.Property(e => e.BestDurationSeconds).IsRequired();
        builder.Property(e => e.UpdatedAtUtc).IsRequired();

        // ──────────── Recency-aware поля (recent window) ────────────
        builder.Property(e => e.RecentRunCount).IsRequired();
        builder.Property(e => e.RecentAvgGoldPerHour).IsRequired();
        builder.Property(e => e.RecentBestGoldPerHour).IsRequired();
        builder.Property(e => e.RecentAvgGoldGained).IsRequired();
        builder.Property(e => e.RecentAvgXpPerHour).IsRequired();
        builder.Property(e => e.RecentBestXpPerHour).IsRequired();
        builder.Property(e => e.RecentAvgXpGained).IsRequired();
        builder.Property(e => e.RecentAvgDurationSeconds).IsRequired();
        builder.Property(e => e.RecentBestDurationSeconds).IsRequired();

        // Power-context nullable поля (null если RecentRunCount = 0).
        builder.Property(e => e.RecentHeroLevelMin).IsRequired(false);
        builder.Property(e => e.RecentHeroLevelMax).IsRequired(false);
        builder.Property(e => e.RecentHeroDamageMin).IsRequired(false);
        builder.Property(e => e.RecentHeroDamageMax).IsRequired(false);

        builder.HasOne<Stage>()
            .WithOne()
            .HasForeignKey<StageAggregate>(e => e.StageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.ChestRates)
            .WithOne()
            .HasForeignKey(r => r.StageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
