using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Data;

public sealed class ProcurementDbContext(DbContextOptions<ProcurementDbContext> options)
    : DbContext(options)
{
    public DbSet<Rfq> Rfqs => Set<Rfq>();
    public DbSet<RfqItem> RfqItems => Set<RfqItem>();
    public DbSet<SupplierOffer> SupplierOffers => Set<SupplierOffer>();
    public DbSet<SupplierOfferItem> SupplierOfferItems => Set<SupplierOfferItem>();
    public DbSet<SupplierOfferCheckTerm> SupplierOfferCheckTerms => Set<SupplierOfferCheckTerm>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Rfq>(entity =>
        {
            entity.ToTable("rfqs");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CompanyId, x.RfqNumber }).IsUnique();
            entity.Property(x => x.RfqNumber).HasMaxLength(80).IsRequired();
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<RfqItem>(entity =>
        {
            entity.ToTable("rfq_items");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Quantity).HasPrecision(18, 4);
            entity.Property(x => x.Unit).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.HasOne(x => x.Rfq)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.RfqId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<SupplierOffer>(entity =>
        {
            entity.ToTable("supplier_offers");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.RfqId, x.SupplierCurrentAccountId, x.OfferNumber }).IsUnique();
            entity.Property(x => x.OfferNumber).HasMaxLength(80).IsRequired();
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.ExchangeRate).HasPrecision(18, 6);
            entity.Property(x => x.DiscountRate).HasPrecision(5, 2);
            entity.Property(x => x.FreightAmount).HasPrecision(18, 2);
            entity.Property(x => x.SupplierPerformanceScore).HasPrecision(5, 2);
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.HasOne(x => x.Rfq)
                .WithMany(x => x.Offers)
                .HasForeignKey(x => x.RfqId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<SupplierOfferItem>(entity =>
        {
            entity.ToTable("supplier_offer_items");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.OfferedQuantity).HasPrecision(18, 4);
            entity.Property(x => x.AvailableStockQuantity).HasPrecision(18, 4);
            entity.Property(x => x.UnitPrice).HasPrecision(18, 4);
            entity.HasOne(x => x.SupplierOffer)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.SupplierOfferId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<SupplierOfferCheckTerm>(entity =>
        {
            entity.ToTable("supplier_offer_check_terms");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.HasOne(x => x.SupplierOffer)
                .WithMany(x => x.CheckTerms)
                .HasForeignKey(x => x.SupplierOfferId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }
}
