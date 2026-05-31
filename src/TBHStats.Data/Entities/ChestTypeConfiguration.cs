namespace TBHStats.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TBHStats.Core.Models;

/// <summary>
/// Fluent-конфигурация EF Core для <see cref="ChestType"/> (ADR-009).
/// </summary>
internal sealed class ChestTypeConfiguration : IEntityTypeConfiguration<ChestType>
{
    public void Configure(EntityTypeBuilder<ChestType> builder)
    {
        builder.ToTable("ChestTypes");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.Key)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(e => e.DisplayName)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(e => e.ColorLabel)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(e => e.SortOrder).IsRequired();
        builder.Property(e => e.IsActive).IsRequired();

        builder.HasIndex(e => e.Key).IsUnique();
    }
}
