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
    public DbSet<Personnel> Personnel => Set<Personnel>();
    public DbSet<PersonnelAssignment> PersonnelAssignments => Set<PersonnelAssignment>();
    public DbSet<PurchaseRequest> PurchaseRequests => Set<PurchaseRequest>();
    public DbSet<PurchaseRequestItem> PurchaseRequestItems => Set<PurchaseRequestItem>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<WarehouseStock> WarehouseStocks => Set<WarehouseStock>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<DocumentNumberSequence> DocumentNumberSequences => Set<DocumentNumberSequence>();
    public DbSet<ManufacturerPriceList> ManufacturerPriceLists => Set<ManufacturerPriceList>();
    public DbSet<ManufacturerPriceListItem> ManufacturerPriceListItems => Set<ManufacturerPriceListItem>();
    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<OfferItem> OfferItems => Set<OfferItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureSecurity(modelBuilder);
        ConfigureCompanies(modelBuilder);
        ConfigureBranches(modelBuilder);
        ConfigureCurrentAccounts(modelBuilder);
        ConfigureProjects(modelBuilder);
        ConfigureWarehouses(modelBuilder);
        ConfigurePersonnel(modelBuilder);
        ConfigurePersonnelAssignments(modelBuilder);
        ConfigurePurchaseRequests(modelBuilder);
        ConfigurePurchaseRequestItems(modelBuilder);
        ConfigureInventoryItems(modelBuilder);
        ConfigureWarehouseStocks(modelBuilder);
        ConfigureStockMovements(modelBuilder);
        ConfigureDocumentNumberSequences(modelBuilder);
        ConfigureManufacturerPriceLists(modelBuilder);
        ConfigureManufacturerPriceListItems(modelBuilder);
        ConfigureOffers(modelBuilder);
        ConfigureOfferItems(modelBuilder);
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

    private static void ConfigurePersonnel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Personnel>(entity =>
        {
            entity.ToTable("personnel");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.CompanyId, x.EmployeeNumber })
                .IsUnique();

            entity.HasIndex(x => x.IdentityNumber)
                .IsUnique();

            entity.Property(x => x.EmployeeNumber)
                .HasMaxLength(40)
                .IsRequired();

            entity.Property(x => x.FirstName)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.LastName)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.IdentityNumber).HasMaxLength(20);
            entity.Property(x => x.Phone).HasMaxLength(30);
            entity.Property(x => x.Email).HasMaxLength(200);
            entity.Property(x => x.JobTitle).HasMaxLength(150);
            entity.Property(x => x.Profession).HasMaxLength(150);
            entity.Property(x => x.SgkRegistrationNumber).HasMaxLength(50);
            entity.Property(x => x.MonthlySalary).HasPrecision(18, 2);

            entity.HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Branch)
                .WithMany()
                .HasForeignKey(x => x.BranchId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigurePersonnelAssignments(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PersonnelAssignment>(entity =>
        {
            entity.ToTable("personnel_assignments");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new
            {
                x.PersonnelId,
                x.ProjectId,
                x.StartDate
            });

            entity.Property(x => x.Role).HasMaxLength(150);
            entity.Property(x => x.Notes).HasMaxLength(1000);

            entity.HasOne(x => x.Personnel)
                .WithMany(x => x.Assignments)
                .HasForeignKey(x => x.PersonnelId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Project)
                .WithMany()
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }


    private static void ConfigurePurchaseRequests(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PurchaseRequest>(entity =>
        {
            entity.ToTable("purchase_requests");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.CompanyId, x.RequestNumber })
                .IsUnique();

            entity.Property(x => x.RequestNumber)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(x => x.RequestedByName)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.CancellationReason).HasMaxLength(500);

            entity.HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Project)
                .WithMany()
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigurePurchaseRequestItems(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PurchaseRequestItem>(entity =>
        {
            entity.ToTable("purchase_request_items");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new
            {
                x.PurchaseRequestId,
                x.LineNumber
            }).IsUnique();

            entity.Property(x => x.MaterialDescription)
                .HasMaxLength(500)
                .IsRequired();

            entity.Property(x => x.Quantity).HasPrecision(18, 4);

            entity.Property(x => x.Unit)
                .HasMaxLength(30)
                .IsRequired();

            entity.Property(x => x.Notes).HasMaxLength(1000);

            entity.HasOne(x => x.PurchaseRequest)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.PurchaseRequestId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }


    private static void ConfigureInventoryItems(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InventoryItem>(entity =>
        {
            entity.ToTable("inventory_items");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(150);
            entity.Property(x => x.Brand).HasMaxLength(150);
            entity.Property(x => x.Model).HasMaxLength(150);
            entity.Property(x => x.Unit).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Barcode).HasMaxLength(100);
            entity.Property(x => x.MinimumStock).HasPrecision(18, 4);
            entity.Property(x => x.MaximumStock).HasPrecision(18, 4);
            entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureWarehouseStocks(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WarehouseStock>(entity =>
        {
            entity.ToTable("warehouse_stocks");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.WarehouseId, x.InventoryItemId }).IsUnique();
            entity.Property(x => x.Quantity).HasPrecision(18, 4);
            entity.Property(x => x.ReservedQuantity).HasPrecision(18, 4);
            entity.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.InventoryItem).WithMany(x => x.WarehouseStocks).HasForeignKey(x => x.InventoryItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureStockMovements(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StockMovement>(entity =>
        {
            entity.ToTable("stock_movements");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.WarehouseId, x.InventoryItemId, x.MovementDate });
            entity.Property(x => x.Quantity).HasPrecision(18, 4);
            entity.Property(x => x.ReferenceNumber).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.RelatedWarehouse).WithMany().HasForeignKey(x => x.RelatedWarehouseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.InventoryItem).WithMany(x => x.StockMovements).HasForeignKey(x => x.InventoryItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PurchaseRequest).WithMany().HasForeignKey(x => x.PurchaseRequestId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }


    private static void ConfigureDocumentNumberSequences(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentNumberSequence>(entity =>
        {
            entity.ToTable("document_number_sequences");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new
            {
                x.CompanyId,
                x.DocumentType,
                x.Year
            }).IsUnique();

            entity.Property(x => x.DocumentType)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(x => x.Prefix)
                .HasMaxLength(20)
                .IsRequired();

            entity.Property(x => x.NumberLength)
                .HasDefaultValue(6);

            entity.HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }


    private static void ConfigureManufacturerPriceLists(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ManufacturerPriceList>(entity =>
        {
            entity.ToTable("manufacturer_price_lists");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.ManufacturerName)
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(x => x.ListName)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.Currency)
                .HasMaxLength(10)
                .IsRequired();

            entity.HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureManufacturerPriceListItems(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ManufacturerPriceListItem>(entity =>
        {
            entity.ToTable("manufacturer_price_list_items");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new
            {
                x.ManufacturerPriceListId,
                x.ProductCode
            });

            entity.Property(x => x.ProductCode)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.ProductDescription)
                .HasMaxLength(500)
                .IsRequired();

            entity.Property(x => x.Unit)
                .HasMaxLength(30)
                .IsRequired();

            entity.Property(x => x.ListPrice)
                .HasPrecision(18, 6);

            entity.Property(x => x.Category)
                .HasMaxLength(150);

            entity.Property(x => x.Brand)
                .HasMaxLength(150);

            entity.Property(x => x.Model)
                .HasMaxLength(150);

            entity.HasOne(x => x.ManufacturerPriceList)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.ManufacturerPriceListId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }


    private static void ConfigureOffers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Offer>(entity =>
        {
            entity.ToTable("offers");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.CompanyId, x.OfferNumber })
                .IsUnique();

            entity.Property(x => x.OfferNumber).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Currency).HasMaxLength(10).IsRequired();
            entity.Property(x => x.ExchangeRate).HasPrecision(18, 6);
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.Notes).HasMaxLength(4000);

            entity.Property(x => x.Subtotal).HasPrecision(18, 2);
            entity.Property(x => x.DiscountTotal).HasPrecision(18, 2);
            entity.Property(x => x.CostTotal).HasPrecision(18, 2);
            entity.Property(x => x.ProfitTotal).HasPrecision(18, 2);
            entity.Property(x => x.GrandTotal).HasPrecision(18, 2);

            entity.HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Project)
                .WithMany()
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureOfferItems(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OfferItem>(entity =>
        {
            entity.ToTable("offer_items");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.OfferId, x.LineNumber }).IsUnique();

            entity.Property(x => x.PositionNumber).HasMaxLength(100);
            entity.Property(x => x.Description).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.ManufacturerName).HasMaxLength(150);
            entity.Property(x => x.ProductCode).HasMaxLength(100);
            entity.Property(x => x.Brand).HasMaxLength(150);
            entity.Property(x => x.Model).HasMaxLength(150);
            entity.Property(x => x.Unit).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(1000);

            entity.Property(x => x.Quantity).HasPrecision(18, 4);
            entity.Property(x => x.ListPrice).HasPrecision(18, 6);
            entity.Property(x => x.DiscountRate).HasPrecision(9, 4);
            entity.Property(x => x.NetPurchasePrice).HasPrecision(18, 6);
            entity.Property(x => x.FreightRate).HasPrecision(9, 4);
            entity.Property(x => x.WasteRate).HasPrecision(9, 4);
            entity.Property(x => x.FinanceRate).HasPrecision(9, 4);
            entity.Property(x => x.GeneralExpenseRate).HasPrecision(9, 4);
            entity.Property(x => x.ProfitRate).HasPrecision(9, 4);
            entity.Property(x => x.UnitCost).HasPrecision(18, 6);
            entity.Property(x => x.UnitSalesPrice).HasPrecision(18, 6);
            entity.Property(x => x.CostTotal).HasPrecision(18, 2);
            entity.Property(x => x.SalesTotal).HasPrecision(18, 2);

            entity.HasOne(x => x.Offer)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.OfferId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.ManufacturerPriceListItem)
                .WithMany()
                .HasForeignKey(x => x.ManufacturerPriceListItemId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

}
