namespace TBHStats.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TBHStats.Core.Models;

/// <summary>
/// Fluent-конфигурация EF Core для <see cref="OptimizationProfile"/> (data-model §OptimizationProfile, FR-009).
/// Синглтон-запись: суррогатный shadow PK «Id» со значением по умолчанию 1.
/// SelectedMetric хранится как int.
/// </summary>
internal sealed class OptimizationProfileConfiguration : IEntityTypeConfiguration<OptimizationProfile>
{
    public void Configure(EntityTypeBuilder<OptimizationProfile> builder)
    {
        builder.ToTable("OptimizationProfiles");

        // Синглтон: суррогатный shadow PK.
        builder.Property<int>("Id").ValueGeneratedOnAdd();
        builder.HasKey("Id");

        builder.Property(e => e.SelectedMetric)
            .IsRequired()
            .HasConversion<int>();
    }
}
