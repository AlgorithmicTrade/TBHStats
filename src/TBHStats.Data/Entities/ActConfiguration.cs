namespace TBHStats.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TBHStats.Core.Models;

/// <summary>
/// Fluent-конфигурация EF Core для <see cref="Act"/> (ADR-008).
/// </summary>
internal sealed class ActConfiguration : IEntityTypeConfiguration<Act>
{
    public void Configure(EntityTypeBuilder<Act> builder)
    {
        builder.ToTable("Acts");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.Number).IsRequired();

        builder.Property(e => e.DisplayName)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(e => e.SortOrder).IsRequired();
    }
}
