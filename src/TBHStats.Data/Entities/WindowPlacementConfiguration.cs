namespace TBHStats.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TBHStats.Core.Models;

/// <summary>
/// Fluent-конфигурация EF Core для <see cref="WindowPlacement"/> (FR-016).
/// Ключ — строковое поле <see cref="WindowPlacement.WindowKey"/> («compare», «charts», …).
/// Одна запись на окно; upsert выполняется репозиторием через Add/Modify.
/// </summary>
internal sealed class WindowPlacementConfiguration : IEntityTypeConfiguration<WindowPlacement>
{
    public void Configure(EntityTypeBuilder<WindowPlacement> builder)
    {
        builder.ToTable("WindowPlacement");

        // Реальный string PK — WindowKey («compare», «charts», «calibration», …).
        builder.HasKey(e => e.WindowKey);

        builder.Property(e => e.WindowKey)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(e => e.PosX).IsRequired();
        builder.Property(e => e.PosY).IsRequired();
        builder.Property(e => e.Width).IsRequired();
        builder.Property(e => e.Height).IsRequired();
    }
}
