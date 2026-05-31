namespace TBHStats.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TBHStats.Core.Models;

/// <summary>
/// Fluent-конфигурация EF Core для <see cref="RoiCalibration"/> (data-model §RoiCalibration, R2, R3).
/// Enum'ы Source и OcrEngine хранятся как int.
/// </summary>
internal sealed class RoiCalibrationConfiguration : IEntityTypeConfiguration<RoiCalibration>
{
    public void Configure(EntityTypeBuilder<RoiCalibration> builder)
    {
        builder.ToTable("RoiCalibrations");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.FieldKey)
            .IsRequired()
            .HasMaxLength(128);

        // Enum как int (по умолчанию EF так и делает, но явно фиксируем конвенцию).
        builder.Property(e => e.Source)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(e => e.TabId); // nullable

        builder.Property(e => e.X).IsRequired();
        builder.Property(e => e.Y).IsRequired();
        builder.Property(e => e.W).IsRequired();
        builder.Property(e => e.H).IsRequired();

        builder.Property(e => e.OcrEngine)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(e => e.ParseHint).HasMaxLength(256); // nullable

        builder.HasOne<Tab>()
            .WithMany()
            .HasForeignKey(e => e.TabId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
