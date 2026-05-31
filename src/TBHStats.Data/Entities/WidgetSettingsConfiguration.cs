namespace TBHStats.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TBHStats.Core.Models;

/// <summary>
/// Fluent-конфигурация EF Core для <see cref="WidgetSettings"/> (data-model §WidgetSettings).
/// Синглтон-запись: суррогатный shadow PK «Id» со значением по умолчанию 1.
/// Theme хранится как int.
/// </summary>
internal sealed class WidgetSettingsConfiguration : IEntityTypeConfiguration<WidgetSettings>
{
    public void Configure(EntityTypeBuilder<WidgetSettings> builder)
    {
        builder.ToTable("WidgetSettings");

        // Синглтон: суррогатный shadow PK (не нужен в доменной модели).
        builder.Property<int>("Id").ValueGeneratedOnAdd();
        builder.HasKey("Id");

        builder.Property(e => e.PosX).IsRequired();
        builder.Property(e => e.PosY).IsRequired();
        builder.Property(e => e.Width).IsRequired();
        builder.Property(e => e.Height).IsRequired();
        builder.Property(e => e.AlwaysOnTop).IsRequired();

        builder.Property(e => e.Theme)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(e => e.PollIntervalMs).IsRequired();
    }
}
