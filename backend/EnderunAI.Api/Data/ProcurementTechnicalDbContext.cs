using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Data;

public sealed class ProcurementTechnicalDbContext(DbContextOptions<ProcurementTechnicalDbContext> options) : DbContext(options)
{
    public DbSet<TechnicalSpecification> Specifications => Set<TechnicalSpecification>();
    public DbSet<TechnicalCriterion> Criteria => Set<TechnicalCriterion>();
    public DbSet<SupplierOfferTechnicalResponse> Responses => Set<SupplierOfferTechnicalResponse>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TechnicalSpecification>(entity =>
        {
            entity.ToTable("procurement_technical_specifications");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(250).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.HasMany(x => x.Criteria).WithOne(x => x.TechnicalSpecification).HasForeignKey(x => x.TechnicalSpecificationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<TechnicalCriterion>(entity =>
        {
            entity.ToTable("procurement_technical_criteria");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TechnicalSpecificationId, x.Code }).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(250).IsRequired();
            entity.Property(x => x.ExpectedValue).HasMaxLength(1000);
            entity.Property(x => x.NumericValue).HasPrecision(18, 4);
            entity.Property(x => x.Unit).HasMaxLength(30);
            entity.Property(x => x.Weight).HasPrecision(10, 4);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<SupplierOfferTechnicalResponse>(entity =>
        {
            entity.ToTable("procurement_offer_technical_responses");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.SupplierOfferItemId, x.TechnicalCriterionId }).IsUnique();
            entity.Property(x => x.OfferedValue).HasMaxLength(1000);
            entity.Property(x => x.OfferedNumericValue).HasPrecision(18, 4);
            entity.Property(x => x.EvidenceReference).HasMaxLength(1000);
            entity.Property(x => x.Score).HasPrecision(10, 2);
            entity.Property(x => x.EvaluationNote).HasMaxLength(1000);
            entity.Property(x => x.EvaluatedByName).HasMaxLength(200);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }
}
