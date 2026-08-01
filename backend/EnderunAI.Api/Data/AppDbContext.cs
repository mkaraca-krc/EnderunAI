using EnderunAI.Api.Models;
using EnderunAI.Api.Models.GoodsReceipt;
using EnderunAI.Api.Models.PurchaseOrder;
using EnderunAI.Api.Models.Rfq;
using EnderunAI.Api.Models.Secretariat;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Data;


public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options)
    : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<AppRole> Roles => Set<AppRole>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<CurrentAccount> CurrentAccounts => Set<CurrentAccount>();

    public DbSet<AccountingAccount> AccountingAccounts =>
        Set<AccountingAccount>();
    public DbSet<AccountingVoucher> AccountingVouchers =>
        Set<AccountingVoucher>();
    public DbSet<AccountingVoucherLine> AccountingVoucherLines =>
        Set<AccountingVoucherLine>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProgressPayment> ProgressPayments => Set<ProgressPayment>();
    public DbSet<ProgressPaymentItem> ProgressPaymentItems => Set<ProgressPaymentItem>();
    public DbSet<ProgressPaymentDeduction> ProgressPaymentDeductions => Set<ProgressPaymentDeduction>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<EngineeringPosition> EngineeringPositions => Set<EngineeringPosition>();
    public DbSet<EngineeringRecipe> EngineeringRecipes => Set<EngineeringRecipe>();
    public DbSet<EngineeringRecipeMaterial> EngineeringRecipeMaterials => Set<EngineeringRecipeMaterial>();
    public DbSet<EngineeringRecipeLabor> EngineeringRecipeLabors => Set<EngineeringRecipeLabor>();
    public DbSet<EngineeringRecipeMachine> EngineeringRecipeMachines => Set<EngineeringRecipeMachine>();
    public DbSet<Personnel> Personnel => Set<Personnel>();
    public DbSet<PersonnelAssignment> PersonnelAssignments => Set<PersonnelAssignment>();
    public DbSet<PurchaseRequest> PurchaseRequests => Set<PurchaseRequest>();
    public DbSet<PurchaseRequestItem> PurchaseRequestItems => Set<PurchaseRequestItem>();
    public DbSet<Rfq> Rfqs => Set<Rfq>();
    public DbSet<RfqItem> RfqItems => Set<RfqItem>();
    public DbSet<RfqSupplier> RfqSuppliers => Set<RfqSupplier>();
    public DbSet<RfqSupplierQuotation> RfqSupplierQuotations => Set<RfqSupplierQuotation>();
    public DbSet<RfqSupplierQuotationItem> RfqSupplierQuotationItems => Set<RfqSupplierQuotationItem>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<GoodsReceiptItem> GoodsReceiptItems => Set<GoodsReceiptItem>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<WarehouseStock> WarehouseStocks => Set<WarehouseStock>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<DocumentNumberSequence> DocumentNumberSequences => Set<DocumentNumberSequence>();
    public DbSet<ManufacturerPriceList> ManufacturerPriceLists => Set<ManufacturerPriceList>();
    public DbSet<ManufacturerPriceListItem> ManufacturerPriceListItems => Set<ManufacturerPriceListItem>();
    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<OfferItem> OfferItems => Set<OfferItem>();
    public DbSet<DocumentCategory> DocumentCategories => Set<DocumentCategory>();
    public DbSet<IncomingDocument> IncomingDocuments => Set<IncomingDocument>();
    public DbSet<OutgoingDocument> OutgoingDocuments => Set<OutgoingDocument>();
    public DbSet<DocumentWorkflow> DocumentWorkflows => Set<DocumentWorkflow>();
    public DbSet<DocumentAttachment> DocumentAttachments => Set<DocumentAttachment>();
    public DbSet<CargoShipment> CargoShipments => Set<CargoShipment>();
    public DbSet<VisitorRecord> VisitorRecords => Set<VisitorRecord>();
    public DbSet<PhoneNote> PhoneNotes => Set<PhoneNote>();
    public DbSet<SecretariatScheduleEntry> SecretariatScheduleEntries => Set<SecretariatScheduleEntry>();
    public DbSet<PriceDifferenceProfile> PriceDifferenceProfiles => Set<PriceDifferenceProfile>();
    public DbSet<PriceDifferenceCoefficient> PriceDifferenceCoefficients => Set<PriceDifferenceCoefficient>();
    public DbSet<PriceDifferenceIndexPeriod> PriceDifferenceIndexPeriods => Set<PriceDifferenceIndexPeriod>();
    public DbSet<ProjectBoq> ProjectBoqs => Set<ProjectBoq>();
    public DbSet<ProjectBoqItem> ProjectBoqItems => Set<ProjectBoqItem>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();
    public DbSet<ProjectSite> ProjectSites => Set<ProjectSite>();
    public DbSet<ProjectSiteAssignment> ProjectSiteAssignments => Set<ProjectSiteAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        EnderunAI.Api.Data.Configurations.ProcurementModelConfiguration.Configure(modelBuilder);

        ConfigureSecurity(modelBuilder);
        ConfigureCompanies(modelBuilder);
        ConfigureBranches(modelBuilder);
        ConfigureCurrentAccounts(modelBuilder);
        ConfigureAccountingAccounts(modelBuilder);
        ConfigureAccountingVouchers(modelBuilder);
        ConfigureAccountingVoucherLines(modelBuilder);
        ConfigureProjects(modelBuilder);
        ConfigureProgressPayments(modelBuilder);
        ConfigureWarehouses(modelBuilder);
        ConfigureEngineeringPositions(modelBuilder);
        ConfigureEngineeringRecipes(modelBuilder);
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
        ConfigureSecretariat(modelBuilder);
        ConfigurePriceDifference(modelBuilder);
        ConfigureProjectBoq(modelBuilder);
        ConfigureStockReservations(modelBuilder);
        ConfigureProjectSites(modelBuilder);
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

    private static void ConfigureAccountingAccounts(
        ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AccountingAccount>(entity =>
        {
            entity.ToTable("accounting_accounts");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.CompanyId, x.Code })
                .IsUnique();

            entity.HasIndex(x => x.ParentAccountId);

            entity.Property(x => x.Code)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(x => x.Name)
                .HasMaxLength(250)
                .IsRequired();

            entity.Property(x => x.Description)
                .HasMaxLength(1000);

            entity.Property(x => x.CurrencyCode)
                .HasMaxLength(3);

            entity.Property(x => x.Nature)
                .HasConversion<int>()
                .IsRequired();

            entity.Property(x => x.Level)
                .IsRequired();

            entity.HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.ParentAccount)
                .WithMany(x => x.ChildAccounts)
                .HasForeignKey(x => x.ParentAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }


    private static void ConfigureAccountingVouchers(
        ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AccountingVoucher>(entity =>
        {
            entity.ToTable("accounting_vouchers");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new
                {
                    x.CompanyId,
                    x.VoucherNumber
                })
                .IsUnique();

            entity.HasIndex(x => new
                {
                    x.CompanyId,
                    x.VoucherDate
                });

            entity.HasIndex(x => new
                {
                    x.CompanyId,
                    x.Status
                });

            entity.Property(x => x.VoucherNumber)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(x => x.VoucherType)
                .HasConversion<int>()
                .IsRequired();

            entity.Property(x => x.Status)
                .HasConversion<int>()
                .IsRequired();

            entity.Property(x => x.CurrencyCode)
                .HasMaxLength(3)
                .IsRequired();

            entity.Property(x => x.ExchangeRate)
                .HasPrecision(18, 6);

            entity.Property(x => x.Description)
                .HasMaxLength(1000);

            entity.Property(x => x.ReferenceNumber)
                .HasMaxLength(100);

            entity.Property(x => x.SourceModule)
                .HasMaxLength(100);

            entity.Property(x => x.TotalDebit)
                .HasPrecision(18, 2);

            entity.Property(x => x.TotalCredit)
                .HasPrecision(18, 2);

            entity.Property(x => x.CancellationReason)
                .HasMaxLength(1000);

            entity.HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(x => x.Lines)
                .WithOne(x => x.AccountingVoucher)
                .HasForeignKey(x => x.AccountingVoucherId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureAccountingVoucherLines(
        ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AccountingVoucherLine>(entity =>
        {
            entity.ToTable("accounting_voucher_lines");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new
                {
                    x.AccountingVoucherId,
                    x.LineNumber
                })
                .IsUnique();

            entity.HasIndex(x => x.AccountingAccountId);
            entity.HasIndex(x => x.CurrentAccountId);
            entity.HasIndex(x => x.ProjectId);

            entity.Property(x => x.Description)
                .HasMaxLength(1000);

            entity.Property(x => x.DebitAmount)
                .HasPrecision(18, 2);

            entity.Property(x => x.CreditAmount)
                .HasPrecision(18, 2);

            entity.Property(x => x.CurrencyCode)
                .HasMaxLength(3)
                .IsRequired();

            entity.Property(x => x.ExchangeRate)
                .HasPrecision(18, 6);

            entity.Property(x => x.DebitAmountLocal)
                .HasPrecision(18, 2);

            entity.Property(x => x.CreditAmountLocal)
                .HasPrecision(18, 2);

            entity.Property(x => x.CostCenterCode)
                .HasMaxLength(100);

            entity.Property(x => x.DocumentNumber)
                .HasMaxLength(100);

            entity.HasOne(x => x.AccountingAccount)
                .WithMany()
                .HasForeignKey(x => x.AccountingAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.CurrentAccount)
                .WithMany()
                .HasForeignKey(x => x.CurrentAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Project)
                .WithMany()
                .HasForeignKey(x => x.ProjectId)
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

            entity.HasOne(x => x.ProjectSite)
                .WithMany(x => x.Warehouses)
                .HasForeignKey(x => x.ProjectSiteId)
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


    private static void ConfigureEngineeringPositions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EngineeringPosition>(entity =>
        {
            entity.ToTable("engineering_positions");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.Source, x.Discipline });

            entity.Property(x => x.Code).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Unit).HasMaxLength(30).IsRequired();
            entity.Property(x => x.OfficialInstitution).HasMaxLength(150);
            entity.Property(x => x.OfficialCode).HasMaxLength(80);
            entity.Property(x => x.Category).HasMaxLength(200);
            entity.Property(x => x.SearchKeywords).HasMaxLength(1000);
            entity.Property(x => x.DefaultLaborHours).HasPrecision(18, 4);
            entity.Property(x => x.DefaultHelperHours).HasPrecision(18, 4);
            entity.Property(x => x.DefaultMachineHours).HasPrecision(18, 4);
            entity.Ignore(x => x.RevisionCode);

            entity.HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }


    private static void ConfigureEngineeringRecipes(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EngineeringRecipe>(entity =>
        {
            entity.ToTable("engineering_recipes");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.EngineeringPositionId, x.Version })
                .IsUnique();

            entity.Property(x => x.Description).HasMaxLength(500);

            entity.HasOne(x => x.EngineeringPosition)
                .WithMany()
                .HasForeignKey(x => x.EngineeringPositionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<EngineeringRecipeMaterial>(entity =>
        {
            entity.ToTable("engineering_recipe_materials");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.MaterialCode)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.MaterialName)
                .HasMaxLength(300)
                .IsRequired();

            entity.Property(x => x.Quantity).HasPrecision(18, 4);
            entity.Property(x => x.Unit).HasMaxLength(30).IsRequired();
            entity.Property(x => x.WastePercent).HasPrecision(8, 4);
            entity.Property(x => x.Notes).HasMaxLength(500);

            entity.HasOne(x => x.EngineeringRecipe)
                .WithMany(x => x.Materials)
                .HasForeignKey(x => x.EngineeringRecipeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<EngineeringRecipeLabor>(entity =>
        {
            entity.ToTable("engineering_recipe_labors");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.PersonCount).HasPrecision(18, 4);
            entity.Property(x => x.Hours).HasPrecision(18, 4);
            entity.Property(x => x.Notes).HasMaxLength(500);

            entity.HasOne(x => x.EngineeringRecipe)
                .WithMany(x => x.Labors)
                .HasForeignKey(x => x.EngineeringRecipeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<EngineeringRecipeMachine>(entity =>
        {
            entity.ToTable("engineering_recipe_machines");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.MachineName)
                .HasMaxLength(250)
                .IsRequired();

            entity.Property(x => x.Quantity).HasPrecision(18, 4);
            entity.Property(x => x.Hours).HasPrecision(18, 4);
            entity.Property(x => x.Notes).HasMaxLength(500);

            entity.HasOne(x => x.EngineeringRecipe)
                .WithMany(x => x.Machines)
                .HasForeignKey(x => x.EngineeringRecipeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }


    private static void ConfigureSecretariat(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentCategory>(entity =>
        {
            entity.ToTable("document_categories");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<IncomingDocument>(entity =>
        {
            entity.ToTable("incoming_documents");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CompanyId, x.DocumentNumber }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.Status, x.DocumentDate });
            entity.Property(x => x.DocumentNumber).HasMaxLength(80).IsRequired();
            entity.Property(x => x.ExternalDocumentNumber).HasMaxLength(100);
            entity.Property(x => x.SenderName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.SenderOrganization).HasMaxLength(250);
            entity.Property(x => x.Subject).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.DeliveryMethod).HasMaxLength(100);
            entity.Property(x => x.AssignedToName).HasMaxLength(200);
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<DocumentCategory>().WithMany().HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<OutgoingDocument>(entity =>
        {
            entity.ToTable("outgoing_documents");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CompanyId, x.DocumentNumber }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.Status, x.DocumentDate });
            entity.Property(x => x.DocumentNumber).HasMaxLength(80).IsRequired();
            entity.Property(x => x.RecipientName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.RecipientOrganization).HasMaxLength(250);
            entity.Property(x => x.Subject).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.DeliveryMethod).HasMaxLength(100);
            entity.Property(x => x.ReferenceNumber).HasMaxLength(100);
            entity.Property(x => x.SignedByName).HasMaxLength(200);
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<DocumentCategory>().WithMany().HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<DocumentWorkflow>(entity =>
        {
            entity.ToTable("document_workflows");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Direction, x.DocumentId, x.ActionAtUtc });
            entity.Property(x => x.FromUserName).HasMaxLength(200);
            entity.Property(x => x.ToUserName).HasMaxLength(200);
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<DocumentAttachment>(entity =>
        {
            entity.ToTable("document_attachments");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Direction, x.DocumentId });
            entity.Property(x => x.FileName).HasMaxLength(255).IsRequired();
            entity.Property(x => x.StoredFileName).HasMaxLength(255).IsRequired();
            entity.Property(x => x.FilePath).HasMaxLength(500).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(150);
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<CargoShipment>(entity =>
        {
            entity.ToTable("cargo_shipments");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CompanyId, x.TrackingNumber }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.Status, x.CargoDate });
            entity.Property(x => x.TrackingNumber).HasMaxLength(100).IsRequired();
            entity.Property(x => x.CargoCompany).HasMaxLength(150).IsRequired();
            entity.Property(x => x.SenderName).HasMaxLength(200);
            entity.Property(x => x.RecipientName).HasMaxLength(200);
            entity.Property(x => x.InstitutionName).HasMaxLength(250);
            entity.Property(x => x.DeliveredToName).HasMaxLength(200);
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<VisitorRecord>(entity =>
        {
            entity.ToTable("visitor_records");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CompanyId, x.Status, x.PlannedVisitAtUtc });
            entity.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.IdentityNumber).HasMaxLength(20);
            entity.Property(x => x.PhoneNumber).HasMaxLength(30);
            entity.Property(x => x.Email).HasMaxLength(200);
            entity.Property(x => x.CompanyName).HasMaxLength(250);
            entity.Property(x => x.VehiclePlate).HasMaxLength(20);
            entity.Property(x => x.VisitorCardNumber).HasMaxLength(50);
            entity.Property(x => x.PersonToVisit).HasMaxLength(200).IsRequired();
            entity.Property(x => x.DepartmentName).HasMaxLength(150);
            entity.Property(x => x.VisitPurpose).HasMaxLength(500).IsRequired();
            entity.Property(x => x.ApprovedByName).HasMaxLength(200);
            entity.Property(x => x.ReceivedByName).HasMaxLength(200);
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<PhoneNote>(entity =>
        {
            entity.ToTable("phone_notes");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CompanyId, x.Status, x.ReceivedAtUtc });
            entity.Property(x => x.CallerName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.PhoneNumber).HasMaxLength(30);
            entity.Property(x => x.InstitutionName).HasMaxLength(250);
            entity.Property(x => x.Subject).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.ResponsibleName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<SecretariatScheduleEntry>(entity =>
        {
            entity.ToTable("secretariat_schedule_entries");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CompanyId, x.Type, x.Status, x.StartAtUtc });
            entity.Property(x => x.Title).HasMaxLength(300).IsRequired();
            entity.Property(x => x.ContactName).HasMaxLength(200);
            entity.Property(x => x.CompanyName).HasMaxLength(250);
            entity.Property(x => x.Location).HasMaxLength(300);
            entity.Property(x => x.OwnerName).HasMaxLength(200);
            entity.Property(x => x.Participants).HasMaxLength(2000);
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }


    private static void ConfigureProgressPayments(
        ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProgressPayment>(entity =>
        {
            entity.ToTable("progress_payments");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new
            {
                x.CompanyId,
                x.ProgressPaymentNumber
            }).IsUnique();

            entity.HasIndex(x => new
            {
                x.ProjectId,
                x.PeriodNumber
            }).IsUnique();

            entity.HasIndex(x => new
            {
                x.CompanyId,
                x.Status,
                x.ProgressPaymentDate
            });

            entity.Property(x => x.ProgressPaymentNumber)
                .HasMaxLength(80)
                .IsRequired();

            entity.Property(x => x.ProgressPaymentDate)
                .HasColumnType("date");

            entity.Property(x => x.PeriodStartDate)
                .HasColumnType("date");

            entity.Property(x => x.PeriodEndDate)
                .HasColumnType("date");

            entity.Property(x => x.Status)
                .HasConversion<int>();

            entity.Property(x => x.CurrencyCode)
                .HasMaxLength(3)
                .IsRequired();

            entity.Property(x => x.ContractAmount)
                .HasPrecision(18, 2);

            entity.Property(x => x.PreviousAmount)
                .HasPrecision(18, 2);

            entity.Property(x => x.CurrentAmount)
                .HasPrecision(18, 2);

            entity.Property(x => x.CumulativeAmount)
                .HasPrecision(18, 2);

            entity.Property(x => x.PriceDifferenceAmount)
                .HasPrecision(18, 2);

            entity.Property(x => x.VatRate)
                .HasPrecision(8, 4);

            entity.Property(x => x.VatAmount)
                .HasPrecision(18, 2);

            entity.Property(x => x.WithholdingAmount)
                .HasPrecision(18, 2);

            entity.Property(x => x.TotalDeductionAmount)
                .HasPrecision(18, 2);

            entity.Property(x => x.GrossPayableAmount)
                .HasPrecision(18, 2);

            entity.Property(x => x.NetPayableAmount)
                .HasPrecision(18, 2);

            entity.Property(x => x.Description)
                .HasMaxLength(2000);

            entity.Property(x => x.Notes)
                .HasMaxLength(4000);

            entity.Property(x => x.CancellationReason)
                .HasMaxLength(2000);

            entity.HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Project)
                .WithMany()
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(x => x.Items)
                .WithOne(x => x.ProgressPayment)
                .HasForeignKey(x => x.ProgressPaymentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(x => x.Deductions)
                .WithOne(x => x.ProgressPayment)
                .HasForeignKey(x => x.ProgressPaymentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ProgressPaymentItem>(entity =>
        {
            entity.ToTable("progress_payment_items");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new
            {
                x.ProgressPaymentId,
                x.LineNumber
            }).IsUnique();

            entity.Property(x => x.PositionCode)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.Description)
                .HasMaxLength(1000)
                .IsRequired();

            entity.Property(x => x.Unit)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(x => x.ContractQuantity)
                .HasPrecision(18, 4);

            entity.Property(x => x.PreviousQuantity)
                .HasPrecision(18, 4);

            entity.Property(x => x.CurrentQuantity)
                .HasPrecision(18, 4);

            entity.Property(x => x.CumulativeQuantity)
                .HasPrecision(18, 4);

            entity.Property(x => x.UnitPrice)
                .HasPrecision(18, 4);

            entity.Property(x => x.PreviousAmount)
                .HasPrecision(18, 2);

            entity.Property(x => x.CurrentAmount)
                .HasPrecision(18, 2);

            entity.Property(x => x.CumulativeAmount)
                .HasPrecision(18, 2);

            entity.Property(x => x.CompletionRate)
                .HasPrecision(10, 4);

            entity.Property(x => x.MeasurementReference)
                .HasMaxLength(500);

            entity.Property(x => x.Notes)
                .HasMaxLength(2000);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ProgressPaymentDeduction>(entity =>
        {
            entity.ToTable("progress_payment_deductions");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new
            {
                x.ProgressPaymentId,
                x.LineNumber
            }).IsUnique();

            entity.Property(x => x.Description)
                .HasMaxLength(500)
                .IsRequired();

            entity.Property(x => x.Rate)
                .HasPrecision(10, 4);

            entity.Property(x => x.BaseAmount)
                .HasPrecision(18, 2);

            entity.Property(x => x.Amount)
                .HasPrecision(18, 2);

            entity.Property(x => x.Notes)
                .HasMaxLength(2000);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigurePriceDifference(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PriceDifferenceProfile>(entity =>
        {
            entity.ToTable("price_difference_profiles");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => x.CompanyId);
            entity.HasIndex(x => new { x.ProjectId, x.IsDefault });
            entity.HasIndex(x => new { x.ProjectId, x.ProfileName }).IsUnique();

            entity.Property(x => x.ProfileName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.CalculationType).HasConversion<int>();
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.FormulaName).HasMaxLength(250);
            entity.Property(x => x.Notes).HasMaxLength(2000);

            entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Coefficient).WithOne(x => x.PriceDifferenceProfile)
                .HasForeignKey<PriceDifferenceCoefficient>(x => x.PriceDifferenceProfileId);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<PriceDifferenceCoefficient>(entity =>
        {
            entity.ToTable("price_difference_coefficients");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.PriceDifferenceProfileId).IsUnique();

            entity.Property(x => x.A).HasPrecision(18, 8);
            entity.Property(x => x.B1).HasPrecision(18, 8);
            entity.Property(x => x.B2).HasPrecision(18, 8);
            entity.Property(x => x.B3).HasPrecision(18, 8);
            entity.Property(x => x.B4).HasPrecision(18, 8);
            entity.Property(x => x.B5).HasPrecision(18, 8);
            entity.Property(x => x.C).HasPrecision(18, 8);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<PriceDifferenceIndexPeriod>(entity =>
        {
            entity.ToTable("price_difference_index_periods");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Year, x.Month, x.SourceName }).IsUnique();

            entity.Property(x => x.SourceName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.PeriodLabel).HasMaxLength(100);
            entity.Property(x => x.LaborIndex).HasPrecision(18, 8);
            entity.Property(x => x.FuelIndex).HasPrecision(18, 8);
            entity.Property(x => x.MaterialIndex).HasPrecision(18, 8);
            entity.Property(x => x.MachineryIndex).HasPrecision(18, 8);
            entity.Property(x => x.CementIndex).HasPrecision(18, 8);
            entity.Property(x => x.OtherIndex).HasPrecision(18, 8);
            entity.Property(x => x.CopperIndex).HasPrecision(18, 8);
            entity.Property(x => x.SteelIndex).HasPrecision(18, 8);
            entity.Property(x => x.ElectricityIndex).HasPrecision(18, 8);
            entity.Property(x => x.UsdRate).HasPrecision(18, 8);
            entity.Property(x => x.EurRate).HasPrecision(18, 8);
            entity.Property(x => x.Notes).HasMaxLength(2000);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureProjectBoq(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProjectBoq>(entity =>
        {
            entity.ToTable("project_boqs");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.CompanyId, x.BoqNumber, x.RevisionNumber }).IsUnique();
            entity.HasIndex(x => new { x.ProjectId, x.IsCurrentRevision });

            entity.Property(x => x.BoqNumber).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Status).HasConversion<int>();
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.TotalAmount).HasPrecision(18, 2);
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.Notes).HasMaxLength(4000);

            entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ProjectBoqItem>(entity =>
        {
            entity.ToTable("project_boq_items");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.ProjectBoqId, x.LineNumber }).IsUnique();
            entity.HasIndex(x => new { x.ProjectBoqId, x.PositionCode });

            entity.Property(x => x.PositionCode).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Unit).HasMaxLength(30).IsRequired();
            entity.Property(x => x.ContractQuantity).HasPrecision(18, 4);
            entity.Property(x => x.UnitPrice).HasPrecision(18, 6);
            entity.Property(x => x.TotalAmount).HasPrecision(18, 2);
            entity.Property(x => x.ItemType).HasConversion<int>();
            entity.Property(x => x.Category).HasMaxLength(200);
            entity.Property(x => x.Notes).HasMaxLength(2000);

            entity.HasOne(x => x.ProjectBoq).WithMany(x => x.Items)
                .HasForeignKey(x => x.ProjectBoqId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<EngineeringPosition>().WithMany()
                .HasForeignKey(x => x.EngineeringPositionId).OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureStockReservations(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StockReservation>(entity =>
        {
            entity.ToTable("stock_reservations");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => x.ReservationNumber).IsUnique();
            entity.HasIndex(x => x.CompanyId);
            entity.HasIndex(x => x.InventoryItemId);
            entity.HasIndex(x => x.ProjectId);
            entity.HasIndex(x => new { x.PurchaseRequestId, x.PurchaseRequestItemId });
            entity.HasIndex(x => x.PurchaseRequestItemId);
            entity.HasIndex(x => new { x.WarehouseId, x.InventoryItemId, x.Status });

            entity.Property(x => x.ReservationNumber).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ReservedQuantity).HasPrecision(18, 4);
            entity.Property(x => x.ConsumedQuantity).HasPrecision(18, 4);
            entity.Property(x => x.Status).HasConversion<int>();
            entity.Property(x => x.Description).HasMaxLength(1000);

            entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.InventoryItem).WithMany().HasForeignKey(x => x.InventoryItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PurchaseRequest).WithMany().HasForeignKey(x => x.PurchaseRequestId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PurchaseRequestItem).WithMany().HasForeignKey(x => x.PurchaseRequestItemId).OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureProjectSites(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProjectSite>(entity =>
        {
            entity.ToTable("project_sites");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.ProjectId, x.Code }).IsUnique();

            entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Location).HasMaxLength(500);
            entity.Property(x => x.Notes).HasMaxLength(2000);

            entity.HasOne(x => x.Project)
                .WithMany()
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ProjectSiteAssignment>(entity =>
        {
            entity.ToTable("project_site_assignments");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.PersonnelId, x.IsActive });
            entity.HasIndex(x => x.ProjectSiteId);

            entity.Property(x => x.Role).HasMaxLength(200);
            entity.Property(x => x.Notes).HasMaxLength(2000);

            entity.HasOne(x => x.Personnel)
                .WithMany(x => x.SiteAssignments)
                .HasForeignKey(x => x.PersonnelId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.ProjectSite)
                .WithMany(x => x.Assignments)
                .HasForeignKey(x => x.ProjectSiteId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }
}
