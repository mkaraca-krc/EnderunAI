using EnderunAI.Api.Models.GoodsReceipt;
using EnderunAI.Api.Models.PurchaseOrder;
using EnderunAI.Api.Models.Rfq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnderunAI.Api.Data.Configurations;

public static class ProcurementModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new RfqConfiguration());
        modelBuilder.ApplyConfiguration(new RfqItemConfiguration());
        modelBuilder.ApplyConfiguration(new RfqSupplierConfiguration());
        modelBuilder.ApplyConfiguration(new RfqSupplierQuotationConfiguration());
        modelBuilder.ApplyConfiguration(new RfqSupplierQuotationItemConfiguration());
        modelBuilder.ApplyConfiguration(new PurchaseOrderConfiguration());
        modelBuilder.ApplyConfiguration(new PurchaseOrderItemConfiguration());
        modelBuilder.ApplyConfiguration(new GoodsReceiptConfiguration());
        modelBuilder.ApplyConfiguration(new GoodsReceiptItemConfiguration());
    }
}

public sealed class RfqConfiguration : IEntityTypeConfiguration<Rfq>
{
    public void Configure(EntityTypeBuilder<Rfq> entity)
    {
        entity.ToTable("rfqs");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.CompanyId, x.RfqNumber }).IsUnique();
        entity.HasIndex(x => x.PurchaseRequestId);
        entity.Property(x => x.RfqNumber).HasMaxLength(50).IsRequired();
        entity.Property(x => x.Title).HasMaxLength(300).IsRequired();
        entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        entity.Property(x => x.Description).HasMaxLength(1000);
        entity.Property(x => x.Notes).HasMaxLength(2000);
        entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.PurchaseRequest).WithMany().HasForeignKey(x => x.PurchaseRequestId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => !x.IsDeleted);
    }
}

public sealed class RfqItemConfiguration : IEntityTypeConfiguration<RfqItem>
{
    public void Configure(EntityTypeBuilder<RfqItem> entity)
    {
        entity.ToTable("rfq_items");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.RfqId, x.LineNumber }).IsUnique();
        entity.HasIndex(x => x.PurchaseRequestItemId);
        entity.Property(x => x.MaterialDescription).HasMaxLength(500).IsRequired();
        entity.Property(x => x.Quantity).HasPrecision(18, 4);
        entity.Property(x => x.Unit).HasMaxLength(40).IsRequired();
        entity.Property(x => x.Notes).HasMaxLength(1000);
        entity.HasOne(x => x.Rfq).WithMany(x => x.Items).HasForeignKey(x => x.RfqId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(x => x.PurchaseRequestItem).WithMany()
            .HasForeignKey(x => x.PurchaseRequestItemId)
            .OnDelete(DeleteBehavior.SetNull);
        entity.HasQueryFilter(x => !x.IsDeleted);
    }
}

public sealed class RfqSupplierConfiguration : IEntityTypeConfiguration<RfqSupplier>
{
    public void Configure(EntityTypeBuilder<RfqSupplier> entity)
    {
        entity.ToTable("rfq_suppliers");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.RfqId, x.SupplierCurrentAccountId }).IsUnique();
        entity.HasIndex(x => x.SupplierCurrentAccountId);
        entity.Property(x => x.ContactName).HasMaxLength(200);
        entity.Property(x => x.ContactEmail).HasMaxLength(200);
        entity.Property(x => x.Notes).HasMaxLength(1000);
        entity.HasOne(x => x.Rfq).WithMany(x => x.Suppliers).HasForeignKey(x => x.RfqId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(x => x.SupplierCurrentAccount).WithMany()
            .HasForeignKey(x => x.SupplierCurrentAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => !x.IsDeleted);
    }
}

public sealed class RfqSupplierQuotationConfiguration :
    IEntityTypeConfiguration<RfqSupplierQuotation>
{
    public void Configure(EntityTypeBuilder<RfqSupplierQuotation> entity)
    {
        entity.ToTable("rfq_supplier_quotations");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => x.RfqSupplierId);
        entity.Property(x => x.SupplierQuotationNumber).HasMaxLength(100);
        entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        entity.Property(x => x.ExchangeRate).HasPrecision(18, 6);
        entity.Property(x => x.PaymentTerm).HasMaxLength(200);
        entity.Property(x => x.Subtotal).HasPrecision(18, 2);
        entity.Property(x => x.DiscountTotal).HasPrecision(18, 2);
        entity.Property(x => x.GrandTotal).HasPrecision(18, 2);
        entity.Property(x => x.Notes).HasMaxLength(2000);
        entity.HasOne(x => x.RfqSupplier).WithMany(x => x.Quotations)
            .HasForeignKey(x => x.RfqSupplierId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasQueryFilter(x => !x.IsDeleted);
    }
}

