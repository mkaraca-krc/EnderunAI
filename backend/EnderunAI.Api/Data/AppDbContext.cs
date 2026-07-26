using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<AppRole> Roles => Set<AppRole>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<CurrentAccount> CurrentAccounts => Set<CurrentAccount>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    public DbSet<Material> Materials => Set<Material>();
    public DbSet<WarehouseStock> WarehouseStocks => Set<WarehouseStock>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    public DbSet<PurchaseRequest> PurchaseRequests => Set<PurchaseRequest>();
    public DbSet<PurchaseRequestItem> PurchaseRequestItems => Set<PurchaseRequestItem>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<GoodsReceiptItem> GoodsReceiptItems => Set<GoodsReceiptItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureSecurity(modelBuilder);
        ConfigureCompanies(modelBuilder);
        ConfigureBranches(modelBuilder);
        ConfigureCurrentAccounts(modelBuilder);
        ConfigureProjects(modelBuilder);
        ConfigureWarehouses(modelBuilder);
        ConfigureInventory(modelBuilder);
        ConfigurePurchasing(modelBuilder);
        ConfigureGoodsReceipts(modelBuilder);
    }

    private static void ConfigureSecurity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Username).IsUnique();

            entity.Property(x => x.Username).HasMaxLength(100).IsRequired();
            entity.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(200);
            entity.Property(x => x.PasswordHash).IsRequired();
            entity.Property(x => x.PasswordSalt).IsRequired();
        });

        modelBuilder.Entity<AppRole>(entity =>
        {
            entity.ToTable("roles");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Name).IsUnique();

            entity.Property(x => x.Name).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(300);
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.ToTable("user_roles");
            entity.HasKey(x => new { x.UserId, x.RoleId });

            entity.HasOne(x => x.User)
                .WithMany(x => x.UserRoles)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Role)
                .WithMany(x => x.UserRoles)
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureCompanies(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Company>(entity =>
        {
            entity.ToTable("companies");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(250).IsRequired();
            entity.Property(x => x.TradeName).HasMaxLength(300);
            entity.Property(x => x.TaxOffice).HasMaxLength(100);
            entity.Property(x => x.TaxNumber).HasMaxLength(20);
            entity.Property(x => x.MersisNumber).HasMaxLength(30);
            entity.Property(x => x.Phone).HasMaxLength(30);
            entity.Property(x => x.Email).HasMaxLength(200);
            entity.Property(x => x.Website).HasMaxLength(250);
            entity.Property(x => x.LogoPath).HasMaxLength(500);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureBranches(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Branch>(entity =>
        {
            entity.ToTable("branches");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Phone).HasMaxLength(30);
            entity.Property(x => x.Email).HasMaxLength(200);

            entity.HasOne(x => x.Company)
                .WithMany(x => x.Branches)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureCurrentAccounts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CurrentAccount>(entity =>
        {
            entity.ToTable("current_accounts");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(300).IsRequired();
            entity.Property(x => x.ShortName).HasMaxLength(150);
            entity.Property(x => x.TaxOffice).HasMaxLength(100);
            entity.Property(x => x.TaxNumber).HasMaxLength(20);
            entity.Property(x => x.MersisNumber).HasMaxLength(30);
            entity.Property(x => x.AuthorizedPerson).HasMaxLength(200);
            entity.Property(x => x.Phone).HasMaxLength(30);
            entity.Property(x => x.Email).HasMaxLength(200);
            entity.Property(x => x.PaymentTerm).HasMaxLength(100);
            entity.Property(x => x.CreditLimit).HasPrecision(18, 2);

            entity.HasOne(x => x.Company)
                .WithMany(x => x.CurrentAccounts)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureProjects(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>(entity =>
        {
            entity.ToTable("projects");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(300).IsRequired();
            entity.Property(x => x.ContractNumber).HasMaxLength(100);
            entity.Property(x => x.ContractAmount).HasPrecision(18, 2);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.VatRate).HasPrecision(5, 2);
            entity.Property(x => x.WithholdingRate).HasMaxLength(20);
            entity.Property(x => x.City).HasMaxLength(100);
            entity.Property(x => x.District).HasMaxLength(100);
            entity.Property(x => x.HealthReason).HasMaxLength(500);

            entity.HasOne(x => x.Company)
                .WithMany(x => x.Projects)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Branch)
                .WithMany(x => x.Projects)
                .HasForeignKey(x => x.BranchId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.EmployerCurrentAccount)
                .WithMany(x => x.EmployerProjects)
                .HasForeignKey(x => x.EmployerCurrentAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureWarehouses(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Warehouse>(entity =>
        {
            entity.ToTable("warehouses");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();

            entity.HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Branch)
                .WithMany(x => x.Warehouses)
                .HasForeignKey(x => x.BranchId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Project)
                .WithMany(x => x.Warehouses)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureInventory(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Material>(entity =>
        {
            entity.ToTable("materials");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(250).IsRequired();
            entity.Property(x => x.Unit).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(100);
            entity.Property(x => x.Brand).HasMaxLength(100);
            entity.Property(x => x.Model).HasMaxLength(100);
            entity.Property(x => x.Barcode).HasMaxLength(100);
            entity.Property(x => x.MinimumStock).HasPrecision(18, 4);
            entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<WarehouseStock>(entity =>
        {
            entity.ToTable("warehouse_stocks");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.WarehouseId, x.MaterialId }).IsUnique();
            entity.Property(x => x.Quantity).HasPrecision(18, 4);
            entity.Property(x => x.ReservedQuantity).HasPrecision(18, 4);
            entity.Property(x => x.AverageUnitCost).HasPrecision(18, 4);
            entity.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Material).WithMany().HasForeignKey(x => x.MaterialId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<StockMovement>(entity =>
        {
            entity.ToTable("stock_movements");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CompanyId, x.MovementDateUtc });
            entity.Property(x => x.Quantity).HasPrecision(18, 4);
            entity.Property(x => x.UnitCost).HasPrecision(18, 4);
            entity.Property(x => x.ReferenceType).HasMaxLength(50);
            entity.Property(x => x.DocumentNumber).HasMaxLength(80);
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Material).WithMany().HasForeignKey(x => x.MaterialId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigurePurchasing(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PurchaseRequest>(entity =>
        {
            entity.ToTable("purchase_requests");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CompanyId, x.RequestNumber }).IsUnique();
            entity.Property(x => x.RequestNumber).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<PurchaseRequestItem>(entity =>
        {
            entity.ToTable("purchase_request_items");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Quantity).HasPrecision(18, 4);
            entity.Property(x => x.Unit).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.HasOne(x => x.PurchaseRequest).WithMany(x => x.Items).HasForeignKey(x => x.PurchaseRequestId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Material).WithMany().HasForeignKey(x => x.MaterialId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<PurchaseOrder>(entity =>
        {
            entity.ToTable("purchase_orders");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CompanyId, x.OrderNumber }).IsUnique();
            entity.Property(x => x.OrderNumber).HasMaxLength(80).IsRequired();
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.ExchangeRate).HasPrecision(18, 6);
            entity.Property(x => x.VatRate).HasPrecision(5, 2);
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.SupplierCurrentAccount).WithMany().HasForeignKey(x => x.SupplierCurrentAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PurchaseRequest).WithMany().HasForeignKey(x => x.PurchaseRequestId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<PurchaseOrderItem>(entity =>
        {
            entity.ToTable("purchase_order_items");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Quantity).HasPrecision(18, 4);
            entity.Property(x => x.ReceivedQuantity).HasPrecision(18, 4);
            entity.Property(x => x.Unit).HasMaxLength(30).IsRequired();
            entity.Property(x => x.UnitPrice).HasPrecision(18, 4);
            entity.Property(x => x.DiscountRate).HasPrecision(5, 2);
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.HasOne(x => x.PurchaseOrder).WithMany(x => x.Items).HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Material).WithMany().HasForeignKey(x => x.MaterialId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureGoodsReceipts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GoodsReceipt>(entity =>
        {
            entity.ToTable("goods_receipts");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CompanyId, x.ReceiptNumber }).IsUnique();
            entity.Property(x => x.ReceiptNumber).HasMaxLength(80).IsRequired();
            entity.Property(x => x.DeliveryNoteNumber).HasMaxLength(80);
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PurchaseOrder).WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<GoodsReceiptItem>(entity =>
        {
            entity.ToTable("goods_receipt_items");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Quantity).HasPrecision(18, 4);
            entity.Property(x => x.UnitCost).HasPrecision(18, 4);
            entity.HasOne(x => x.GoodsReceipt).WithMany(x => x.Items).HasForeignKey(x => x.GoodsReceiptId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.PurchaseOrderItem).WithMany().HasForeignKey(x => x.PurchaseOrderItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Material).WithMany().HasForeignKey(x => x.MaterialId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }
}
