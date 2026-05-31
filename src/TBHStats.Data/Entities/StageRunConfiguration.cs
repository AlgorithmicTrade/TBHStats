namespace TBHStats.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TBHStats.Core.Models;

/// <summary>
/// Fluent-конфигурация EF Core для <see cref="StageRun"/> (data-model §StageRun).
/// HeroSnapshot встраивается как owned entity.
/// Вычисляемые GoldPerHour / XpPerHour игнорируются.
/// </summary>
internal sealed class StageRunConfiguration : IEntityTypeConfiguration<StageRun>
{
    public void Configure(EntityTypeBuilder<StageRun> builder)
    {
        builder.ToTable("StageRuns");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.StageId).IsRequired();
        builder.Property(e => e.DurationSeconds).IsRequired();
        builder.Property(e => e.GoldGained).IsRequired();
        builder.Property(e => e.XpGained).IsRequired();
        builder.Property(e => e.CompletedAtUtc).IsRequired();
        builder.Property(e => e.IsPartial).IsRequired();

        builder.HasOne<Stage>()
            .WithMany()
            .HasForeignKey(e => e.StageId)
            .OnDelete(DeleteBehavior.Restrict);

        // HeroSnapshot — owned value-object, встраивается в строку StageRuns.
        // Используем явный маппинг свойств, чтобы обойти guard-валидацию в init-свойствах
        // record-типа при материализации EF (конструктор с throw не вызывается при Property-маппинге).
        builder.OwnsOne(e => e.Hero, hero =>
        {
            hero.Property(h => h.HeroClassId)
                .HasColumnName("Hero_HeroClassId")
                .IsRequired();

            hero.Property(h => h.Level)
                .HasColumnName("Hero_Level")
                .IsRequired();

            hero.Property(h => h.Damage)
                .HasColumnName("Hero_Damage")
                .IsRequired();

            // FK к HeroClass — хранится только id, без nav-свойства на уровне owned.
            hero.HasOne<HeroClass>()
                .WithMany()
                .HasForeignKey(h => h.HeroClassId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Вычисляемые get-only свойства — не персистируются.
        builder.Ignore(e => e.GoldPerHour);
        builder.Ignore(e => e.XpPerHour);

        builder.HasMany(e => e.Chests)
            .WithOne(c => c.Run)
            .HasForeignKey(c => c.StageRunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
