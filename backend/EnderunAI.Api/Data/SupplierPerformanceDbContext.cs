using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Data;

public sealed class SupplierPerformanceDbContext(DbContextOptions<SupplierPerformanceDbContext> options)
    : DbContext(options)
{
    public DbSet<SupplierPerformanceSnapshot> Snapshots => Set<SupplierPerformanceSnapshot>();
    public DbSet<SupplierQualityRecord> QualityRecords => Set<SupplierQualityRecord>();
    public DbSet<SupplierManualEvaluation> ManualEvaluations => Set<SupplierManualEvaluation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SupplierPerformanceSnapshot>(entity =>
        {
            entity.ToTable("supplier_performance_snapshots");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CompanyId, x.SupplierCurrentAccountId, x.PeriodEndUtc });
            entity.Property(x => x.DeliveryScore).HasPrecision(5, 2);
            entity.Property(x => x.QualityScore).HasPrecision(5, 2);
            entity.Property(x => x.PriceScore).HasPrecision(5, 2);
            entity.Property(x => x.TechnicalScore).HasPrecision(5, 2);
            entity.Property(x => x.FinancialScore).HasPrecision(5, 2);
            entity.Property(x => x.CommunicationScore).HasPrecision(5, 2);
            entity.Property(x => x.OverallScore).HasPrecision(5, 2);
            entity.Property(x => x.TotalOrderAmountTry).HasPrecision(18, 2);
            entity.Property(x => x.OnTimeDeliveryRate).HasPrecision(5, 2);
            entity.Property(x => x.ReturnRate).HasPrecision(5, 2);
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<SupplierQualityRecord>(entity =>
        {
            entity.ToTable("supplier_quality_records");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CompanyId, x.SupplierCurrentAccountId, x.EventDateUtc });
            entity.Property(x => x.Quantity).HasPrecision(18, 4);
            entity.Property(x => x.ImpactScore).HasPrecision(5, 2);
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.CreatedByName).HasMaxLength(200).IsRequired();
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<SupplierManualEvaluation>(entity =>
        {
            entity.ToTable("supplier_manual_evaluations");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CompanyId, x.SupplierCurrentAccountId, x.EvaluationDateUtc });
            entity.Property(x => x.CommunicationScore).HasPrecision(5, 2);
            entity.Property(x => x.FinancialScore).HasPrecision(5, 2);
            entity.Property(x => x.QualityScore).HasPrecision(5, 2);
            entity.Property(x => x.TechnicalScore).HasPrecision(5, 2);
            entity.Property(x => x.Comment).HasMaxLength(1000);
            entity.Property(x => x.EvaluatedByName).HasMaxLength(200).IsRequired();
            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }
}
