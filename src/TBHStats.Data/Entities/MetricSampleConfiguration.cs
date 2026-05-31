namespace TBHStats.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TBHStats.Core.Models;

/// <summary>
/// Fluent-конфигурация EF Core для <see cref="MetricSample"/> (data-model §MetricSample).
/// NextLocation (<see cref="StageRef?"/>) хранится в одной TEXT-колонке через <see cref="ValueConverter{TModel,TProvider}"/>:
/// формат «{ActNumber}/{DifficultyKey}/{StageNumber}» или NULL.
/// Это позволяет избежать проблем с materialisation EF для readonly record struct с guard-валидацией.
/// </summary>
internal sealed class MetricSampleConfiguration : IEntityTypeConfiguration<MetricSample>
{
    // Конвертер StageRef? ↔ string?  ("1/normal/5" | null)
    private static readonly ValueConverter<StageRef?, string?> StageRefConverter =
        new(
            model => model == null
                ? null
                : $"{model.Value.ActNumber}/{model.Value.DifficultyKey}/{model.Value.StageNumber}",
            raw => raw == null
                ? (StageRef?)null
                : Parse(raw));

    private static StageRef Parse(string raw)
    {
        string[] parts = raw.Split('/');
        return new StageRef(
            ActNumber:     int.Parse(parts[0]),
            DifficultyKey: parts[1],
            StageNumber:   int.Parse(parts[2]));
    }

    public void Configure(EntityTypeBuilder<MetricSample> builder)
    {
        builder.ToTable("MetricSamples");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.TakenAtUtc).IsRequired();
        builder.Property(e => e.StageId);       // nullable FK
        builder.Property(e => e.Gold);           // nullable
        builder.Property(e => e.Xp);             // nullable
        builder.Property(e => e.XpToLevel);      // nullable
        builder.Property(e => e.IsReliable).IsRequired();
        builder.Property(e => e.HeroLevel);      // nullable
        builder.Property(e => e.HeroDamage);     // nullable

        builder.HasOne<Stage>()
            .WithMany()
            .HasForeignKey(e => e.StageId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // StageRef? хранится как «ActNumber/DifficultyKey/StageNumber» TEXT или NULL.
        // HasConversion обходит ограничения EF при маппинге readonly record struct с guard-init.
        builder.Property(e => e.NextLocation)
            .HasColumnName("NextLocation")
            .HasMaxLength(128)
            .HasConversion(StageRefConverter);

        builder.HasMany(e => e.Chests)
            .WithOne()
            .HasForeignKey(c => c.MetricSampleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
