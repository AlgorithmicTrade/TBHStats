namespace TBHStats.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TBHStats.Core.Models;

/// <summary>
/// Fluent-конфигурация EF Core для <see cref="MetricSampleChest"/> (data-model §MetricSample).
/// Составной PK: (MetricSampleId, ChestTypeId).
/// </summary>
internal sealed class MetricSampleChestConfiguration : IEntityTypeConfiguration<MetricSampleChest>
{
    public void Configure(EntityTypeBuilder<MetricSampleChest> builder)
    {
        builder.ToTable("MetricSampleChests");

        builder.HasKey(e => new { e.MetricSampleId, e.ChestTypeId });

        builder.Property(e => e.MetricSampleId).IsRequired();
        builder.Property(e => e.ChestTypeId).IsRequired();
        builder.Property(e => e.Count).IsRequired();

        builder.HasOne<ChestType>()
            .WithMany()
            .HasForeignKey(e => e.ChestTypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
