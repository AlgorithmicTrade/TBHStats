namespace TBHStats.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TBHStats.Core.Models;

/// <summary>
/// Fluent-конфигурация EF Core для <see cref="HeroClass"/> (ADR-009).
/// </summary>
internal sealed class HeroClassConfiguration : IEntityTypeConfiguration<HeroClass>
{
    public void Configure(EntityTypeBuilder<HeroClass> builder)
    {
        builder.ToTable("HeroClasses");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.Key)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(e => e.DisplayName)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(e => e.IsActive).IsRequired();

        builder.HasIndex(e => e.Key).IsUnique();
    }
}
