using EnderunAI.Api.Models;
using EnderunAI.Api.Models.GoodsReceipt;
using EnderunAI.Api.Models.Market;
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
    public DbSet<CompanyCorporateTaxRate> CompanyCorporateTaxRates =>
        Set<CompanyCorporateTaxRate>();
    public DbSet<PersonnelRehireOverride> PersonnelRehireOverrides =>
        Set<PersonnelRehireOverride>();
    public DbSet<PersonnelDuty> PersonnelDuties => Set<PersonnelDuty>();
    public DbSet<CashFlowEstimatedExpense> CashFlowEstimatedExpenses =>
        Set<CashFlowEstimatedExpense>();
    public DbSet<DutySurveyReport> DutySurveyReports => Set<DutySurveyReport>();
    public DbSet<DutySurveyMeasurement> DutySurveyMeasurements => Set<DutySurveyMeasurement>();
    public DbSet<DutySurveyPhoto> DutySurveyPhotos => Set<DutySurveyPhoto>();
    public DbSet<CashAccount> CashAccounts => Set<CashAccount>();
    public DbSet<CashTransaction> CashTransactions => Set<CashTransaction>();
    public DbSet<CurrencyValuationRun> CurrencyValuationRuns =>
        Set<CurrencyValuationRun>();
    public DbSet<CurrencyValuationRunLine> CurrencyValuationRunLines =>
        Set<CurrencyValuationRunLine>();
    public DbSet<HizirConversation> HizirConversations => Set<HizirConversation>();
    public DbSet<HizirMessage> HizirMessages => Set<HizirMessage>();
    public DbSet<HizirPendingAction> HizirPendingActions => Set<HizirPendingAction>();
    public DbSet<CompanyPayrollSettings> CompanyPayrollSettings =>
        Set<CompanyPayrollSettings>();
    public DbSet<Models.HumanResources.CompanyHolidayCalendar> CompanyHolidayCalendars =>
        Set<Models.HumanResources.CompanyHolidayCalendar>();
    public DbSet<Models.HumanResources.CompanyHoliday> CompanyHolidays =>
        Set<Models.HumanResources.CompanyHoliday>();
    public DbSet<Models.HumanResources.PersonnelDocument> PersonnelDocuments =>
        Set<Models.HumanResources.PersonnelDocument>();
    public DbSet<PayrollTaxBracket> PayrollTaxBrackets => Set<PayrollTaxBracket>();
    public DbSet<Cheque> Cheques => Set<Cheque>();
    public DbSet<ChequeMovement> ChequeMovements => Set<ChequeMovement>();
    public DbSet<ChequeAllocation> ChequeAllocations => Set<ChequeAllocation>();
    public DbSet<TaxPayment> TaxPayments => Set<TaxPayment>();
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
    public DbSet<PositionUnitPrice> PositionUnitPrices => Set<PositionUnitPrice>();
    public DbSet<EngineeringRecipe> EngineeringRecipes => Set<EngineeringRecipe>();
    public DbSet<EngineeringRecipeMaterial> EngineeringRecipeMaterials => Set<EngineeringRecipeMaterial>();
    public DbSet<EngineeringRecipeLabor> EngineeringRecipeLabors => Set<EngineeringRecipeLabor>();
    public DbSet<EngineeringRecipeMachine> EngineeringRecipeMachines => Set<EngineeringRecipeMachine>();
    public DbSet<Personnel> Personnel => Set<Personnel>();

    /// <summary>
    /// Elden ödeme tutarları. Ayrı tablo tutulmasının sebebi izolasyon:
    /// extra_payment.view olmayan kullanıcının sorgusu buraya uğramaz.
    /// </summary>
    public DbSet<PersonnelExtraPayment> PersonnelExtraPayments =>
        Set<PersonnelExtraPayment>();
    public DbSet<PersonnelCashPaymentEntry> PersonnelCashPayments =>
        Set<PersonnelCashPaymentEntry>();

    public DbSet<PersonnelTermination> PersonnelTerminations =>
        Set<PersonnelTermination>();

    public DbSet<SubcontractorContract> SubcontractorContracts =>
        Set<SubcontractorContract>();

    public DbSet<SubcontractorContractSection> SubcontractorContractSections =>
        Set<SubcontractorContractSection>();

    public DbSet<SubcontractorProgressPayment> SubcontractorProgressPayments =>
        Set<SubcontractorProgressPayment>();

    public DbSet<SubcontractorProgressPaymentItem>
        SubcontractorProgressPaymentItems =>
        Set<SubcontractorProgressPaymentItem>();

    public DbSet<SubcontractorProgressPaymentSection>
        SubcontractorProgressPaymentSections =>
        Set<SubcontractorProgressPaymentSection>();

    public DbSet<SubcontractorProgressPaymentDeduction>
        SubcontractorProgressPaymentDeductions =>
        Set<SubcontractorProgressPaymentDeduction>();

    public DbSet<SubcontractorLedgerEntry> SubcontractorLedgerEntries =>
        Set<SubcontractorLedgerEntry>();

    /// <summary>
    /// Taşerona elden ödeme ve elden avans. Ayrı tablo tutulmasının
    /// sebebi izolasyon: extra_payment.view olmayan kullanıcının
    /// sorgusu buraya uğramaz.
    /// </summary>
    public DbSet<SubcontractorCashLedgerEntry> SubcontractorCashLedgerEntries =>
        Set<SubcontractorCashLedgerEntry>();

    public DbSet<SubcontractorDocument> SubcontractorDocuments =>
        Set<SubcontractorDocument>();

    public DbSet<ProjectHakedisSection> ProjectHakedisSections =>
        Set<ProjectHakedisSection>();

    public DbSet<ProgressPaymentSection> ProgressPaymentSections =>
        Set<ProgressPaymentSection>();

    public DbSet<ProgressPaymentAdvanceMaterial> ProgressPaymentAdvanceMaterials =>
        Set<ProgressPaymentAdvanceMaterial>();

    public DbSet<ProgressPaymentAdvanceMaterialOffset> ProgressPaymentAdvanceMaterialOffsets =>
        Set<ProgressPaymentAdvanceMaterialOffset>();

    public DbSet<ProgressPaymentDeductionLine> ProgressPaymentDeductionLines =>
        Set<ProgressPaymentDeductionLine>();

    public DbSet<BarterLedgerEntry> BarterLedgerEntries =>
        Set<BarterLedgerEntry>();

    public DbSet<ProgressPaymentPaymentPlan> ProgressPaymentPaymentPlans =>
        Set<ProgressPaymentPaymentPlan>();

    public DbSet<HakedisDeductionAccountMapping> HakedisDeductionAccountMappings =>
        Set<HakedisDeductionAccountMapping>();

    public DbSet<ProjectExtraWork> ProjectExtraWorks =>
        Set<ProjectExtraWork>();

    public DbSet<SalesInvoice> SalesInvoices => Set<SalesInvoice>();

    public DbSet<SalesInvoiceItem> SalesInvoiceItems => Set<SalesInvoiceItem>();

    // İş sağlığı ve güvenliği
    public DbSet<IsgOsgbContract> IsgOsgbContracts => Set<IsgOsgbContract>();
    public DbSet<IsgOsgbExpert> IsgOsgbExperts => Set<IsgOsgbExpert>();
    public DbSet<IsgHealthReport> IsgHealthReports => Set<IsgHealthReport>();
    public DbSet<IsgTraining> IsgTrainings => Set<IsgTraining>();
    public DbSet<IsgCertificate> IsgCertificates => Set<IsgCertificate>();
    public DbSet<IsgIncident> IsgIncidents => Set<IsgIncident>();
    public DbSet<IsgSiteDocument> IsgSiteDocuments => Set<IsgSiteDocument>();
    public DbSet<PersonnelAssignment> PersonnelAssignments => Set<PersonnelAssignment>();
    public DbSet<PurchaseRequest> PurchaseRequests => Set<PurchaseRequest>();
    public DbSet<PurchaseRequestItem> PurchaseRequestItems => Set<PurchaseRequestItem>();
    public DbSet<Models.GoodsReceipt.PurchaseReturn> PurchaseReturns =>
        Set<Models.GoodsReceipt.PurchaseReturn>();
    public DbSet<Models.GoodsReceipt.PurchaseReturnItem> PurchaseReturnItems =>
        Set<Models.GoodsReceipt.PurchaseReturnItem>();
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
    public DbSet<ToolAsset> ToolAssets => Set<ToolAsset>();
    public DbSet<ToolServiceRequest> ToolServiceRequests =>
        Set<ToolServiceRequest>();
    public DbSet<Models.Schedule.ProjectSchedule> ProjectSchedules =>
        Set<Models.Schedule.ProjectSchedule>();
    public DbSet<Models.Schedule.ScheduleActivity> ScheduleActivities =>
        Set<Models.Schedule.ScheduleActivity>();
    public DbSet<Models.Schedule.ScheduleDependency> ScheduleDependencies =>
        Set<Models.Schedule.ScheduleDependency>();
    public DbSet<Models.Schedule.ScheduleBaselineRevision> ScheduleBaselineRevisions =>
        Set<Models.Schedule.ScheduleBaselineRevision>();
    public DbSet<Models.Schedule.ScheduleHoliday> ScheduleHolidays =>
        Set<Models.Schedule.ScheduleHoliday>();
    public DbSet<Models.Schedule.ScheduleResourceAssignment> ScheduleResourceAssignments =>
        Set<Models.Schedule.ScheduleResourceAssignment>();
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
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<CommodityPrice> CommodityPrices => Set<CommodityPrice>();
    public DbSet<CommodityAlertThreshold> CommodityAlertThresholds =>
        Set<CommodityAlertThreshold>();
    public DbSet<CommodityAlertTrigger> CommodityAlertTriggers =>
        Set<CommodityAlertTrigger>();
    public DbSet<ProjectCopperExposure> ProjectCopperExposures =>
        Set<ProjectCopperExposure>();

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
        ConfigureCompanyCorporateTaxRates(modelBuilder);
        ConfigurePersonnelRehireOverrides(modelBuilder);
        ConfigurePersonnelDuties(modelBuilder);
        ConfigureDutySurveyReports(modelBuilder);
        ConfigureCashFlowEstimatedExpenses(modelBuilder);
        ConfigureCurrencyValuation(modelBuilder);
        ConfigureCashAccounts(modelBuilder);
        ConfigurePayrollSettings(modelBuilder);
        ConfigureHizir(modelBuilder);
        ConfigureCheques(modelBuilder);
        ConfigureFactoringTransactions(modelBuilder);
        ConfigureSupplierInvoices(modelBuilder);
        ConfigureAccountingVoucherLines(modelBuilder);
        ConfigureProjects(modelBuilder);
        ConfigureProjectDocuments(modelBuilder);
        ConfigureSubcontractorContracts(modelBuilder);
        ConfigureSubcontractorProgressPayments(modelBuilder);
        ConfigureSubcontractorLedger(modelBuilder);
        ConfigureSubcontractorDocuments(modelBuilder);
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
        ConfigureMarketData(modelBuilder);
        ConfigureRbac(modelBuilder);
        ConfigureWorkHourAccess(modelBuilder);
        ConfigureIsg(modelBuilder);
    }

    private static void ConfigureIsg(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IsgOsgbContract>(entity =>
        {
            entity.ToTable("isg_osgb_contracts");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.CompanyId, x.ContractNumber })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");
            entity.HasIndex(x => new { x.CompanyId, x.StartDate });

            entity.Property(x => x.ContractNumber).HasMaxLength(80).IsRequired();
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.BillingType).HasConversion<int>();
            entity.Property(x => x.MonthlyFee).HasPrecision(18, 2);
            entity.Property(x => x.PerPersonFee).HasPrecision(18, 2);
            entity.Property(x => x.Notes).HasMaxLength(2000);

            entity.HasOne(x => x.Company).WithMany()
                .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CurrentAccount).WithMany()
                .HasForeignKey(x => x.CurrentAccountId).OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<IsgOsgbExpert>(entity =>
        {
            entity.ToTable("isg_osgb_contract_experts");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => x.IsgOsgbContractId);

            entity.Property(x => x.ExpertType).HasConversion<int>();
            entity.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.CertificateNumber).HasMaxLength(60);
            entity.Property(x => x.ExpertClass).HasMaxLength(5);
            entity.Property(x => x.Phone).HasMaxLength(30);
            entity.Property(x => x.Email).HasMaxLength(200);

            entity.HasOne(x => x.IsgOsgbContract).WithMany(x => x.Experts)
                .HasForeignKey(x => x.IsgOsgbContractId).OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<IsgHealthReport>(entity =>
        {
            entity.ToTable("isg_health_reports");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.PersonnelId, x.ExamDate });
            // Süresi dolanları taramak panelin ve brifingin ana sorgusu.
            entity.HasIndex(x => new { x.CompanyId, x.ValidUntil });

            entity.Property(x => x.ReportType).HasConversion<int>();
            entity.Property(x => x.Result).HasConversion<int>();
            entity.Property(x => x.DoctorName).HasMaxLength(200);
            entity.Property(x => x.Restrictions).HasMaxLength(1000);
            entity.Property(x => x.DoctorNotes).HasMaxLength(2000);
            entity.Property(x => x.DocumentPath).HasMaxLength(500);

            entity.HasOne(x => x.Company).WithMany()
                .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Personnel).WithMany()
                .HasForeignKey(x => x.PersonnelId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.IsgOsgbContract).WithMany()
                .HasForeignKey(x => x.IsgOsgbContractId).OnDelete(DeleteBehavior.SetNull);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<IsgTraining>(entity =>
        {
            entity.ToTable("isg_trainings");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.PersonnelId, x.TrainingDate });
            entity.HasIndex(x => new { x.CompanyId, x.ValidUntil });

            entity.Property(x => x.TrainingType).HasConversion<int>();
            entity.Property(x => x.Topic).HasMaxLength(300).IsRequired();
            entity.Property(x => x.DurationHours).HasPrecision(8, 2);
            entity.Property(x => x.TrainerName).HasMaxLength(200);
            entity.Property(x => x.DocumentPath).HasMaxLength(500);
            entity.Property(x => x.Notes).HasMaxLength(1000);

            entity.HasOne(x => x.Company).WithMany()
                .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Personnel).WithMany()
                .HasForeignKey(x => x.PersonnelId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.IsgOsgbContract).WithMany()
                .HasForeignKey(x => x.IsgOsgbContractId).OnDelete(DeleteBehavior.SetNull);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<IsgCertificate>(entity =>
        {
            entity.ToTable("isg_certificates");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.PersonnelId, x.CertificateType });
            entity.HasIndex(x => new { x.CompanyId, x.ExpiryDate });

            entity.Property(x => x.CertificateType).HasConversion<int>();
            entity.Property(x => x.CustomTypeName).HasMaxLength(200);
            entity.Property(x => x.CertificateNumber).HasMaxLength(100);
            entity.Property(x => x.IssuedBy).HasMaxLength(200);
            entity.Property(x => x.DocumentPath).HasMaxLength(500);
            entity.Property(x => x.Notes).HasMaxLength(1000);

            entity.HasOne(x => x.Company).WithMany()
                .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Personnel).WithMany()
                .HasForeignKey(x => x.PersonnelId).OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<IsgIncident>(entity =>
        {
            entity.ToTable("isg_incidents");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.CompanyId, x.IncidentDateTime });
            entity.HasIndex(x => new { x.CompanyId, x.Status });
            // SGK bildirimi yapılmamış kayıtlar panelde kritik olarak
            // taranıyor; indeks o sorgu için.
            entity.HasIndex(x => new { x.CompanyId, x.SgkNotified });
            entity.HasIndex(x => x.ProjectSiteId);

            entity.Property(x => x.IncidentType).HasConversion<int>();
            entity.Property(x => x.Severity).HasConversion<int>();
            entity.Property(x => x.Status).HasConversion<int>();
            entity.Property(x => x.Description).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.RootCause).HasMaxLength(2000);
            entity.Property(x => x.ActionTaken).HasMaxLength(2000);
            entity.Property(x => x.SgkNotificationNumber).HasMaxLength(100);
            entity.Property(x => x.ClosureNote).HasMaxLength(1000);

            entity.HasOne(x => x.Company).WithMany()
                .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Project).WithMany()
                .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.ProjectSite).WithMany()
                .HasForeignKey(x => x.ProjectSiteId).OnDelete(DeleteBehavior.SetNull);
            // Personel silinse bile kaza kaydı durur — yasal kayıt.
            entity.HasOne(x => x.Personnel).WithMany()
                .HasForeignKey(x => x.PersonnelId).OnDelete(DeleteBehavior.SetNull);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<IsgSiteDocument>(entity =>
        {
            entity.ToTable("isg_site_documents");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.CompanyId, x.ValidUntil });
            entity.HasIndex(x => new { x.ProjectId, x.DocumentType });
            entity.HasIndex(x => x.ProjectSiteId);

            entity.Property(x => x.DocumentType).HasConversion<int>();
            entity.Property(x => x.Title).HasMaxLength(300).IsRequired();
            entity.Property(x => x.StoredFileName).HasMaxLength(300).IsRequired();
            entity.Property(x => x.OriginalFileName).HasMaxLength(300).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(1000);

            entity.HasOne(x => x.Company).WithMany()
                .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Project).WithMany()
                .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ProjectSite).WithMany()
                .HasForeignKey(x => x.ProjectSiteId).OnDelete(DeleteBehavior.SetNull);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
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

    private static void ConfigureMarketData(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ExchangeRate>(entity =>
        {
            entity.ToTable("exchange_rates");
            entity.HasKey(x => x.Id);

            // Aynı gün ve para birimi için ikinci kayıt olamaz; olsaydı
            // hangi kurun kullanıldığı kayda göre değişirdi.
            entity.HasIndex(x => new { x.RateDate, x.CurrencyCode })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");

            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Source).HasMaxLength(40).IsRequired();
            entity.Property(x => x.BulletinNumber).HasMaxLength(40);

            // TCMB kurları dört haneden fazla ondalık yayımlamıyor; altı
            // hane, 100 birim üzerinden kote edilenlerin bire indirgenmesi
            // sırasındaki bölme için pay bırakır.
            entity.Property(x => x.ForexBuying).HasPrecision(18, 6);
            entity.Property(x => x.ForexSelling).HasPrecision(18, 6);
            entity.Property(x => x.BanknoteBuying).HasPrecision(18, 6);
            entity.Property(x => x.BanknoteSelling).HasPrecision(18, 6);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<CommodityPrice>(entity =>
        {
            entity.ToTable("commodity_prices");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.Commodity, x.PriceDate })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");

            entity.Property(x => x.Commodity).HasConversion<int>().IsRequired();
            entity.Property(x => x.SourceKind).HasConversion<int>().IsRequired();
            entity.Property(x => x.SourceSymbol).HasMaxLength(40).IsRequired();

            entity.Property(x => x.PriceUsdPerTon).HasPrecision(18, 2);
            entity.Property(x => x.PriceTryPerTon).HasPrecision(18, 2);
            entity.Property(x => x.UsdRate).HasPrecision(18, 6);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<CommodityAlertThreshold>(entity =>
        {
            entity.ToTable("commodity_alert_thresholds");
            entity.HasKey(x => x.Id);

            // Şirket başına emtia başına tek eşik.
            entity.HasIndex(x => new { x.CompanyId, x.Commodity })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");

            entity.Property(x => x.Commodity).HasConversion<int>().IsRequired();
            entity.Property(x => x.BuyBelowUsdPerTon).HasPrecision(18, 2);
            entity.Property(x => x.AlertAboveUsdPerTon).HasPrecision(18, 2);
            entity.Property(x => x.Notes).HasMaxLength(1000);

            entity.HasOne(x => x.Company).WithMany()
                .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<CommodityAlertTrigger>(entity =>
        {
            entity.ToTable("commodity_alert_triggers");
            entity.HasKey(x => x.Id);

            // Aynı eşik + gün + yön ikinci kez yazılamaz: değerlendirme
            // idempotent, gecelik iş birden fazla koşsa da uyarı çoğalmaz.
            entity.HasIndex(x => new
                {
                    x.CommodityAlertThresholdId, x.PriceDate, x.Direction
                })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");

            entity.Property(x => x.Direction).HasConversion<int>().IsRequired();
            entity.Property(x => x.PriceUsdPerTon).HasPrecision(18, 2);
            entity.Property(x => x.PriceTryPerTon).HasPrecision(18, 2);
            entity.Property(x => x.ThresholdUsdPerTon).HasPrecision(18, 2);

            entity.HasOne(x => x.CommodityAlertThreshold).WithMany(x => x.Triggers)
                .HasForeignKey(x => x.CommodityAlertThresholdId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ProjectCopperExposure>(entity =>
        {
            entity.ToTable("project_copper_exposures");
            entity.HasKey(x => x.Id);

            // Proje başına tek maruziyet kaydı; ikincisi olsaydı hangi
            // tonajın geçerli olduğu kayda göre değişirdi.
            entity.HasIndex(x => x.ProjectId)
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");

            entity.Property(x => x.RemainingTons).HasPrecision(18, 3);
            entity.Property(x => x.Note).HasMaxLength(500);

            entity.HasOne(x => x.Project)
                .WithMany()
                .HasForeignKey(x => x.ProjectId)
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
            entity.Property(x => x.Honorific).HasMaxLength(10);
            entity.Property(x => x.Email).HasMaxLength(200);
            entity.Property(x => x.PasswordHash).IsRequired();
            entity.Property(x => x.PasswordSalt).IsRequired();

            // Bir personel kartına en fazla bir kullanıcı bağlanabilir;
            // aksi halde "kendi kaydım" iki kullanıcıya açılırdı. Filtre
            // gerekli: bağı olmayan kullanıcılarda null tekrar eder.
            entity.HasIndex(x => x.PersonnelId)
                .IsUnique()
                .HasFilter("\"PersonnelId\" IS NOT NULL");

            // Personel silinirse kullanıcı silinmez, bağ kopar.
            entity.HasOne(x => x.Personnel)
                .WithMany()
                .HasForeignKey(x => x.PersonnelId)
                .OnDelete(DeleteBehavior.SetNull);
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

    private static void ConfigureCurrencyValuation(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CurrencyValuationRun>(entity =>
        {
            entity.ToTable("currency_valuation_runs");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.PostedDifference).HasPrecision(18, 2);

            // Aynı şirket + tarih için birden fazla İPTAL EDİLMEMİŞ tur
            // olmamalı; kontrol serviste, burada arama için indeks.
            entity.HasIndex(x => new { x.CompanyId, x.ValuationDate });

            entity.HasOne(x => x.Company).WithMany()
                .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.AccountingVoucher).WithMany()
                .HasForeignKey(x => x.AccountingVoucherId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<CurrencyValuationRunLine>(entity =>
        {
            entity.ToTable("currency_valuation_run_lines");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.CurrencyCode).HasMaxLength(8).IsRequired();
            entity.Property(x => x.Balance).HasPrecision(18, 2);
            entity.Property(x => x.BookValueLocal).HasPrecision(18, 2);
            entity.Property(x => x.ValuationRate).HasPrecision(18, 6);
            entity.Property(x => x.ValuedLocal).HasPrecision(18, 2);
            entity.Property(x => x.TotalDifference).HasPrecision(18, 2);
            entity.Property(x => x.PostedDifference).HasPrecision(18, 2);

            // Kümülatif düzeltme sorgusunun taradığı yol.
            entity.HasIndex(x => new { x.CurrentAccountId, x.CurrencyCode });

            entity.HasOne(x => x.CurrencyValuationRun).WithMany(x => x.Lines)
                .HasForeignKey(x => x.CurrencyValuationRunId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.CurrentAccount).WithMany()
                .HasForeignKey(x => x.CurrentAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigurePersonnelDuties(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PersonnelDuty>(entity =>
        {
            entity.ToTable("personnel_duties");
            entity.HasKey(x => x.Id);

            // Çakışma kontrolü ve gün dağıtımı bu iki eksenden
            // sorgulanıyor.
            entity.HasIndex(x => new { x.PersonnelId, x.StartDate });
            entity.HasIndex(x => x.TargetProjectId);

            entity.Property(x => x.DailyAllowance).HasPrecision(18, 2);
            entity.Property(x => x.Purpose).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.Property(x => x.DecisionNote).HasMaxLength(1000);
            entity.Property(x => x.AllowanceRevisionNote).HasMaxLength(1000);

            // Hesaplanan alanlar veritabanına yazılmaz.
            entity.Ignore(x => x.DayCount);
            entity.Ignore(x => x.TotalAllowance);
            entity.Ignore(x => x.ShiftsLaborCost);
            entity.Ignore(x => x.TotalExpense);
            entity.Ignore(x => x.SettlementGap);
            entity.Ignore(x => x.SettlementPending);

            entity.HasOne(x => x.Personnel)
                .WithMany()
                .HasForeignKey(x => x.PersonnelId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.TargetProject)
                .WithMany()
                .HasForeignKey(x => x.TargetProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.SourceProject)
                .WithMany()
                .HasForeignKey(x => x.SourceProjectId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.TargetProjectSite)
                .WithMany()
                .HasForeignKey(x => x.TargetProjectSiteId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureCashFlowEstimatedExpenses(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CashFlowEstimatedExpense>(entity =>
        {
            entity.ToTable("cash_flow_estimated_expenses");
            entity.HasKey(x => x.Id);

            // Projeksiyon şirket + dönem üzerinden okuyor.
            entity.HasIndex(x => new { x.CompanyId, x.StartYear, x.StartMonth });

            entity.Property(x => x.Description).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Amount).HasPrecision(18, 2);

            entity.HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Project)
                .WithMany()
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureDutySurveyReports(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DutySurveyReport>(entity =>
        {
            entity.ToTable("duty_survey_reports");
            entity.HasKey(x => x.Id);

            // Silinen kayıt okunmaz: tabloda kalır, listelerden düşer.
            entity.HasQueryFilter(x => !x.IsDeleted);

            // Görev başına tek rapor: ikinci bir rapor "hangisi
            // geçerli" sorusunu doğururdu. Düzeltme aynı kaydın
            // üzerine yazılır.
            entity.HasIndex(x => x.DutyId).IsUnique();

            // Bir projenin keşif dosyası proje üzerinden okunuyor.
            entity.HasIndex(x => x.ProjectId);

            entity.Property(x => x.Summary).HasMaxLength(8000).IsRequired();
            entity.Property(x => x.SiteConditions).HasMaxLength(4000);
            entity.Property(x => x.AccessNotes).HasMaxLength(4000);
            entity.Property(x => x.Risks).HasMaxLength(4000);

            entity.HasOne(x => x.Duty)
                .WithMany()
                .HasForeignKey(x => x.DutyId)
                .OnDelete(DeleteBehavior.Cascade);

            // Rapor ARŞİVDE KALIR: proje kaybedilse de silinmez.
            // Restrict, projeyi silmeye çalışan bir akışın raporu
            // sessizce götürmesini engeller.
            entity.HasOne(x => x.Project)
                .WithMany()
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DutySurveyMeasurement>(entity =>
        {
            entity.ToTable("duty_survey_measurements");
            entity.HasKey(x => x.Id);

            // Ölçüm listesi her kayıtta bütün olarak yenileniyor;
            // filtresiz kalsaydı eski sürümler rapora karışırdı.
            entity.HasQueryFilter(x => !x.IsDeleted);
            entity.HasIndex(x => x.SurveyReportId);

            entity.Property(x => x.Description).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Quantity).HasPrecision(18, 4);
            entity.Property(x => x.Unit).HasMaxLength(20);
            entity.Property(x => x.Note).HasMaxLength(1000);

            entity.HasOne(x => x.SurveyReport)
                .WithMany(x => x.Measurements)
                .HasForeignKey(x => x.SurveyReportId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DutySurveyPhoto>(entity =>
        {
            entity.ToTable("duty_survey_photos");
            entity.HasKey(x => x.Id);

            entity.HasQueryFilter(x => !x.IsDeleted);
            entity.HasIndex(x => x.SurveyReportId);

            entity.Property(x => x.StoredFileName).HasMaxLength(260).IsRequired();
            entity.Property(x => x.OriginalName).HasMaxLength(260).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Caption).HasMaxLength(500);

            entity.HasOne(x => x.SurveyReport)
                .WithMany(x => x.Photos)
                .HasForeignKey(x => x.SurveyReportId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigurePersonnelRehireOverrides(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PersonnelRehireOverride>(entity =>
        {
            entity.ToTable("personnel_rehire_overrides");
            entity.HasKey(x => x.Id);

            // Denetim izi kişi bazında sorgulanır: "bu kişi için kaç
            // kez engel geçildi".
            entity.HasIndex(x => x.MatchedPersonnelId);
            entity.HasIndex(x => x.IdentityNumber);

            entity.Property(x => x.IdentityNumber)
                .HasMaxLength(11)
                .IsRequired();

            entity.Property(x => x.Reason)
                .HasMaxLength(1000)
                .IsRequired();
        });
    }

    private static void ConfigureCompanyCorporateTaxRates(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CompanyCorporateTaxRate>(entity =>
        {
            entity.ToTable("company_corporate_tax_rates");
            entity.HasKey(x => x.Id);

            // Bir şirketin bir yıl için tek oranı olur. Silinmiş satır
            // yeni kaydı engellemesin diye filtreli.
            entity.HasIndex(x => new { x.CompanyId, x.Year })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");

            entity.Property(x => x.Rate).HasPrecision(5, 2);
            entity.Property(x => x.Note).HasMaxLength(500);

            entity.HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
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
            entity.HasOne(x => x.PayrollExpenseAccount).WithMany()
                .HasForeignKey(x => x.PayrollExpenseAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PayrollPayableAccount).WithMany()
                .HasForeignKey(x => x.PayrollPayableAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.TaxPayableAccount).WithMany()
                .HasForeignKey(x => x.TaxPayableAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.SocialSecurityPayableAccount).WithMany()
                .HasForeignKey(x => x.SocialSecurityPayableAccountId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.EmployeeAdvanceAccount).WithMany()
                .HasForeignKey(x => x.EmployeeAdvanceAccountId).OnDelete(DeleteBehavior.Restrict);

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
            entity.Property(x => x.ExchangeRate).HasPrecision(18, 6);
            entity.Property(x => x.AmountTry).HasPrecision(18, 2);
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

    private static void ConfigureHizir(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HizirConversation>(entity =>
        {
            entity.ToTable("hizir_conversations");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.UserId, x.LastMessageAtUtc });

            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.StartedOnPath).HasMaxLength(300);

            entity.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<HizirPendingAction>(entity =>
        {
            entity.ToTable("hizir_pending_actions");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.UserId, x.Status });
            entity.HasIndex(x => x.ExpiresAtUtc);

            entity.Property(x => x.ActionName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ArgumentsJson).HasMaxLength(8000).IsRequired();
            entity.Property(x => x.Summary).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.RequiredPermission).HasMaxLength(100);
            entity.Property(x => x.ResultMessage).HasMaxLength(2000);
            entity.Property(x => x.Status).HasConversion<int>().IsRequired();

            entity.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Conversation).WithMany()
                .HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.SetNull);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<HizirMessage>(entity =>
        {
            entity.ToTable("hizir_messages");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.ConversationId, x.CreatedAtUtc });

            entity.Property(x => x.Role).HasConversion<int>().IsRequired();
            entity.Property(x => x.Content).HasMaxLength(20000).IsRequired();
            entity.Property(x => x.PagePath).HasMaxLength(300);
            entity.Property(x => x.UsedTools).HasMaxLength(500);
            entity.Property(x => x.DeniedTools).HasMaxLength(500);

            entity.HasOne(x => x.Conversation).WithMany(x => x.Messages)
                .HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigurePayrollSettings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PersonnelExtraPayment>(entity =>
        {
            entity.ToTable("personnel_extra_payments");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.MonthlyAmount).HasPrecision(18, 2);
            entity.Property(x => x.Note).HasMaxLength(500);

            entity.HasIndex(x => new { x.PersonnelId, x.EffectiveStartDate });

            entity.HasOne(x => x.Personnel).WithMany()
                .HasForeignKey(x => x.PersonnelId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Company).WithMany()
                .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<PersonnelCashPaymentEntry>(entity =>
        {
            entity.ToTable("personnel_cash_payments");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Kind).HasConversion<int>().IsRequired();
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Note).HasMaxLength(500);

            entity.HasIndex(x => new { x.PersonnelId, x.PaymentDate });
            entity.HasIndex(x => new { x.CompanyId, x.PeriodYear, x.PeriodMonth });

            entity.HasOne(x => x.Personnel).WithMany()
                .HasForeignKey(x => x.PersonnelId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Company).WithMany()
                .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<PersonnelTermination>(entity =>
        {
            entity.ToTable("personnel_terminations");
            entity.HasKey(x => x.Id);

            foreach (var property in new[]
            {
                nameof(PersonnelTermination.UnusedLeaveDays),
                nameof(PersonnelTermination.OfficialMonthlyGross),
                nameof(PersonnelTermination.ExtraMonthlyAmount),
                nameof(PersonnelTermination.OfficialSeveranceGross),
                nameof(PersonnelTermination.OfficialSeveranceStampTax),
                nameof(PersonnelTermination.OfficialNoticeGross),
                nameof(PersonnelTermination.OfficialNoticeIncomeTax),
                nameof(PersonnelTermination.OfficialNoticeStampTax),
                nameof(PersonnelTermination.OfficialLeaveGross),
                nameof(PersonnelTermination.OfficialLeaveSgk),
                nameof(PersonnelTermination.OfficialLeaveIncomeTax),
                nameof(PersonnelTermination.OfficialLeaveStampTax),
                nameof(PersonnelTermination.OfficialNetTotal),
                nameof(PersonnelTermination.ActualNetTotal),
                nameof(PersonnelTermination.ExtraPaymentDifference)
            })
            {
                entity.Property(property).HasPrecision(18, 2);
            }

            entity.Property(x => x.Note).HasMaxLength(1000);

            // Bir personelin kesinleşmiş tek bir çıkışı olur; taslaklar
            // serbest. Soft-delete nedeniyle indeks silinmemişlerle sınırlı.
            entity.HasIndex(x => x.PersonnelId)
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false AND \"Status\" = 1");

            entity.HasOne(x => x.Personnel).WithMany()
                .HasForeignKey(x => x.PersonnelId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Company).WithMany()
                .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<Models.HumanResources.CompanyHolidayCalendar>(entity =>
        {
            entity.ToTable("company_holiday_calendars");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.CompanyId, x.Year })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");

            entity.Property(x => x.VerificationNote).HasMaxLength(500);

            entity.HasOne(x => x.Company).WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<Models.HumanResources.CompanyHoliday>(entity =>
        {
            entity.ToTable("company_holidays");
            entity.HasKey(x => x.Id);

            // Aynı güne iki tatil kaydı, ücrete esas gün sayısını iki
            // kez düşürürdü.
            entity.HasIndex(x => new { x.CompanyHolidayCalendarId, x.Date })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");

            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();

            entity.HasOne(x => x.CompanyHolidayCalendar).WithMany(x => x.Days)
                .HasForeignKey(x => x.CompanyHolidayCalendarId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<Models.HumanResources.PersonnelDocument>(entity =>
        {
            // MEVCUT tabloya bağlanıyor: hr_personnel_documents canlıda
            // zaten vardı (modeli ve ucu olmayan, terk edilmiş bir
            // tasarımdan kalma, boş). İkinci bir personel belgesi
            // tablosu açmak iki ayrı kaynak yaratırdı.
            entity.ToTable("hr_personnel_documents");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.PersonnelId, x.DocumentType });
            entity.HasIndex(x => new { x.CompanyId, x.ExpiryDate });

            entity.Property(x => x.DocumentType).HasConversion<int>().IsRequired();
            entity.Property(x => x.DocumentName).HasMaxLength(300).IsRequired();
            entity.Property(x => x.DocumentNumber).HasMaxLength(150);
            entity.Property(x => x.IssuingInstitution).HasMaxLength(300);
            entity.Property(x => x.FilePath).HasMaxLength(1000);
            entity.Property(x => x.Notes).HasMaxLength(4000);
            entity.Property(x => x.OriginalName).HasMaxLength(300);
            entity.Property(x => x.ContentType).HasMaxLength(200);

            entity.HasOne(x => x.Personnel).WithMany()
                .HasForeignKey(x => x.PersonnelId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

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
            entity.Property(x => x.DailyWorkHours).HasPrecision(5, 2);

            // Kolon varsayılanı Pazartesi–Cumartesi. Sıfır "hiçbir gün
            // çalışılmıyor" demek olurdu ve süre hesabı yapılamazdı.
            entity.Property(x => x.WorkWeek)
                .HasDefaultValue((int)Services.Schedule.WorkWeekDays.MondayToSaturday);
            entity.Property(x => x.SeveranceCeiling).HasPrecision(18, 2);
            entity.Property(x => x.SeveranceCeilingPeriodNote).HasMaxLength(100);
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
            entity.Property(x => x.ExchangeRate).HasPrecision(18, 6);
            entity.Property(x => x.AmountTry).HasPrecision(18, 2);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.CostCenterCode).HasMaxLength(50);
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
            entity.HasOne(x => x.ReplacedByCheque).WithMany()
                .HasForeignKey(x => x.ReplacedByChequeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ReplacesCheque).WithMany()
                .HasForeignKey(x => x.ReplacesChequeId).OnDelete(DeleteBehavior.Restrict);

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

        modelBuilder.Entity<TaxPayment>(entity =>
        {
            entity.ToTable("tax_payments");
            entity.HasKey(x => x.Id);

            // Aynı dönem iki kez ödendi işaretlenemez; nakit akıştan
            // iki kez düşülürdü.
            entity.HasIndex(x => new
            {
                x.CompanyId, x.Kind, x.PeriodYear, x.PeriodNumber
            })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

            entity.Property(x => x.Kind).HasConversion<int>().IsRequired();
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Note).HasMaxLength(500);

            entity.HasOne(x => x.Company).WithMany()
                .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ChequeAllocation>(entity =>
        {
            entity.ToTable("cheque_allocations");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => x.ChequeId);
            entity.HasIndex(x => x.SupplierInvoiceId);
            entity.HasIndex(x => x.SalesInvoiceId);

            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.CostCenterCode).HasMaxLength(50);
            entity.Property(x => x.Description).HasMaxLength(500);

            entity.HasOne(x => x.Cheque).WithMany(x => x.Allocations)
                .HasForeignKey(x => x.ChequeId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Project).WithMany()
                .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.SupplierInvoice).WithMany()
                .HasForeignKey(x => x.SupplierInvoiceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.SalesInvoice).WithMany()
                .HasForeignKey(x => x.SalesInvoiceId).OnDelete(DeleteBehavior.Restrict);

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
        modelBuilder.Entity<SalesInvoice>(entity =>
        {
            entity.ToTable("sales_invoices");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.CompanyId, x.InternalNumber }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.Status });

            // Aynı müşteriye aynı resmi fatura numarası iki kez
            // girilemez; içe aktarmada mükerrer engeli bu indekse dayanır.
            entity.HasIndex(x => new { x.CustomerCurrentAccountId, x.OfficialInvoiceNumber })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false AND \"OfficialInvoiceNumber\" IS NOT NULL");

            entity.Property(x => x.InternalNumber).HasMaxLength(50).IsRequired();
            entity.Property(x => x.OfficialInvoiceNumber).HasMaxLength(100);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.ExchangeRate).HasPrecision(18, 6);
            entity.Property(x => x.Subtotal).HasPrecision(18, 2);
            entity.Property(x => x.VatTotal).HasPrecision(18, 2);
            entity.Property(x => x.GrandTotal).HasPrecision(18, 2);
            entity.Property(x => x.WithholdingAmount).HasPrecision(18, 2);
            entity.Property(x => x.NetReceivableAmount).HasPrecision(18, 2);
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.Property(x => x.CancellationReason).HasMaxLength(1000);
            entity.Property(x => x.SourceXmlPath).HasMaxLength(500);
            entity.Property(x => x.Status).HasConversion<int>().IsRequired();
            entity.Property(x => x.ParseSource).HasConversion<int?>();

            entity.HasOne(x => x.Company).WithMany()
                .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CustomerCurrentAccount).WithMany()
                .HasForeignKey(x => x.CustomerCurrentAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Project).WithMany()
                .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.AccountingVoucher).WithMany()
                .HasForeignKey(x => x.AccountingVoucherId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ReversalVoucher).WithMany()
                .HasForeignKey(x => x.ReversalVoucherId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.OriginalInvoice).WithMany()
                .HasForeignKey(x => x.OriginalInvoiceId).OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(x => x.Items)
                .WithOne(x => x.SalesInvoice)
                .HasForeignKey(x => x.SalesInvoiceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<SalesInvoiceItem>(entity =>
        {
            entity.ToTable("sales_invoice_items");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.SalesInvoiceId, x.LineNumber }).IsUnique();

            entity.Property(x => x.Description).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Unit).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Quantity).HasPrecision(18, 4);
            entity.Property(x => x.UnitPrice).HasPrecision(18, 6);
            entity.Property(x => x.VatRate).HasPrecision(8, 4);
            entity.Property(x => x.LineSubtotal).HasPrecision(18, 2);
            entity.Property(x => x.VatAmount).HasPrecision(18, 2);
            entity.Property(x => x.LineTotal).HasPrecision(18, 2);

            entity.HasOne(x => x.OriginalItem).WithMany()
                .HasForeignKey(x => x.OriginalItemId).OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<SupplierInvoice>(entity =>
        {
            entity.ToTable("supplier_invoices");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.CompanyId, x.InternalNumber }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.Status });
            entity.HasIndex(x => x.ProjectId);

            entity.Property(x => x.InternalNumber).HasMaxLength(50).IsRequired();
            entity.Property(x => x.InvoiceNumber).HasMaxLength(100).IsRequired();
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.ExchangeRate).HasPrecision(18, 6);
            entity.Property(x => x.Subtotal).HasPrecision(18, 2);
            entity.Property(x => x.VatTotal).HasPrecision(18, 2);
            entity.Property(x => x.GrandTotal).HasPrecision(18, 2);
            entity.Property(x => x.WithholdingAmount).HasPrecision(18, 2);
            entity.Property(x => x.MatchDifferenceAmount).HasPrecision(18, 2);
            entity.Property(x => x.SourceXmlPath).HasMaxLength(500);
            entity.Property(x => x.ParseSource).HasConversion<int?>();

            // Aynı tedarikçiden aynı fatura numarası iki kez girilemez.
            // Soft-delete nedeniyle indeks silinmemişlerle sınırlı.
            entity.HasIndex(x => new { x.SupplierCurrentAccountId, x.InvoiceNumber })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");
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
            entity.HasOne(x => x.ReversalVoucher).WithMany()
                .HasForeignKey(x => x.ReversalVoucherId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.OriginalInvoice).WithMany()
                .HasForeignKey(x => x.OriginalInvoiceId).OnDelete(DeleteBehavior.Restrict);

            entity.Property(x => x.CancellationReason).HasMaxLength(1000);

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
            entity.HasOne(x => x.OriginalItem).WithMany()
                .HasForeignKey(x => x.OriginalItemId).OnDelete(DeleteBehavior.Restrict);

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
            entity.Property(x => x.PaymentTerms).HasMaxLength(2000);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.VatRate).HasPrecision(5, 2);
            entity.Property(x => x.WithholdingRate).HasMaxLength(20);
            entity.Property(x => x.ContractType).HasConversion<int>();
            entity.Property(x => x.DeviationAlertThresholdRate).HasPrecision(8, 4);
            entity.Property(x => x.DelayPenaltyKind).HasConversion<int>();
            entity.Property(x => x.DelayPenaltyValue).HasPrecision(18, 4);
            entity.Property(x => x.DelayPenaltyCapRate).HasPrecision(8, 4);
            entity.Property(x => x.City).HasMaxLength(100);
            entity.Property(x => x.District).HasMaxLength(100);
            entity.Property(x => x.ArchiveReason).HasMaxLength(500);
            entity.HasIndex(x => x.IsArchived);

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

    private static void ConfigureSubcontractorContracts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SubcontractorContract>(entity =>
        {
            entity.ToTable("subcontractor_contracts");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.CompanyId, x.ContractNumber }).IsUnique();
            entity.HasIndex(x => new { x.ProjectId, x.Status });
            entity.HasIndex(x => x.CurrentAccountId);

            entity.Property(x => x.ContractNumber).HasMaxLength(50).IsRequired();
            entity.Property(x => x.WorkDescription).HasMaxLength(500).IsRequired();
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(1000);

            entity.Property(x => x.ContractAmount).HasPrecision(18, 2);
            entity.Property(x => x.RetentionRate).HasPrecision(9, 4);

            entity.HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.CurrentAccount)
                .WithMany()
                .HasForeignKey(x => x.CurrentAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Project)
                .WithMany()
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.ProjectSite)
                .WithMany()
                .HasForeignKey(x => x.ProjectSiteId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<SubcontractorContractSection>(entity =>
        {
            entity.ToTable("subcontractor_contract_sections");
            entity.HasKey(x => x.Id);

            // Aynı kısım aynı sözleşmede iki kez olamaz; olsaydı götürü
            // ilerleme ağırlığı çift sayılırdı.
            entity.HasIndex(x => new
            {
                x.SubcontractorContractId,
                x.ProjectHakedisSectionId
            }).IsUnique();

            entity.Property(x => x.SectionAmount).HasPrecision(18, 2);

            entity.HasOne(x => x.SubcontractorContract)
                .WithMany(x => x.Sections)
                .HasForeignKey(x => x.SubcontractorContractId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.ProjectHakedisSection)
                .WithMany()
                .HasForeignKey(x => x.ProjectHakedisSectionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureSubcontractorProgressPayments(
        ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SubcontractorProgressPayment>(entity =>
        {
            entity.ToTable("subcontractor_progress_payments");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.CompanyId, x.ProgressPaymentNumber })
                .IsUnique();

            // Aynı sözleşmede aynı dönem numarası iki kez olamaz;
            // olsaydı kümülatif zincir çatallanırdı.
            entity.HasIndex(x => new
            {
                x.SubcontractorContractId,
                x.PeriodNumber
            }).IsUnique();

            entity.Property(x => x.ProgressPaymentNumber)
                .HasMaxLength(50).IsRequired();
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(1000);

            foreach (var property in new[]
            {
                nameof(SubcontractorProgressPayment.ContractAmount),
                nameof(SubcontractorProgressPayment.PreviousAmount),
                nameof(SubcontractorProgressPayment.CurrentAmount),
                nameof(SubcontractorProgressPayment.CumulativeAmount),
                nameof(SubcontractorProgressPayment.TotalDeductionAmount),
                nameof(SubcontractorProgressPayment.GrossPayableAmount),
                nameof(SubcontractorProgressPayment.NetPayableAmount)
            })
            {
                entity.Property(property).HasPrecision(18, 2);
            }

            entity.HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.SubcontractorContract)
                .WithMany()
                .HasForeignKey(x => x.SubcontractorContractId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<SubcontractorProgressPaymentItem>(entity =>
        {
            entity.ToTable("subcontractor_progress_payment_items");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new
            {
                x.SubcontractorProgressPaymentId,
                x.LineNumber
            });

            entity.Property(x => x.PositionCode).HasMaxLength(60).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Unit).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(500);

            foreach (var property in new[]
            {
                nameof(SubcontractorProgressPaymentItem.ContractQuantity),
                nameof(SubcontractorProgressPaymentItem.PreviousQuantity),
                nameof(SubcontractorProgressPaymentItem.SuggestedQuantity),
                nameof(SubcontractorProgressPaymentItem.AgreedQuantity),
                nameof(SubcontractorProgressPaymentItem.CurrentQuantity)
            })
            {
                entity.Property(property).HasPrecision(18, 4);
            }

            foreach (var property in new[]
            {
                nameof(SubcontractorProgressPaymentItem.UnitPrice),
                nameof(SubcontractorProgressPaymentItem.PreviousAmount),
                nameof(SubcontractorProgressPaymentItem.CurrentAmount),
                nameof(SubcontractorProgressPaymentItem.CumulativeAmount)
            })
            {
                entity.Property(property).HasPrecision(18, 2);
            }

            entity.HasOne(x => x.SubcontractorProgressPayment)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.SubcontractorProgressPaymentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.ProjectHakedisSection)
                .WithMany()
                .HasForeignKey(x => x.ProjectHakedisSectionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.ProjectBoqItem)
                .WithMany()
                .HasForeignKey(x => x.ProjectBoqItemId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<SubcontractorProgressPaymentSection>(entity =>
        {
            entity.ToTable("subcontractor_progress_payment_sections");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new
            {
                x.SubcontractorProgressPaymentId,
                x.ProjectHakedisSectionId
            }).IsUnique();

            entity.Property(x => x.Notes).HasMaxLength(500);

            foreach (var property in new[]
            {
                nameof(SubcontractorProgressPaymentSection.PreviousProgressRate),
                nameof(SubcontractorProgressPaymentSection.SuggestedProgressRate),
                nameof(SubcontractorProgressPaymentSection.AgreedProgressRate)
            })
            {
                entity.Property(property).HasPrecision(9, 4);
            }

            foreach (var property in new[]
            {
                nameof(SubcontractorProgressPaymentSection.SectionAmount),
                nameof(SubcontractorProgressPaymentSection.PreviousAmount),
                nameof(SubcontractorProgressPaymentSection.CurrentAmount),
                nameof(SubcontractorProgressPaymentSection.CumulativeAmount)
            })
            {
                entity.Property(property).HasPrecision(18, 2);
            }

            entity.HasOne(x => x.SubcontractorProgressPayment)
                .WithMany(x => x.Sections)
                .HasForeignKey(x => x.SubcontractorProgressPaymentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.ProjectHakedisSection)
                .WithMany()
                .HasForeignKey(x => x.ProjectHakedisSectionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<SubcontractorProgressPaymentDeduction>(entity =>
        {
            entity.ToTable("subcontractor_progress_payment_deductions");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new
            {
                x.SubcontractorProgressPaymentId,
                x.LineNumber
            });

            entity.Property(x => x.Description).HasMaxLength(300).IsRequired();
            entity.Property(x => x.SuggestionBasis).HasMaxLength(500);
            entity.Property(x => x.Rate).HasPrecision(9, 4);

            foreach (var property in new[]
            {
                nameof(SubcontractorProgressPaymentDeduction.CumulativeBaseAmount),
                nameof(SubcontractorProgressPaymentDeduction.PreviousAmount),
                nameof(SubcontractorProgressPaymentDeduction.CumulativeAmount),
                nameof(SubcontractorProgressPaymentDeduction.Amount)
            })
            {
                entity.Property(property).HasPrecision(18, 2);
            }

            entity.HasOne(x => x.SubcontractorProgressPayment)
                .WithMany(x => x.Deductions)
                .HasForeignKey(x => x.SubcontractorProgressPaymentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureSubcontractorLedger(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SubcontractorLedgerEntry>(entity =>
        {
            entity.ToTable("subcontractor_ledger_entries");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.SubcontractorContractId, x.Kind });
            entity.HasIndex(x => x.SubcontractorProgressPaymentId);

            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.VatRate).HasPrecision(9, 4);

            foreach (var property in new[]
            {
                nameof(SubcontractorLedgerEntry.Amount),
                nameof(SubcontractorLedgerEntry.VatAmount),
                nameof(SubcontractorLedgerEntry.WithholdingAmount),
                nameof(SubcontractorLedgerEntry.PayableAmount)
            })
            {
                entity.Property(property).HasPrecision(18, 2);
            }

            entity.HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.SubcontractorContract)
                .WithMany()
                .HasForeignKey(x => x.SubcontractorContractId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.SubcontractorProgressPayment)
                .WithMany()
                .HasForeignKey(x => x.SubcontractorProgressPaymentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<SubcontractorCashLedgerEntry>(entity =>
        {
            entity.ToTable("subcontractor_cash_ledger_entries");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.SubcontractorContractId, x.Kind });

            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.Amount).HasPrecision(18, 2);

            entity.HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.SubcontractorContract)
                .WithMany()
                .HasForeignKey(x => x.SubcontractorContractId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.SubcontractorProgressPayment)
                .WithMany()
                .HasForeignKey(x => x.SubcontractorProgressPaymentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureSubcontractorDocuments(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SubcontractorDocument>(entity =>
        {
            entity.ToTable("subcontractor_documents");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new
            {
                x.SubcontractorContractId,
                x.DocumentType
            });

            // "Süresi dolan evraklar" sorgusu panelde ve uyarıda çalışır.
            entity.HasIndex(x => x.ValidUntil);

            entity.Property(x => x.Title).HasMaxLength(300).IsRequired();
            entity.Property(x => x.StoredFileName).HasMaxLength(300).IsRequired();
            entity.Property(x => x.OriginalFileName).HasMaxLength(300).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(500);

            entity.HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.SubcontractorContract)
                .WithMany()
                .HasForeignKey(x => x.SubcontractorContractId)
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

            // "Atama bekleyen personel" sorgusu panelde ve uyarıda çalışır.
            entity.HasIndex(x => new { x.CompanyId, x.WorkLocationType });

            entity.Property(x => x.WorkLocationType).HasConversion<int>();

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

            // Taşeron ekibi: hakediş kesintisi bu bağ üzerinden
            // hesaplandığı için sözleşme bazlı sorgu indeksli.
            entity.HasIndex(x => x.SubcontractorContractId);

            entity.HasOne(x => x.SubcontractorContract)
                .WithMany()
                .HasForeignKey(x => x.SubcontractorContractId)
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

            // Silmeler YUMUŞAK (AuditSaveChangesInterceptor satırı
            // fiziksel silmez, IsDeleted=true yapar). Filtresiz bir
            // tekil indeks, silinmiş satırın numarasını da rezerve
            // ettiği için talep düzenlenip kalemleri yeniden
            // numaralandığında çakışıyordu. Kod tabanının başka
            // yerlerinde kullanılan desenin aynısı.
            entity.HasIndex(x => new
            {
                x.PurchaseRequestId,
                x.LineNumber
            }).IsUnique().HasFilter("\"IsDeleted\" = false");

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

            // Poz silinse bile talep tarihçesi durmalı: Restrict, poza
            // bağlı bir talep varken pozun silinmesini engelliyor.
            entity.HasOne(x => x.EngineeringPosition)
                .WithMany()
                .HasForeignKey(x => x.EngineeringPositionId)
                .OnDelete(DeleteBehavior.Restrict);

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
            entity.Property(x => x.CopperKgPerUnit).HasPrecision(18, 4);
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
            entity.HasOne(x => x.ProjectHakedisSection).WithMany().HasForeignKey(x => x.ProjectHakedisSectionId).OnDelete(DeleteBehavior.SetNull);
            // Sözleşme silinse bile stok hareketi ayakta kalmalı: hareket
            // gerçekleşmiş bir depo olayı, sözleşme yalnızca etiketi.
            entity.HasOne(x => x.SubcontractorContract).WithMany()
                .HasForeignKey(x => x.SubcontractorContractId)
                .OnDelete(DeleteBehavior.SetNull);
            // Malzeme kesintisi bu yolu tarıyor.
            entity.HasIndex(x => new { x.SubcontractorContractId, x.MovementDate });
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

            entity.Property(x => x.LostReasonNote).HasMaxLength(1000);
            entity.Property(x => x.StatusNote).HasMaxLength(1000);

            // Huni ekranı durum ve karşı tarafa göre filtreliyor.
            entity.HasIndex(x => new { x.CompanyId, x.Status });

            entity.HasOne(x => x.CounterpartyCurrentAccount)
                .WithMany()
                .HasForeignKey(x => x.CounterpartyCurrentAccountId)
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
            entity.Property(x => x.MaterialUnitPrice).HasPrecision(18, 6);
            entity.Property(x => x.LaborUnitPrice).HasPrecision(18, 6);
            entity.Property(x => x.OverheadUnitPrice).HasPrecision(18, 6);
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

        modelBuilder.Entity<PositionUnitPrice>(entity =>
        {
            entity.ToTable("position_unit_prices");
            entity.HasKey(x => x.Id);

            // Aynı poz + yıl + kurum için tek satır: aynı fiyat kitabının
            // iki kez yüklenmesi satır çoğaltmamalı, fiyatı güncellemeli.
            entity.HasIndex(x => new
            {
                x.EngineeringPositionId, x.Year, x.Institution, x.Component
            })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

            entity.Property(x => x.Institution).HasConversion<int>().IsRequired();
            entity.Property(x => x.Component).HasConversion<int>().IsRequired();
            entity.Property(x => x.UnitPrice).HasPrecision(18, 4);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.SourceNote).HasMaxLength(300);

            entity.HasOne(x => x.EngineeringPosition)
                .WithMany()
                .HasForeignKey(x => x.EngineeringPositionId)
                .OnDelete(DeleteBehavior.Cascade);

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

            entity.Property(x => x.CumulativeWorkAmount)
                .HasPrecision(18, 2);

            entity.Property(x => x.CumulativeAdvanceMaterialAmount)
                .HasPrecision(18, 2);

            entity.Property(x => x.IncomeTaxWithholdingRate)
                .HasPrecision(8, 4);

            entity.Property(x => x.IncomeTaxWithholdingAmount)
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

        modelBuilder.Entity<ProjectExtraWork>(entity =>
        {
            entity.ToTable("project_extra_works");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.PositionCode).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Unit).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(2000);

            entity.Property(x => x.Quantity).HasPrecision(18, 4);
            entity.Property(x => x.UnitPrice).HasPrecision(18, 6);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.ApprovalStatus).HasConversion<int>();

            entity.HasIndex(x => new { x.ProjectId, x.ApprovalStatus });

            entity.HasOne(x => x.Project).WithMany()
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            // Bölüm silinse de ilave iş kaydı kalır, bağı kopar.
            entity.HasOne(x => x.ProjectHakedisSection).WithMany()
                .HasForeignKey(x => x.ProjectHakedisSectionId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.ApprovalDocument).WithMany()
                .HasForeignKey(x => x.ApprovalDocumentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.ProgressPayment).WithMany()
                .HasForeignKey(x => x.ProgressPaymentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<HakedisDeductionAccountMapping>(entity =>
        {
            entity.ToTable("hakedis_deduction_account_mappings");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Notes).HasMaxLength(500);

            // Şirket başına her kesinti türünden tek eşleme. Soft-delete
            // nedeniyle indeks yalnızca silinmemiş satırlarda arar.
            entity.HasIndex(x => new { x.CompanyId, x.DeductionType })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");

            entity.HasOne(x => x.Company).WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.AccountingAccount).WithMany()
                .HasForeignKey(x => x.AccountingAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ProgressPaymentPaymentPlan>(entity =>
        {
            entity.ToTable("progress_payment_payment_plans");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.PaymentType).HasConversion<int>();
            entity.Property(x => x.Rate).HasPrecision(8, 4);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Description).HasMaxLength(500);

            entity.HasIndex(x => new { x.ProgressPaymentId, x.LineNumber }).IsUnique();

            entity.HasOne(x => x.ProgressPayment).WithMany(x => x.PaymentPlans)
                .HasForeignKey(x => x.ProgressPaymentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Çek silinirse plan satırı kalsın, bağı kopsun.
            entity.HasOne(x => x.Cheque).WithMany()
                .HasForeignKey(x => x.ChequeId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ProgressPaymentDeductionLine>(entity =>
        {
            entity.ToTable("progress_payment_deduction_lines");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(1000);

            entity.Property(x => x.UnitPrice).HasPrecision(18, 4);
            entity.Property(x => x.Quantity).HasPrecision(18, 4);
            entity.Property(x => x.VatRate).HasPrecision(8, 4);
            entity.Property(x => x.NetAmount).HasPrecision(18, 2);
            entity.Property(x => x.VatAmount).HasPrecision(18, 2);
            entity.Property(x => x.GrossAmount).HasPrecision(18, 2);

            entity.HasIndex(x => new
            {
                x.ProgressPaymentDeductionId,
                x.LineNumber
            }).IsUnique();

            entity.HasOne(x => x.ProgressPaymentDeduction).WithMany(x => x.Lines)
                .HasForeignKey(x => x.ProgressPaymentDeductionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<BarterLedgerEntry>(entity =>
        {
            entity.ToTable("barter_ledger_entries");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Description).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.EntryType).HasConversion<int>();

            entity.HasIndex(x => new { x.ProjectId, x.EntryDate });

            entity.HasOne(x => x.Project).WithMany()
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.ProjectSite).WithMany()
                .HasForeignKey(x => x.ProjectSiteId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.ProgressPayment).WithMany()
                .HasForeignKey(x => x.ProgressPaymentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ProgressPaymentAdvanceMaterial>(entity =>
        {
            entity.ToTable("progress_payment_advance_materials");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.PositionCode).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Unit).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(2000);

            entity.Property(x => x.Quantity).HasPrecision(18, 4);
            entity.Property(x => x.UnitPrice).HasPrecision(18, 4);
            entity.Property(x => x.ValuationRate).HasPrecision(8, 4);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.OffsetAmount).HasPrecision(18, 2);

            // OpenAmount hesaplanmış bir özellik; kolona yazılmaz.
            entity.Ignore(x => x.OpenAmount);

            entity.HasIndex(x => new { x.ProgressPaymentId, x.LineNumber }).IsUnique();

            entity.HasOne(x => x.ProgressPayment).WithMany(x => x.AdvanceMaterials)
                .HasForeignKey(x => x.ProgressPaymentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ProgressPaymentAdvanceMaterialOffset>(entity =>
        {
            entity.ToTable("progress_payment_advance_material_offsets");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Notes).HasMaxLength(1000);

            entity.HasIndex(x => new { x.ProgressPaymentId, x.AdvanceMaterialId });

            entity.HasOne(x => x.AdvanceMaterial).WithMany(x => x.Offsets)
                .HasForeignKey(x => x.AdvanceMaterialId)
                .OnDelete(DeleteBehavior.Cascade);

            // Mahsubun yapıldığı hakediş silinirse mahsup de silinmeli;
            // iki yoldan cascade çakışmasın diye bu taraf Restrict.
            entity.HasOne(x => x.ProgressPayment).WithMany(x => x.AdvanceMaterialOffsets)
                .HasForeignKey(x => x.ProgressPaymentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ProjectHakedisSection>(entity =>
        {
            entity.ToTable("project_hakedis_sections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Code).HasMaxLength(50);
            entity.Property(x => x.ContractType).HasConversion<int>();

            entity.HasIndex(x => new { x.ProjectId, x.Order });

            entity.HasOne(x => x.Project).WithMany()
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ProgressPaymentSection>(entity =>
        {
            entity.ToTable("progress_payment_sections");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Code).HasMaxLength(50);

            foreach (var property in new[]
            {
                nameof(ProgressPaymentSection.MaterialAmount),
                nameof(ProgressPaymentSection.LaborAmount),
                nameof(ProgressPaymentSection.OverheadAmount),
                nameof(ProgressPaymentSection.CurrentAmount),
                nameof(ProgressPaymentSection.PreviousAmount),
                nameof(ProgressPaymentSection.CumulativeAmount)
            })
            {
                entity.Property(property).HasPrecision(18, 2);
            }

            entity.HasIndex(x => new { x.ProgressPaymentId, x.Order });

            entity.HasOne(x => x.ProgressPayment).WithMany(x => x.Sections)
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

            entity.Property(x => x.MaterialUnitPrice)
                .HasPrecision(18, 4);

            entity.Property(x => x.LaborUnitPrice)
                .HasPrecision(18, 4);

            entity.Property(x => x.OverheadUnitPrice)
                .HasPrecision(18, 4);

            entity.Property(x => x.MaterialAmount)
                .HasPrecision(18, 2);

            entity.Property(x => x.LaborAmount)
                .HasPrecision(18, 2);

            entity.Property(x => x.OverheadAmount)
                .HasPrecision(18, 2);

            entity.Property(x => x.PreviousAmount)
                .HasPrecision(18, 2);

            entity.Property(x => x.CurrentAmount)
                .HasPrecision(18, 2);

            entity.Property(x => x.CumulativeAmount)
                .HasPrecision(18, 2);

            entity.Property(x => x.CompletionRate)
                .HasPrecision(10, 4);

            entity.HasOne(x => x.Section).WithMany()
                .HasForeignKey(x => x.ProgressPaymentSectionId)
                .OnDelete(DeleteBehavior.SetNull);

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

            entity.Property(x => x.CumulativeBaseAmount)
                .HasPrecision(18, 2);

            entity.Property(x => x.PreviousAmount)
                .HasPrecision(18, 2);

            entity.Property(x => x.CumulativeAmount)
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

            // Proje başına tek sözleşme metrajı. Soft-delete nedeniyle
            // filtre silinmemişlerle sınırlı; silinen bir baseline
            // yenisinin kurulmasını engellememeli.
            entity.HasIndex(x => x.ProjectId)
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false AND \"IsContractBaseline\" = true");

            entity.Property(x => x.BoqNumber).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Status).HasConversion<int>();
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.TotalAmount).HasPrecision(18, 2);
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.Notes).HasMaxLength(4000);

            entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);

            // Teklif silinse bile icmal ayakta kalmalı: icmal hakedişin
            // referansı, teklif ise yalnızca nereden geldiğinin kaydı.
            entity.HasOne(x => x.SourceOffer).WithMany()
                .HasForeignKey(x => x.SourceOfferId).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => x.SourceOfferId);

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
            entity.Property(x => x.CopperKgPerUnit).HasPrecision(18, 4);
            entity.Property(x => x.UnitPrice).HasPrecision(18, 6);
            entity.Property(x => x.TotalAmount).HasPrecision(18, 2);
            entity.Property(x => x.ItemType).HasConversion<int>();
            entity.Property(x => x.Category).HasMaxLength(200);
            entity.Property(x => x.Notes).HasMaxLength(2000);

            entity.HasOne(x => x.ProjectBoq).WithMany(x => x.Items)
                .HasForeignKey(x => x.ProjectBoqId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<EngineeringPosition>().WithMany()
                .HasForeignKey(x => x.EngineeringPositionId).OnDelete(DeleteBehavior.Restrict);

            // Bölüm silinse de keşif kalemi kalır, bağı kopar.
            entity.HasOne(x => x.ProjectHakedisSection).WithMany()
                .HasForeignKey(x => x.ProjectHakedisSectionId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.InventoryItem).WithMany()
                .HasForeignKey(x => x.InventoryItemId)
                .OnDelete(DeleteBehavior.SetNull);

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
            entity.Property(x => x.CostClass).HasConversion<int>();
            entity.HasIndex(x => new { x.ProjectId, x.CostClass });
            entity.HasIndex(x => x.ProjectHakedisSectionId);
            entity.HasIndex(x => x.ProjectBoqItemId);
            entity.Property(x => x.Description).IsRequired();
            entity.Property(x => x.ReferenceType);

            entity.HasOne(x => x.ProjectBoqItem)
                .WithMany()
                .HasForeignKey(x => x.ProjectBoqItemId)
                // İcmal revize edilip satır silinse bile maliyet kaydı
                // durmalı; yalnızca poz bağı kopar ve maliyet kısım
                // düzeyine döner.
                .OnDelete(DeleteBehavior.SetNull);

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

            entity.HasOne(x => x.ProjectHakedisSection)
                .WithMany()
                .HasForeignKey(x => x.ProjectHakedisSectionId)
                .OnDelete(DeleteBehavior.SetNull);

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
            entity.HasIndex(x => x.ProjectHakedisSectionId);
            entity.HasIndex(x => x.ProjectBoqItemId);

            entity.HasOne(x => x.ProjectBoqItem)
                .WithMany()
                .HasForeignKey(x => x.ProjectBoqItemId)
                // İcmal revize edilip satır silinse bile maliyet kaydı
                // durmalı; yalnızca poz bağı kopar ve maliyet kısım
                // düzeyine döner.
                .OnDelete(DeleteBehavior.SetNull);

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
            entity.Property(x => x.ProgressPaymentCost).HasPrecision(18, 2);
            entity.Property(x => x.ProgressPaymentCompensationCost).HasPrecision(18, 2);
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
            // Silme soft-delete'e çevrildiği için benzersizlik yalnızca
            // silinmemiş satırlarda aranmalı: aksi halde silinen bir
            // puantaj kaydı, aynı personel/gün için yeniden giriş
            // yapılmasını kalıcı olarak engelliyordu (kayıt eklenirken
            // veritabanı kısıt hatası).
            entity.HasIndex(x => new { x.CompanyId, x.PersonnelId, x.WorkDate })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");
            entity.HasIndex(x => new { x.PersonnelId, x.Status });
            entity.HasIndex(x => new { x.ProjectId, x.WorkDate });
            entity.HasIndex(x => new { x.ProjectSiteId, x.WorkDate });

            entity.HasOne(x => x.ProjectSite).WithMany()
                .HasForeignKey(x => x.ProjectSiteId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ProjectHakedisSection).WithMany()
                .HasForeignKey(x => x.ProjectHakedisSectionId).OnDelete(DeleteBehavior.SetNull);
            entity.Property(x => x.NormalHours).HasPrecision(8, 2);
            entity.Property(x => x.OvertimeHours).HasPrecision(8, 2);
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

            // Alet kartı sonradan geldi: mevcut serbest metinli
            // zimmetler bozulmasın diye bağ OPSİYONEL.
            entity.HasOne(x => x.ToolAsset).WithMany()
                .HasForeignKey(x => x.ToolAssetId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.ToolAssetId, x.Status });

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ToolAsset>(entity =>
        {
            entity.ToTable("tool_assets");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.CompanyId, x.Code })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");

            // Seri numarası girilmişse şirket içinde benzersiz: aynı
            // seriyi iki karta yazmak, servis geçmişini ikiye böler.
            entity.HasIndex(x => new { x.CompanyId, x.SerialNumber })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false AND \"SerialNumber\" IS NOT NULL");

            entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Brand).HasMaxLength(150);
            entity.Property(x => x.Model).HasMaxLength(150);
            entity.Property(x => x.SerialNumber).HasMaxLength(200);
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.Property(x => x.PurchaseCost).HasPrecision(18, 2);
            entity.Property(x => x.Status).HasConversion<int>();
            entity.Property(x => x.LocationType).HasConversion<int>();

            entity.HasOne(x => x.Company).WithMany()
                .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ProjectSite).WithMany()
                .HasForeignKey(x => x.ProjectSiteId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.AssignedPersonnel).WithMany()
                .HasForeignKey(x => x.AssignedPersonnelId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ToolServiceRequest>(entity =>
        {
            entity.ToTable("tool_service_requests");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.CompanyId, x.RequestNumber })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");
            entity.HasIndex(x => new { x.ToolAssetId, x.Status });
            entity.HasIndex(x => new { x.ProjectId, x.RequestDate });

            entity.Property(x => x.RequestNumber).HasMaxLength(50).IsRequired();
            entity.Property(x => x.FaultDescription).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.DecisionNote).HasMaxLength(2000);
            entity.Property(x => x.ServiceProviderName).HasMaxLength(300);
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.Property(x => x.ServiceCost).HasPrecision(18, 2);
            entity.Property(x => x.Status).HasConversion<int>();
            entity.Property(x => x.Decision).HasConversion<int>();
            entity.Property(x => x.Urgency).HasConversion<int>();

            entity.HasOne(x => x.Company).WithMany()
                .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ToolAsset).WithMany()
                .HasForeignKey(x => x.ToolAssetId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Project).WithMany()
                .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.ProjectSite).WithMany()
                .HasForeignKey(x => x.ProjectSiteId).OnDelete(DeleteBehavior.SetNull);

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
            entity.Property(x => x.EducationLevel).HasMaxLength(150);
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

        ConfigureSchedule(modelBuilder);
    }

    /// <summary>
    /// İş programı (Gantt) tabloları.
    ///
    /// Aktivite icmal kısmına ve icmal satırına SetNull ile bağlı:
    /// kısım silinse bile çubuk kaybolmaz, yalnızca bağsız kalır ve
    /// ekranda öyle görünür. Cascade seçilseydi bir kısmın silinmesi
    /// iş programının bir bölümünü sessizce yok ederdi.
    /// </summary>
    private static void ConfigureSchedule(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Models.Schedule.ProjectSchedule>(entity =>
        {
            entity.ToTable("project_schedules");
            entity.HasKey(x => x.Id);

            // Proje başına yalnızca bir yürürlükteki program. Arşivlenmiş
            // programlar sınırın dışında: geçmiş plan saklanabilmeli.
            entity.HasIndex(x => x.ProjectId)
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false AND \"Status\" <> 2");

            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.Property(x => x.Status).HasConversion<int>().IsRequired();
            entity.Property(x => x.WorkWeek).HasConversion<int>().IsRequired();

            entity.HasOne(x => x.Project)
                .WithMany()
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<Models.Schedule.ScheduleActivity>(entity =>
        {
            entity.ToTable("schedule_activities");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => x.ProjectScheduleId);
            entity.HasIndex(x => x.ParentActivityId);

            entity.Property(x => x.Name).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.Property(x => x.ManualProgressRate).HasPrecision(5, 2);

            entity.HasOne(x => x.ProjectSchedule)
                .WithMany(x => x.Activities)
                .HasForeignKey(x => x.ProjectScheduleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.ParentActivity)
                .WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentActivityId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.ProjectHakedisSection)
                .WithMany()
                .HasForeignKey(x => x.ProjectHakedisSectionId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.ProjectBoqItem)
                .WithMany()
                .HasForeignKey(x => x.ProjectBoqItemId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<Models.Schedule.ScheduleDependency>(entity =>
        {
            entity.ToTable("schedule_dependencies");
            entity.HasKey(x => x.Id);

            // Aynı iki aktivite arasında ikinci bir bağ olamaz. Yumuşak
            // silinen satır sırayı işgal etmesin diye filtreli.
            entity.HasIndex(x => new
                {
                    x.ProjectScheduleId,
                    x.PredecessorActivityId,
                    x.SuccessorActivityId
                })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");

            entity.Property(x => x.Type).HasConversion<int>().IsRequired();

            entity.HasOne(x => x.ProjectSchedule)
                .WithMany(x => x.Dependencies)
                .HasForeignKey(x => x.ProjectScheduleId)
                .OnDelete(DeleteBehavior.Cascade);

            // İki ucu da aynı tabloya bakıyor; cascade çoklu yol
            // üretirdi. Aktivite silinirken bağları kod açıkça siliyor.
            entity.HasOne(x => x.PredecessorActivity)
                .WithMany()
                .HasForeignKey(x => x.PredecessorActivityId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.SuccessorActivity)
                .WithMany()
                .HasForeignKey(x => x.SuccessorActivityId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<Models.Schedule.ScheduleBaselineRevision>(entity =>
        {
            entity.ToTable("schedule_baseline_revisions");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.ProjectScheduleId, x.RevisionNumber })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");

            entity.Property(x => x.Reason).HasMaxLength(1000);

            entity.HasOne(x => x.ProjectSchedule)
                .WithMany()
                .HasForeignKey(x => x.ProjectScheduleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<Models.Schedule.ScheduleHoliday>(entity =>
        {
            entity.ToTable("schedule_holidays");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.ProjectScheduleId, x.Date })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");

            entity.Property(x => x.Name).HasMaxLength(200);

            entity.HasOne(x => x.ProjectSchedule)
                .WithMany(x => x.Holidays)
                .HasForeignKey(x => x.ProjectScheduleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<Models.Schedule.ScheduleResourceAssignment>(entity =>
        {
            entity.ToTable("schedule_resource_assignments");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => x.ScheduleActivityId);
            entity.HasIndex(x => x.PersonnelId);
            entity.HasIndex(x => x.SubcontractorContractId);

            entity.Property(x => x.Kind).HasConversion<int>().IsRequired();
            entity.Property(x => x.Role).HasMaxLength(200);
            entity.Property(x => x.Notes).HasMaxLength(1000);

            entity.HasOne(x => x.ScheduleActivity)
                .WithMany(x => x.Resources)
                .HasForeignKey(x => x.ScheduleActivityId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Personnel)
                .WithMany()
                .HasForeignKey(x => x.PersonnelId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.SubcontractorContract)
                .WithMany()
                .HasForeignKey(x => x.SubcontractorContractId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }
}