public sealed class RfqSupplierQuotationItemConfiguration :
    IEntityTypeConfiguration<RfqSupplierQuotationItem>
{
    public void Configure(EntityTypeBuilder<RfqSupplierQuotationItem> entity)
    {
        entity.ToTable("rfq_supplier_quotation_items");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.RfqSupplierQuotationId, x.RfqItemId }).IsUnique();
        entity.HasIndex(x => x.RfqItemId);
        entity.Property(x => x.Quantity).HasPrecision(18, 4);
        entity.Property(x => x.UnitPrice).HasPrecision(18, 4);
        entity.Property(x => x.DiscountRate).HasPrecision(8, 4);
        entity.Property(x => x.NetUnitPrice).HasPrecision(18, 4);
        entity.Property(x => x.TotalPrice).HasPrecision(18, 2);
        entity.Property(x => x.Brand).HasMaxLength(150);
        entity.Property(x => x.Model).HasMaxLength(150);
        entity.Property(x => x.Notes).HasMaxLength(1000);
        entity.HasOne(x => x.RfqSupplierQuotation).WithMany(x => x.Items)
            .HasForeignKey(x => x.RfqSupplierQuotationId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(x => x.RfqItem).WithMany(x => x.QuotationItems)
            .HasForeignKey(x => x.RfqItemId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => !x.IsDeleted);
    }
}

public sealed class PurchaseOrderConfiguration :
    IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> entity)
    {
        entity.ToTable("purchase_orders");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.CompanyId, x.OrderNumber }).IsUnique();
        entity.HasIndex(x => x.ProjectId);
        entity.HasIndex(x => x.RfqId);
        entity.HasIndex(x => x.SupplierCurrentAccountId);
        entity.Property(x => x.OrderNumber).HasMaxLength(50).IsRequired();
        entity.Property(x => x.Currency).HasMaxLength(10).IsRequired();
        entity.Property(x => x.ExchangeRate).HasPrecision(18, 6);
        entity.Property(x => x.PaymentTerm).HasMaxLength(250);
        entity.Property(x => x.DeliveryAddress).HasMaxLength(1000);
        entity.Property(x => x.Description).HasMaxLength(1000);
        entity.Property(x => x.Notes).HasMaxLength(2000);
        entity.Property(x => x.Subtotal).HasPrecision(18, 2);
        entity.Property(x => x.DiscountTotal).HasPrecision(18, 2);
        entity.Property(x => x.GrandTotal).HasPrecision(18, 2);
        entity.Property(x => x.VatRate).HasPrecision(5, 2);
        entity.Property(x => x.VatAmount).HasPrecision(18, 2);
        entity.Property(x => x.CancellationReason).HasMaxLength(1000);
        entity.Property(x => x.RejectionReason).HasMaxLength(1000);
        entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.Rfq).WithMany().HasForeignKey(x => x.RfqId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.SupplierCurrentAccount).WithMany()
            .HasForeignKey(x => x.SupplierCurrentAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => !x.IsDeleted);
    }
}

