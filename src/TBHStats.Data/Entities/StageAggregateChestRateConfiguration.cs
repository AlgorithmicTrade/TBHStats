namespace TBHStats.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TBHStats.Core.Models;

/// <summary>
/// Fluent-конфигурация EF Core для <see cref="StageAggregateChestRate"/> (data-model §StageAggregate).
/// Составной PK: (StageId, ChestTypeId).
/// </summary>
internal sealed class StageAggregateChestRateConfiguration : IEntityTypeConfiguration<StageAggregateChestRate>
{
    public void Configure(EntityTypeBuilder<StageAggregateChestRate> builder)
    {
        builder.ToTable("StageAggregateChestRates");

        builder.HasKey(e => new { e.StageId, e.ChestTypeId });

        builder.Property(e => e.StageId).IsRequired();
        builder.Property(e => e.ChestTypeId).IsRequired();
        builder.Property(e => e.RatePerHour).IsRequired();
        builder.Property(e => e.RecentRatePerHour).IsRequired();

        builder.HasOne<ChestType>()
            .WithMany()
            .HasForeignKey(e => e.ChestTypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
