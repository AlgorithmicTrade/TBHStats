namespace TBHStats.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TBHStats.Core.Models;

/// <summary>
/// Fluent-конфигурация EF Core для <see cref="StageRunChest"/> (data-model §StageRunChest).
/// Составной PK: (StageRunId, ChestTypeId).
/// </summary>
internal sealed class StageRunChestConfiguration : IEntityTypeConfiguration<StageRunChest>
{
    public void Configure(EntityTypeBuilder<StageRunChest> builder)
    {
        builder.ToTable("StageRunChests");

        builder.HasKey(e => new { e.StageRunId, e.ChestTypeId });

        builder.Property(e => e.StageRunId).IsRequired();
        builder.Property(e => e.ChestTypeId).IsRequired();
        builder.Property(e => e.Count).IsRequired();

        // FK к ChestType — без nav-свойства в модели (только id).
        builder.HasOne<ChestType>()
            .WithMany()
            .HasForeignKey(e => e.ChestTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK к StageRun — с nav-свойством Run (настроен в StageRunConfiguration.HasMany).
    }
}