public sealed class PurchaseOrderItemConfiguration :
    IEntityTypeConfiguration<PurchaseOrderItem>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderItem> entity)
    {
        entity.ToTable("purchase_order_items");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.PurchaseOrderId, x.LineNumber }).IsUnique();
        entity.HasIndex(x => x.RfqItemId);
        entity.HasIndex(x => x.RfqSupplierQuotationItemId);
        entity.Property(x => x.MaterialDescription).HasMaxLength(500).IsRequired();
        entity.Property(x => x.Brand).HasMaxLength(150);
        entity.Property(x => x.Model).HasMaxLength(150);
        entity.Property(x => x.Quantity).HasPrecision(18, 4);
        entity.Property(x => x.ReceivedQuantity).HasPrecision(18, 4);
        entity.Property(x => x.Unit).HasMaxLength(30).IsRequired();
        entity.Property(x => x.UnitPrice).HasPrecision(18, 4);
        entity.Property(x => x.DiscountRate).HasPrecision(8, 4);
        entity.Property(x => x.NetUnitPrice).HasPrecision(18, 4);
        entity.Property(x => x.TotalPrice).HasPrecision(18, 2);
        entity.Property(x => x.Notes).HasMaxLength(1000);
        entity.HasOne(x => x.PurchaseOrder).WithMany(x => x.Items)
            .HasForeignKey(x => x.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(x => x.RfqItem).WithMany().HasForeignKey(x => x.RfqItemId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.RfqSupplierQuotationItem).WithMany()
            .HasForeignKey(x => x.RfqSupplierQuotationItemId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => !x.IsDeleted);
    }
}

public sealed class GoodsReceiptConfiguration :
    IEntityTypeConfiguration<GoodsReceipt>
{
    public void Configure(EntityTypeBuilder<GoodsReceipt> entity)
    {
        entity.ToTable("goods_receipts");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.CompanyId, x.ReceiptNumber }).IsUnique();
        entity.HasIndex(x => new { x.PurchaseOrderId, x.ReceiptDate });
        entity.HasIndex(x => x.WarehouseId);
        entity.Property(x => x.ReceiptNumber).HasMaxLength(50).IsRequired();
        entity.Property(x => x.DispatchNoteNumber).HasMaxLength(100);
        entity.Property(x => x.InvoiceNumber).HasMaxLength(100);
        entity.Property(x => x.ReceivedByName).HasMaxLength(200).IsRequired();
        entity.Property(x => x.VehiclePlate).HasMaxLength(30);
        entity.Property(x => x.DriverName).HasMaxLength(200);
        entity.Property(x => x.Description).HasMaxLength(1000);
        entity.Property(x => x.Notes).HasMaxLength(2000);
        entity.Property(x => x.CancellationReason).HasMaxLength(1000);
        entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.PurchaseOrder).WithMany(x => x.GoodsReceipts)
            .HasForeignKey(x => x.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => !x.IsDeleted);
    }
}

public sealed class GoodsReceiptItemConfiguration :
    IEntityTypeConfiguration<GoodsReceiptItem>
{
    public void Configure(EntityTypeBuilder<GoodsReceiptItem> entity)
    {
        entity.ToTable("goods_receipt_items");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.GoodsReceiptId, x.LineNumber }).IsUnique();
        entity.HasIndex(x => x.PurchaseOrderItemId);
        entity.HasIndex(x => x.InventoryItemId);
        entity.Property(x => x.MaterialDescription).HasMaxLength(500).IsRequired();
        entity.Property(x => x.Brand).HasMaxLength(150);
        entity.Property(x => x.Model).HasMaxLength(150);
        entity.Property(x => x.OrderedQuantity).HasPrecision(18, 4);
        entity.Property(x => x.PreviouslyReceivedQuantity).HasPrecision(18, 4);
        entity.Property(x => x.DeliveredQuantity).HasPrecision(18, 4);
        entity.Property(x => x.AcceptedQuantity).HasPrecision(18, 4);
        entity.Property(x => x.RejectedQuantity).HasPrecision(18, 4);
        entity.Property(x => x.DamagedQuantity).HasPrecision(18, 4);
        entity.Property(x => x.Unit).HasMaxLength(30).IsRequired();
        entity.Property(x => x.LotNumber).HasMaxLength(100);
        entity.Property(x => x.SerialNumber).HasMaxLength(250);
        entity.Property(x => x.ShelfLocation).HasMaxLength(100);
        entity.Property(x => x.Notes).HasMaxLength(1000);
        entity.HasOne(x => x.GoodsReceipt).WithMany(x => x.Items)
            .HasForeignKey(x => x.GoodsReceiptId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(x => x.PurchaseOrderItem).WithMany(x => x.GoodsReceiptItems)
            .HasForeignKey(x => x.PurchaseOrderItemId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.InventoryItem).WithMany()
            .HasForeignKey(x => x.InventoryItemId)
            .OnDelete(DeleteBehavior.SetNull);
        entity.HasQueryFilter(x => !x.IsDeleted);
    }
}
