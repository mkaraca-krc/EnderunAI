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
    public DbSet<CompanyFinanceSettings> CompanyFinanceSettings => Set<CompanyFinanceSettings>();
    public DbSet<CashAccount> CashAccounts => Set<CashAccount>();
    public DbSet<CashTransaction> CashTransactions => Set<CashTransaction>();
    public DbSet<CompanyPayrollSettings> CompanyPayrollSettings =>
        Set<CompanyPayrollSettings>();
    public DbSet<PayrollTaxBracket> PayrollTaxBrackets => Set<PayrollTaxBracket>();
    public DbSet<Cheque> Cheques => Set<Cheque>();
    public DbSet<ChequeMovement> ChequeMovements => Set<ChequeMovement>();
    public DbSet<FactoringTransaction> FactoringTransactions => Set<FactoringTransaction>();
    public DbSet<SupplierInvoice> SupplierInvoices => Set<SupplierInvoice>();
    public DbSet<SupplierInvoiceItem> SupplierInvoiceItems => Set<SupplierInvoiceItem>();
    public DbSet<AccountingVoucher> AccountingVouchers =>
        Set<AccountingVoucher>();
    public DbSet<AccountingVoucherLine> AccountingVoucherLines =>
        Set<AccountingVoucherLine>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectDocument> ProjectDocuments => Set<ProjectDocument>();
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
    public DbSet<ProjectCostTransaction> ProjectCostTransactions => Set<ProjectCostTransaction>();
    public DbSet<HrProjectLaborCost> HrProjectLaborCosts => Set<HrProjectLaborCost>();
    public DbSet<WorkTask> WorkTasks => Set<WorkTask>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<HrCompensationComponent> HrCompensationComponents => Set<HrCompensationComponent>();
    public DbSet<HrCareerHistory> HrCareerHistories => Set<HrCareerHistory>();
    public DbSet<HrShiftDefinition> HrShiftDefinitions => Set<HrShiftDefinition>();
    public DbSet<HrShiftAssignment> HrShiftAssignments => Set<HrShiftAssignment>();
    public DbSet<HrAssetAssignment> HrAssetAssignments => Set<HrAssetAssignment>();
    public DbSet<ProjectMeasurement> ProjectMeasurements => Set<ProjectMeasurement>();
    public DbSet<ProjectMeasurementItem> ProjectMeasurementItems => Set<ProjectMeasurementItem>();
    public DbSet<JobPosting> JobPostings => Set<JobPosting>();
    public DbSet<JobCandidate> JobCandidates => Set<JobCandidate>();
    public DbSet<JobApplication> JobApplications => Set<JobApplication>();
    public DbSet<CandidateInterview> CandidateInterviews => Set<CandidateInterview>();
    public DbSet<ProjectSiteDailyReport> ProjectSiteDailyReports => Set<ProjectSiteDailyReport>();
    public DbSet<ProjectSiteDailyReportWorkItem> ProjectSiteDailyReportWorkItems => Set<ProjectSiteDailyReportWorkItem>();
    public DbSet<ProjectSiteDailyReportPhoto> ProjectSiteDailyReportPhotos => Set<ProjectSiteDailyReportPhoto>();
    public DbSet<EmployerPortalLink> EmployerPortalLinks => Set<EmployerPortalLink>();
    public DbSet<EmployerPortalEmailLog> EmployerPortalEmailLogs => Set<EmployerPortalEmailLog>();
    public DbSet<SecurityAuditEvent> SecurityAuditEvents => Set<SecurityAuditEvent>();

    public DbSet<CompanyBankAccount> CompanyBankAccounts => Set<CompanyBankAccount>();

    public DbSet<RoleWorkHourWindow> RoleWorkHourWindows => Set<RoleWorkHourWindow>();
    public DbSet<AccessRequest> AccessRequests => Set<AccessRequest>();
    public DbSet<TemporaryAccessGrant> TemporaryAccessGrants => Set<TemporaryAccessGrant>();

    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserPermissionOverride> UserPermissionOverrides => Set<UserPermissionOverride>();
    public DbSet<UserDataScope> UserDataScopes => Set<UserDataScope>();

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
        ConfigureCompanyFinanceSettings(modelBuilder);
        ConfigureCashAccounts(modelBuilder);
        ConfigurePayrollSettings(modelBuilder);
        ConfigureCheques(modelBuilder);
        ConfigureFactoringTransactions(modelBuilder);
        ConfigureSupplierInvoices(modelBuilder);
        ConfigureAccountingVoucherLines(modelBuilder);
        ConfigureProjects(modelBuilder);
        ConfigureProjectDocuments(modelBuilder);
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
        ConfigureProjectCostTransactions(modelBuilder);
        ConfigureHrProjectLaborCosts(modelBuilder);
        ConfigureWorkTasks(modelBuilder);
        ConfigureAttendanceRecords(modelBuilder);
        ConfigureHrCompensationComponents(modelBuilder);
        ConfigureHrCareerHistories(modelBuilder);
        ConfigureHrShifts(modelBuilder);
        ConfigureHrAssetAssignments(modelBuilder);
        ConfigureProjectMeasurements(modelBuilder);
        ConfigureHrRecruitment(modelBuilder);
        ConfigureEmployerPortal(modelBuilder);
        ConfigureSecurityAuditEvents(modelBuilder);
        ConfigureRbac(modelBuilder);
        ConfigureWorkHourAccess(modelBuilder);
    }

    private static void ConfigureRbac(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Permission>(entity =>
        {
            entity.ToTable("permissions");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => x.Key).IsUnique();

            entity.Property(x => x.Key).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Module).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<RolePermission>(entity =>
        {
            entity.ToTable("role_permissions");
            entity.HasKey(x => new { x.RoleId, x.PermissionId });

            entity.HasOne(x => x.Role)
                .WithMany()
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Permission)
                .WithMany(x => x.RolePermissions)
                .HasForeignKey(x => x.PermissionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserPermissionOverride>(entity =>
        {
            entity.ToTable("user_permission_overrides");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.UserId, x.PermissionId }).IsUnique();

            entity.Property(x => x.Effect).HasConversion<int>().IsRequired();

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Permission)
                .WithMany()
                .HasForeignKey(x => x.PermissionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserDataScope>(entity =>
        {
            entity.ToTable("user_data_scopes");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => x.UserId);

            entity.Property(x => x.ScopeType).HasConversion<int>().IsRequired();

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Branch)
                .WithMany()
                .HasForeignKey(x => x.BranchId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Project)
                .WithMany()
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.ProjectSite)
                .WithMany()
                .HasForeignKey(x => x.ProjectSiteId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureSecurityAuditEvents(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SecurityAuditEvent>(entity =>
        {
            entity.ToTable("security_audit_events");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.EntityType, x.EntityId });

            entity.Property(x => x.ActorUsername).HasMaxLength(100);
            entity.Property(x => x.Action).HasMaxLength(100).IsRequired();
            entity.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.DetailsJson).HasColumnType("jsonb");
            entity.Property(x => x.IpAddress).HasMaxLength(64);
            entity.Property(x => x.UserAgent).HasMaxLength(500);
        });
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
            entity.Property(x => x.DataScopePolicy).HasConversion<int>().IsRequired();
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
            entity.Property(x => x.TradeRegistryNumber).HasMaxLength(30);
            entity.Property(x => x.Phone).HasMaxLength(30);
            entity.Property(x => x.Email).HasMaxLength(200);
            entity.Property(x => x.Website).HasMaxLength(250);
            entity.Property(x => x.LogoPath).HasMaxLength(500);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<CompanyBankAccount>(entity =>
        {
            entity.ToTable("company_bank_accounts");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.BankName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Iban).HasMaxLength(34).IsRequired();
            entity.Property(x => x.AccountHolder).HasMaxLength(250);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3);

            entity.HasOne(x => x.Company)
                .WithMany(x => x.BankAccounts)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);

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

            // Kolonlar/FK'ler 20260724083553 migration'ından beri DB'de
            // mevcut — model bu sefer geriye dönük olarak şemayı
            // sahipleniyor (yeni migration'da duplicate op üretilmez).
            entity.HasOne(x => x.PayableAccountingAccount)
                .WithMany()
                .HasForeignKey(x => x.PayableAccountingAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.ReceivableAccountingAccount)
                .WithMany()
                .HasForeignKey(x => x.ReceivableAccountingAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureCompanyFinanceSettings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CompanyFinanceSettings>(entity =>
        {
            entity.ToTable("company_finance_settings");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => x.CompanyId).IsUnique();

            entity.Property(x => x.GmApprovalThresholdTry).HasPrecision(18, 2);
            entity.Property(x => x.ThreeWayTolerancePercent).HasPrecision(5, 2);
            entity.Property(x => x.DefaultVatRate).HasPrecision(5, 2);

            entity.HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.VatInAccount).WithMany()
                .HasForeignKey(x => x.VatInAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.VatOutAccount).WithMany()
                .HasForeignKey(x => x.VatOutAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.SalesAccount).WithMany()
                .HasForeignKey(x => x.SalesAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ExpenseAccount).WithMany()
                .HasForeignKey(x => x.ExpenseAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PayablesAccount).WithMany()
                .HasForeignKey(x => x.PayablesAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ReceivablesAccount).WithMany()
                .HasForeignKey(x => x.ReceivablesAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.FactoringExpenseAccount).WithMany()
                .HasForeignKey(x => x.FactoringExpenseAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.DeductionAccount).WithMany()
                .HasForeignKey(x => x.DeductionAccountId).OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureCashAccounts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CashAccount>(entity =>
        {
            entity.ToTable("cash_accounts");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.Type });

            entity.Property(x => x.Type).HasConversion<int>().IsRequired();
            entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.BankName).HasMaxLength(150);
            entity.Property(x => x.Iban).HasMaxLength(40);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.OpeningBalance).HasPrecision(18, 2);

            entity.HasOne(x => x.Company).WithMany()
                .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.AccountingAccount).WithMany()
                .HasForeignKey(x => x.AccountingAccountId).OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<CashTransaction>(entity =>
        {
            entity.ToTable("cash_transactions");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.CashAccountId, x.TransactionDate });
            entity.HasIndex(x => new { x.SourceModule, x.SourceEntityId });
            entity.HasIndex(x => x.CurrentAccountId);

            entity.Property(x => x.TransactionType).HasConversion<int>().IsRequired();
            entity.Property(x => x.Direction).HasConversion<int>().IsRequired();
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.DocumentNumber).HasMaxLength(100);
            entity.Property(x => x.SourceModule).HasMaxLength(100);

            entity.HasOne(x => x.CashAccount).WithMany()
                .HasForeignKey(x => x.CashAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CurrentAccount).WithMany()
                .HasForeignKey(x => x.CurrentAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Project).WithMany()
                .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.AccountingVoucher).WithMany()
                .HasForeignKey(x => x.AccountingVoucherId).OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigurePayrollSettings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CompanyPayrollSettings>(entity =>
        {
            entity.ToTable("company_payroll_settings");
            entity.HasKey(x => x.Id);

            // Silme, AuditSaveChangesInterceptor tarafından soft-delete'e
            // çevrildiği için benzersizlik yalnızca silinmemiş satırlarda
            // aranmalı; aksi halde silinen bir kayıt aynı anahtarın tekrar
            // kullanılmasını kalıcı olarak engeller.
            entity.HasIndex(x => new { x.CompanyId, x.Year })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");

            entity.Property(x => x.MinimumWageGross).HasPrecision(18, 2);
            entity.Property(x => x.MinimumWageNet).HasPrecision(18, 2);
            entity.Property(x => x.SgkBaseFloor).HasPrecision(18, 2);
            entity.Property(x => x.SgkBaseCeiling).HasPrecision(18, 2);
            entity.Property(x => x.SgkEmployeeRate).HasPrecision(9, 4);
            entity.Property(x => x.UnemploymentEmployeeRate).HasPrecision(9, 4);
            entity.Property(x => x.SgkEmployerRate).HasPrecision(9, 4);
            entity.Property(x => x.UnemploymentEmployerRate).HasPrecision(9, 4);
            entity.Property(x => x.SgkEmployerDiscountPoints).HasPrecision(9, 4);
            entity.Property(x => x.StampTaxPerMille).HasPrecision(9, 4);
            entity.Property(x => x.VerificationNote).HasMaxLength(500);

            entity.HasOne(x => x.Company).WithMany()
                .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<PayrollTaxBracket>(entity =>
        {
            entity.ToTable("payroll_tax_brackets");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.CompanyPayrollSettingsId, x.Order })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");

            entity.Property(x => x.LowerBound).HasPrecision(18, 2);
            entity.Property(x => x.UpperBound).HasPrecision(18, 2);
            entity.Property(x => x.Rate).HasPrecision(9, 4);

            entity.HasOne(x => x.CompanyPayrollSettings).WithMany(x => x.TaxBrackets)
                .HasForeignKey(x => x.CompanyPayrollSettingsId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureCheques(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Cheque>(entity =>
        {
            entity.ToTable("cheques");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.CompanyId, x.InternalNumber }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.Direction, x.Status });
            entity.HasIndex(x => x.DueDate);
            entity.HasIndex(x => x.CurrentAccountId);
            entity.HasIndex(x => x.ProgressPaymentId);
            entity.HasIndex(x => x.SupplierInvoiceId);

            entity.Property(x => x.Direction).HasConversion<int>().IsRequired();
            entity.Property(x => x.Status).HasConversion<int>().IsRequired();
            entity.Property(x => x.InternalNumber).HasMaxLength(50).IsRequired();
            entity.Property(x => x.ChequeNumber).HasMaxLength(50).IsRequired();
            entity.Property(x => x.BankName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.BankBranch).HasMaxLength(150);
            entity.Property(x => x.Drawer).HasMaxLength(200);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000);

            entity.HasOne(x => x.Company).WithMany()
                .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CurrentAccount).WithMany()
                .HasForeignKey(x => x.CurrentAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Project).WithMany()
                .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ProgressPayment).WithMany()
                .HasForeignKey(x => x.ProgressPaymentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.SupplierInvoice).WithMany()
                .HasForeignKey(x => x.SupplierInvoiceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CashAccount).WithMany()
                .HasForeignKey(x => x.CashAccountId).OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ChequeMovement>(entity =>
        {
            entity.ToTable("cheque_movements");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.ChequeId, x.MovementDate });

            entity.Property(x => x.FromStatus).HasConversion<int?>();
            entity.Property(x => x.ToStatus).HasConversion<int>().IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000).IsRequired();

            entity.HasOne(x => x.Cheque).WithMany(x => x.Movements)
                .HasForeignKey(x => x.ChequeId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.CashAccount).WithMany()
                .HasForeignKey(x => x.CashAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.AccountingVoucher).WithMany()
                .HasForeignKey(x => x.AccountingVoucherId).OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureFactoringTransactions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FactoringTransaction>(entity =>
        {
            entity.ToTable("factoring_transactions");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.CompanyId, x.InternalNumber }).IsUnique();
            entity.HasIndex(x => x.ChequeId).IsUnique();
            entity.HasIndex(x => x.TransactionDate);

            entity.Property(x => x.InternalNumber).HasMaxLength(50).IsRequired();
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.ChequeAmount).HasPrecision(18, 2);
            entity.Property(x => x.CommissionRate).HasPrecision(9, 4);
            entity.Property(x => x.CommissionAmount).HasPrecision(18, 2);
            entity.Property(x => x.BsmvRate).HasPrecision(9, 4);
            entity.Property(x => x.BsmvAmount).HasPrecision(18, 2);
            entity.Property(x => x.ExpenseAmount).HasPrecision(18, 2);
            entity.Property(x => x.TotalDeductionAmount).HasPrecision(18, 2);
            entity.Property(x => x.NetAmount).HasPrecision(18, 2);
            entity.Property(x => x.Description).HasMaxLength(1000);

            entity.HasOne(x => x.Company).WithMany()
                .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Cheque).WithMany()
                .HasForeignKey(x => x.ChequeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.FactoringCurrentAccount).WithMany()
                .HasForeignKey(x => x.FactoringCurrentAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CashAccount).WithMany()
                .HasForeignKey(x => x.CashAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Project).WithMany()
                .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.AccountingVoucher).WithMany()
                .HasForeignKey(x => x.AccountingVoucherId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CashTransaction).WithMany()
                .HasForeignKey(x => x.CashTransactionId).OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureSupplierInvoices(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SupplierInvoice>(entity =>
        {
            entity.ToTable("supplier_invoices");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.CompanyId, x.InternalNumber }).IsUnique();
            entity.HasIndex(x => new { x.SupplierCurrentAccountId, x.InvoiceNumber });
            entity.HasIndex(x => new { x.CompanyId, x.Status });
            entity.HasIndex(x => x.ProjectId);

            entity.Property(x => x.InternalNumber).HasMaxLength(50).IsRequired();
            entity.Property(x => x.InvoiceNumber).HasMaxLength(100).IsRequired();
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.ExchangeRate).HasPrecision(18, 6);
            entity.Property(x => x.Subtotal).HasPrecision(18, 2);
            entity.Property(x => x.VatTotal).HasPrecision(18, 2);
            entity.Property(x => x.GrandTotal).HasPrecision(18, 2);
            entity.Property(x => x.MatchDifferenceAmount).HasPrecision(18, 2);
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.MatchNote).HasMaxLength(1000);
            entity.Property(x => x.RejectionReason).HasMaxLength(1000);
            entity.Property(x => x.Status).HasConversion<int>().IsRequired();
            entity.Property(x => x.MatchStatus).HasConversion<int>().IsRequired();

            entity.HasOne(x => x.Company).WithMany()
                .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.SupplierCurrentAccount).WithMany()
                .HasForeignKey(x => x.SupplierCurrentAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Project).WithMany()
                .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PurchaseOrder).WithMany()
                .HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.GoodsReceipt).WithMany()
                .HasForeignKey(x => x.GoodsReceiptId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.AccountingVoucher).WithMany()
                .HasForeignKey(x => x.AccountingVoucherId).OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(x => x.Items)
                .WithOne(x => x.SupplierInvoice)
                .HasForeignKey(x => x.SupplierInvoiceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<SupplierInvoiceItem>(entity =>
        {
            entity.ToTable("supplier_invoice_items");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.SupplierInvoiceId, x.LineNumber }).IsUnique();

            entity.Property(x => x.Description).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Unit).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Quantity).HasPrecision(18, 4);
            entity.Property(x => x.UnitPrice).HasPrecision(18, 4);
            entity.Property(x => x.VatRate).HasPrecision(5, 2);
            entity.Property(x => x.LineSubtotal).HasPrecision(18, 2);
            entity.Property(x => x.VatAmount).HasPrecision(18, 2);
            entity.Property(x => x.LineTotal).HasPrecision(18, 2);

            entity.HasOne(x => x.PurchaseOrderItem).WithMany()
                .HasForeignKey(x => x.PurchaseOrderItemId).OnDelete(DeleteBehavior.Restrict);

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

    private static void ConfigureProjectDocuments(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProjectDocument>(entity =>
        {
            entity.ToTable("project_documents");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.ProjectId, x.Folder, x.FileName });
            entity.HasIndex(x => new { x.ProjectId, x.ProjectSiteId });

            entity.Property(x => x.Folder).HasMaxLength(100).IsRequired();
            entity.Property(x => x.FileName).HasMaxLength(300).IsRequired();
            entity.Property(x => x.StoredFileName).HasMaxLength(300).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Extension).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);

            entity.HasOne(x => x.Project)
                .WithMany()
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.ProjectSite)
                .WithMany()
                .HasForeignKey(x => x.ProjectSiteId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.UploadedByUser)
                .WithMany()
                .HasForeignKey(x => x.UploadedByUserId)
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
            entity.Property(x => x.AverageUnitCost).HasPrecision(18, 4);
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
            entity.Property(x => x.UnitCost).HasPrecision(18, 4);
            entity.Property(x => x.TotalCost).HasPrecision(18, 4);
            entity.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.RelatedWarehouse).WithMany().HasForeignKey(x => x.RelatedWarehouseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.InventoryItem).WithMany(x => x.StockMovements).HasForeignKey(x => x.InventoryItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ProjectSite).WithMany().HasForeignKey(x => x.ProjectSiteId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PurchaseRequest).WithMany().HasForeignKey(x => x.PurchaseRequestId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.GoodsReceipt).WithMany().HasForeignKey(x => x.GoodsReceiptId).OnDelete(DeleteBehavior.Restrict);
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

            entity.HasOne(x => x.AccountingVoucher)
                .WithMany()
                .HasForeignKey(x => x.AccountingVoucherId)
                .OnDelete(DeleteBehavior.Restrict);

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

            entity.HasOne(x => x.AccountingAccount)
                .WithMany()
                .HasForeignKey(x => x.AccountingAccountId)
                .OnDelete(DeleteBehavior.Restrict);

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

    private static void ConfigureEmployerPortal(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProjectSiteDailyReport>(entity =>
        {
            entity.ToTable("project_site_daily_reports");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.ProjectSiteId, x.ReportDate }).IsUnique();

            entity.Property(x => x.WeatherCondition).HasMaxLength(100);
            entity.Property(x => x.Notes).HasMaxLength(4000);
            entity.Property(x => x.Status).HasConversion<int>().IsRequired();

            entity.HasOne(x => x.ProjectSite)
                .WithMany()
                .HasForeignKey(x => x.ProjectSiteId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ProjectSiteDailyReportWorkItem>(entity =>
        {
            entity.ToTable("project_site_daily_report_work_items");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => x.DailyReportId);

            entity.Property(x => x.Description).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Unit).HasMaxLength(50);
            entity.Property(x => x.Quantity).HasPrecision(18, 3);

            entity.HasOne(x => x.DailyReport)
                .WithMany(x => x.WorkItems)
                .HasForeignKey(x => x.DailyReportId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ProjectSiteDailyReportPhoto>(entity =>
        {
            entity.ToTable("project_site_daily_report_photos");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => x.DailyReportId);

            entity.Property(x => x.StoredFileName).HasMaxLength(300).IsRequired();
            entity.Property(x => x.OriginalName).HasMaxLength(300).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Caption).HasMaxLength(500);

            entity.HasOne(x => x.DailyReport)
                .WithMany(x => x.Photos)
                .HasForeignKey(x => x.DailyReportId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<EmployerPortalLink>(entity =>
        {
            entity.ToTable("employer_portal_links");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => x.Token).IsUnique();
            entity.HasIndex(x => x.ProjectId);

            entity.Property(x => x.Token).HasMaxLength(200).IsRequired();
            entity.Property(x => x.EmployerName).HasMaxLength(200);
            entity.Property(x => x.EmployerEmail).HasMaxLength(300);

            entity.HasOne(x => x.Project)
                .WithMany()
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<EmployerPortalEmailLog>(entity =>
        {
            entity.ToTable("employer_portal_email_logs");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => x.EmployerPortalLinkId);
            entity.HasIndex(x => x.ProjectId);

            entity.Property(x => x.RecipientEmail).HasMaxLength(300).IsRequired();
            entity.Property(x => x.RecipientName).HasMaxLength(200);
            entity.Property(x => x.ErrorMessage).HasMaxLength(1000);

            entity.HasOne(x => x.EmployerPortalLink)
                .WithMany()
                .HasForeignKey(x => x.EmployerPortalLinkId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureProjectCostTransactions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProjectCostTransaction>(entity =>
        {
            entity.ToTable("ProjectCostTransactions");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => x.ProjectId);
            entity.HasIndex(x => x.ProjectSiteId);

            entity.Property(x => x.CostType).HasConversion<int>();
            entity.Property(x => x.Description).IsRequired();
            entity.Property(x => x.ReferenceType);

            entity.HasOne(x => x.Project)
                .WithMany()
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.ProjectSite)
                .WithMany()
                .HasForeignKey(x => x.ProjectSiteId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.AccountingVoucherLine)
                .WithMany()
                .HasForeignKey(x => x.AccountingVoucherLineId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureHrProjectLaborCosts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HrProjectLaborCost>(entity =>
        {
            entity.ToTable("hr_project_labor_costs");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.ProjectId, x.PersonnelId, x.WorkDate });
            entity.HasIndex(x => x.ProjectSiteId);

            entity.Property(x => x.WorkItemCode).HasMaxLength(100);
            entity.Property(x => x.WorkItemName).HasMaxLength(500);
            entity.Property(x => x.NormalHours).HasPrecision(8, 2);
            entity.Property(x => x.OvertimeHours).HasPrecision(8, 2);
            entity.Property(x => x.SundayHours).HasPrecision(8, 2);
            entity.Property(x => x.PublicHolidayHours).HasPrecision(8, 2);
            entity.Property(x => x.NormalCost).HasPrecision(18, 2);
            entity.Property(x => x.OvertimeCost).HasPrecision(18, 2);
            entity.Property(x => x.SundayCost).HasPrecision(18, 2);
            entity.Property(x => x.PublicHolidayCost).HasPrecision(18, 2);
            entity.Property(x => x.MealCost).HasPrecision(18, 2);
            entity.Property(x => x.AccommodationCost).HasPrecision(18, 2);
            entity.Property(x => x.ShuttleCost).HasPrecision(18, 2);
            entity.Property(x => x.OtherCost).HasPrecision(18, 2);
            entity.Property(x => x.CompensationCost).HasPrecision(18, 2);
            entity.Property(x => x.TotalLaborCost).HasPrecision(18, 2);
            entity.Property(x => x.CurrencyCode).HasMaxLength(10).IsRequired();

            entity.HasOne(x => x.ProjectSite)
                .WithMany()
                .HasForeignKey(x => x.ProjectSiteId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureWorkTasks(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkTask>(entity =>
        {
            entity.ToTable("WorkTasks");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TaskNumber).IsRequired();
            entity.Property(x => x.Title).IsRequired();
            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureAttendanceRecords(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AttendanceRecord>(entity =>
        {
            entity.ToTable("attendance_records");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CompanyId, x.PersonnelId, x.WorkDate }).IsUnique();
            entity.HasIndex(x => new { x.PersonnelId, x.Status });
            entity.HasIndex(x => new { x.ProjectId, x.WorkDate });
            entity.Property(x => x.NormalHours).HasPrecision(8, 2);
            entity.Property(x => x.OvertimeHours).HasPrecision(8, 2);
            entity.Property(x => x.NightShiftHours).HasPrecision(8, 2);
            entity.Property(x => x.SundayHours).HasPrecision(8, 2);
            entity.Property(x => x.PublicHolidayHours).HasPrecision(8, 2);
            entity.Property(x => x.TotalHours).HasPrecision(8, 2);
            entity.Property(x => x.TeamName).HasMaxLength(200);
            entity.Property(x => x.RoleName).HasMaxLength(150);
            entity.Property(x => x.WorkItemCode).HasMaxLength(100);
            entity.Property(x => x.WorkItemName).HasMaxLength(500);
            entity.Property(x => x.LocationName).HasMaxLength(300);
            entity.Property(x => x.Description).HasMaxLength(4000);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureHrCompensationComponents(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HrCompensationComponent>(entity =>
        {
            entity.ToTable("hr_compensation_components");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.PersonnelId, x.IsActive, x.EffectiveStartDate, x.EffectiveEndDate });
            entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureHrCareerHistories(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HrCareerHistory>(entity =>
        {
            entity.ToTable("hr_career_histories");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.PersonnelId, x.EffectiveDate });
            entity.Property(x => x.ActionType).HasConversion<int>();
            entity.Property(x => x.PreviousSalary).HasPrecision(18, 2);
            entity.Property(x => x.NewSalary).HasPrecision(18, 2);
            entity.Property(x => x.Reason).HasMaxLength(2000);
            entity.Property(x => x.ApprovedByName).HasMaxLength(250);
            entity.Property(x => x.Notes).HasMaxLength(4000);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureHrShifts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HrShiftDefinition>(entity =>
        {
            entity.ToTable("hr_shift_definitions");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.BreakHours).HasPrecision(8, 2);
            entity.Property(x => x.DailyWorkingHours).HasPrecision(8, 2);
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<HrShiftAssignment>(entity =>
        {
            entity.ToTable("hr_shift_assignments");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.PersonnelId, x.StartDate, x.EndDate });
            entity.Property(x => x.TeamName).HasMaxLength(200);
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureHrAssetAssignments(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HrAssetAssignment>(entity =>
        {
            entity.ToTable("hr_asset_assignments");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.PersonnelId, x.Status });
            entity.Property(x => x.AssetType).HasMaxLength(150).IsRequired();
            entity.Property(x => x.AssetCode).HasMaxLength(150).IsRequired();
            entity.Property(x => x.AssetName).HasMaxLength(300).IsRequired();
            entity.Property(x => x.SerialNumber).HasMaxLength(200);
            entity.Property(x => x.ConditionAtAssignment).HasMaxLength(2000);
            entity.Property(x => x.ConditionAtReturn).HasMaxLength(2000);
            entity.Property(x => x.DocumentPath).HasMaxLength(1000);
            entity.Property(x => x.Notes).HasMaxLength(4000);
            entity.Property(x => x.InventoryQuantity).HasPrecision(18, 4);
            entity.Property(x => x.IssuedUnitCost).HasPrecision(18, 6);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureProjectMeasurements(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProjectMeasurement>(entity =>
        {
            entity.ToTable("project_measurements");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CompanyId, x.MeasurementNumber }).IsUnique();
            entity.HasIndex(x => x.ProjectBoqId);
            entity.HasIndex(x => new { x.ProjectId, x.MeasurementDate });
            entity.Property(x => x.MeasurementNumber).HasMaxLength(50).IsRequired();
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.TotalAmount).HasPrecision(18, 2);
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.Property(x => x.CancellationReason).HasMaxLength(1000);

            entity.HasOne(x => x.Project)
                .WithMany()
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.ProjectBoq)
                .WithMany()
                .HasForeignKey(x => x.ProjectBoqId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ProjectMeasurementItem>(entity =>
        {
            entity.ToTable("project_measurement_items");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.EngineeringPositionId);
            entity.HasIndex(x => x.ProjectBoqItemId);
            entity.HasIndex(x => new { x.ProjectMeasurementId, x.LineNumber }).IsUnique();
            entity.HasIndex(x => new { x.ProjectMeasurementId, x.ProjectBoqItemId }).IsUnique();
            entity.Property(x => x.PositionCode).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Unit).HasMaxLength(30).IsRequired();
            entity.Property(x => x.ContractQuantity).HasPrecision(18, 4);
            entity.Property(x => x.PreviousQuantity).HasPrecision(18, 4);
            entity.Property(x => x.CurrentQuantity).HasPrecision(18, 4);
            entity.Property(x => x.CumulativeQuantity).HasPrecision(18, 4);
            entity.Property(x => x.RemainingQuantity).HasPrecision(18, 4);
            entity.Property(x => x.UnitPrice).HasPrecision(18, 4);
            entity.Property(x => x.CurrentAmount).HasPrecision(18, 2);
            entity.Property(x => x.CumulativeAmount).HasPrecision(18, 2);
            entity.Property(x => x.CompletionRate).HasPrecision(8, 4);
            entity.Property(x => x.MeasurementReference).HasMaxLength(250);
            entity.Property(x => x.Location).HasMaxLength(250);
            entity.Property(x => x.Block).HasMaxLength(100);
            entity.Property(x => x.Floor).HasMaxLength(100);
            entity.Property(x => x.Room).HasMaxLength(100);
            entity.Property(x => x.Notes).HasMaxLength(1000);

            entity.HasOne(x => x.ProjectMeasurement)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.ProjectMeasurementId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.ProjectBoqItem)
                .WithMany()
                .HasForeignKey(x => x.ProjectBoqItemId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureHrRecruitment(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<JobPosting>(entity =>
        {
            entity.ToTable("hr_job_postings");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CompanyId, x.PostingNumber }).IsUnique();
            entity.Property(x => x.PostingNumber).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(300).IsRequired();
            entity.Property(x => x.LocationName).HasMaxLength(250);
            entity.Property(x => x.EmploymentType).HasMaxLength(100);
            entity.Property(x => x.Description).HasMaxLength(6000).IsRequired();
            entity.Property(x => x.Requirements).HasMaxLength(6000);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<JobCandidate>(entity =>
        {
            entity.ToTable("hr_job_candidates");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Email);
            entity.HasIndex(x => x.IdentityNumber);
            entity.Property(x => x.FirstName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.LastName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.IdentityNumber).HasMaxLength(50);
            entity.Property(x => x.PhoneNumber).HasMaxLength(50);
            entity.Property(x => x.Email).HasMaxLength(250);
            entity.Property(x => x.City).HasMaxLength(150);
            entity.Property(x => x.Profession).HasMaxLength(250);
            entity.Property(x => x.CurrentCompany).HasMaxLength(300);
            entity.Property(x => x.EducationLevel).HasMaxLength(150);
            entity.Property(x => x.CvFilePath).HasMaxLength(1000);
            entity.Property(x => x.Source).HasMaxLength(200);
            entity.Property(x => x.Notes).HasMaxLength(4000);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<JobApplication>(entity =>
        {
            entity.ToTable("hr_job_applications");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.JobPostingId, x.CandidateId }).IsUnique();
            entity.Property(x => x.ExpectedSalary).HasPrecision(18, 2);
            entity.Property(x => x.CurrencyCode).HasMaxLength(10).IsRequired();
            entity.Property(x => x.EvaluationNote).HasMaxLength(4000);

            entity.HasOne(x => x.JobPosting)
                .WithMany(x => x.Applications)
                .HasForeignKey(x => x.JobPostingId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Candidate)
                .WithMany()
                .HasForeignKey(x => x.CandidateId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<CandidateInterview>(entity =>
        {
            entity.ToTable("hr_candidate_interviews");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.JobApplicationId, x.PlannedAtUtc });
            entity.Property(x => x.InterviewType).HasMaxLength(100);
            entity.Property(x => x.LocationOrLink).HasMaxLength(1000);
            entity.Property(x => x.InterviewerName).HasMaxLength(250);
            entity.Property(x => x.Score).HasPrecision(8, 2);
            entity.Property(x => x.Strengths).HasMaxLength(4000);
            entity.Property(x => x.Weaknesses).HasMaxLength(4000);
            entity.Property(x => x.EvaluationNote).HasMaxLength(4000);

            entity.HasOne(x => x.JobApplication)
                .WithMany(x => x.Interviews)
                .HasForeignKey(x => x.JobApplicationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureWorkHourAccess(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RoleWorkHourWindow>(entity =>
        {
            entity.ToTable("role_work_hour_windows");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.RoleId, x.DayOfWeek });

            entity.HasOne(x => x.Role)
                .WithMany()
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AccessRequest>(entity =>
        {
            entity.ToTable("access_requests");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.UserId, x.Status });

            entity.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Status).HasConversion<int>().IsRequired();
            entity.Property(x => x.RejectionReason).HasMaxLength(1000);

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<TemporaryAccessGrant>(entity =>
        {
            entity.ToTable("temporary_access_grants");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.UserId, x.ExpiresAtUtc });

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.SourceAccessRequest)
                .WithMany()
                .HasForeignKey(x => x.SourceAccessRequestId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }
}
