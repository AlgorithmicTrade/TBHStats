namespace TBHStats.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TBHStats.Core.Models;

/// <summary>
/// Fluent-конфигурация EF Core для <see cref="Difficulty"/> (ADR-008).
/// </summary>
internal sealed class DifficultyConfiguration : IEntityTypeConfiguration<Difficulty>
{
    public void Configure(EntityTypeBuilder<Difficulty> builder)
    {
        builder.ToTable("Difficulties");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.Key)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(e => e.DisplayName)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(e => e.SortOrder).IsRequired();

        builder.HasIndex(e => e.Key).IsUnique();
    }
}
