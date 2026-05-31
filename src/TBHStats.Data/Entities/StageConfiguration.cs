namespace TBHStats.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TBHStats.Core.Models;

/// <summary>
/// Fluent-конфигурация EF Core для <see cref="Stage"/> (ADR-008, data-model §Stage).
/// Уникальный составной индекс: (ActId, DifficultyId, Number).
/// </summary>
internal sealed class StageConfiguration : IEntityTypeConfiguration<Stage>
{
    public void Configure(EntityTypeBuilder<Stage> builder)
    {
        builder.ToTable("Stages");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.ActId).IsRequired();
        builder.Property(e => e.DifficultyId).IsRequired();
        builder.Property(e => e.Number).IsRequired();

        builder.HasOne<Act>()
            .WithMany()
            .HasForeignKey(e => e.ActId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Difficulty>()
            .WithMany()
            .HasForeignKey(e => e.DifficultyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Один и тот же номер этапа на разных актах/сложностях — это разные этапы (ADR-008 edge case).
        builder.HasIndex(e => new { e.ActId, e.DifficultyId, e.Number }).IsUnique();
    }
}
