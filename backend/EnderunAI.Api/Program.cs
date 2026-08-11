using EnderunAI.Api.Data.Interceptors;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.Costing;
using EnderunAI.Api.Services.DocumentNumbers;
using EnderunAI.Api.Services.AI;
using EnderunAI.Api.Services.Accounting;
using System.Text;
using EnderunAI.Api.Data;
using EnderunAI.Api.Data.HumanResources;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Upload;
using EnderunAI.Api.Services.Secretariat;
using EnderunAI.Api.Services.HumanResources;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? Environment.GetEnvironmentVariable("DB_CONNECTION")
    ?? throw new InvalidOperationException(
        "DB_CONNECTION tanımlı değil."
    );

var jwtSecret =
    builder.Configuration["Jwt:Secret"]
    ?? Environment.GetEnvironmentVariable("JWT_SECRET")
    ?? throw new InvalidOperationException(
        "JWT_SECRET tanımlı değil."
    );

builder.Services.AddScoped<AuditSaveChangesInterceptor>();

builder.Services.AddDbContext<AppDbContext>(
    (serviceProvider, options) =>
    {
        options.UseNpgsql(connectionString);
        options.AddInterceptors(
            serviceProvider.GetRequiredService<
                AuditSaveChangesInterceptor>());
    });

builder.Services.AddDbContext<HrDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

builder.Services.AddSingleton<IUploadService, UploadService>();
// Aktif e-posta kanalı. Sunucu sağlayıcısı 465'i açtığı için varsayılan
// SMTP; Brevo kodu yerinde duruyor ve EMAIL_PROVIDER=brevo ile tek satır
// değişiklikle geri alınabiliyor.
var emailProvider =
    (Environment.GetEnvironmentVariable("EMAIL_PROVIDER") ?? "smtp").Trim();

if (string.Equals(emailProvider, "brevo", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<
        EnderunAI.Api.Services.Email.IEmailService,
        EnderunAI.Api.Services.Email.EmailService>();
}
else
{
    builder.Services.AddScoped<
        EnderunAI.Api.Services.Email.IEmailService,
        EnderunAI.Api.Services.Email.SmtpEmailService>();
}
builder.Services.AddSingleton<EnderunAI.Api.Security.ILoginAttemptService, EnderunAI.Api.Security.LoginAttemptService>();

builder.Services.AddExceptionHandler<EnderunAI.Api.Security.GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddScoped<IAccountingAccountService, AccountingAccountService>();
builder.Services.AddScoped<IAccountingAccountSeedService, AccountingAccountSeedService>();
builder.Services.AddScoped<IAccountingVoucherService, AccountingVoucherService>();
builder.Services.AddScoped<IAccountingIntegrationService, AccountingIntegrationService>();
builder.Services.AddScoped<CurrentAccountCurrencyService>();
builder.Services.AddScoped<CurrencyValuationService>();
builder.Services.AddScoped<ISupplierInvoiceService, SupplierInvoiceService>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Inventory.ISupplierInvoiceStockPoster,
    EnderunAI.Api.Services.Inventory.SupplierInvoiceStockPoster>();
builder.Services.AddScoped<ISalesInvoiceService, SalesInvoiceService>();

// İş sağlığı ve güvenliği
builder.Services.AddScoped<
    EnderunAI.Api.Services.Isg.IIsgOsgbContractService,
    EnderunAI.Api.Services.Isg.IsgOsgbContractService>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Isg.IIsgPersonnelRecordService,
    EnderunAI.Api.Services.Isg.IsgPersonnelRecordService>();
builder.Services.AddScoped<
    EnderunAI.Api.Security.IIsgHealthVisibilityService,
    EnderunAI.Api.Security.IsgHealthVisibilityService>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Isg.IIsgIncidentService,
    EnderunAI.Api.Services.Isg.IsgIncidentService>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Isg.IIsgSiteDocumentService,
    EnderunAI.Api.Services.Isg.IsgSiteDocumentService>();

// E-fatura okuma: standart UBL-TR ayrıştırıcı önce, AI yedeği yalnızca
// o yetersiz kalırsa (token maliyeti).
builder.Services.AddScoped<
    EnderunAI.Api.Services.EInvoice.IAiInvoiceParser,
    EnderunAI.Api.Services.EInvoice.AiInvoiceParser>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.EInvoice.IEInvoiceReader,
    EnderunAI.Api.Services.EInvoice.EInvoiceReader>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.EInvoice.IEInvoiceImportService,
    EnderunAI.Api.Services.EInvoice.EInvoiceImportService>();
// Önizleme ile onay arasındaki geçici saklama ve XML arşivi süreç
// boyunca ortak olmalı; ikisi de singleton.
builder.Services.AddSingleton<
    EnderunAI.Api.Services.EInvoice.IEInvoiceStagingStore,
    EnderunAI.Api.Services.EInvoice.EInvoiceStagingStore>();
builder.Services.AddSingleton<
    EnderunAI.Api.Services.EInvoice.IEInvoiceArchive,
    EnderunAI.Api.Services.EInvoice.EInvoiceArchive>();
builder.Services.AddScoped<IChequeService, ChequeService>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Engineering.IPositionPriceService,
    EnderunAI.Api.Services.Engineering.PositionPriceService>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Engineering.IPositionImportService,
    EnderunAI.Api.Services.Engineering.PositionImportService>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Engineering.IBookImportService,
    EnderunAI.Api.Services.Engineering.BookImportService>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Engineering.IPositionMatchService,
    EnderunAI.Api.Services.Engineering.PositionMatchService>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Projects.IProjectCostAnalysisService,
    EnderunAI.Api.Services.Projects.ProjectCostAnalysisService>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Projects.IBoqItemCostService,
    EnderunAI.Api.Services.Projects.BoqItemCostService>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Projects.IBoqProfitService,
    EnderunAI.Api.Services.Projects.BoqProfitService>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Hakedis.IHakedisProfitService,
    EnderunAI.Api.Services.Hakedis.HakedisProfitService>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Projects.IProjectDeletionService,
    EnderunAI.Api.Services.Projects.ProjectDeletionService>();

// Piyasa verisi: TCMB kurları. Dış kaynak erişilemezse uygulama normal
// çalışır, yalnızca arşiv tazelenemez ve ekranlarda uyarı çıkar.
builder.Services.AddHttpClient<
    EnderunAI.Api.Services.Market.ITcmbRateClient,
    EnderunAI.Api.Services.Market.TcmbRateClient>(client =>
{
    client.BaseAddress = new Uri("https://www.tcmb.gov.tr/");
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddScoped<
    EnderunAI.Api.Services.Market.IExchangeRateService,
    EnderunAI.Api.Services.Market.ExchangeRateService>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Market.IInvoiceExchangeRateResolver,
    EnderunAI.Api.Services.Market.InvoiceExchangeRateResolver>();

// Emtia (bakır) fiyat kaynağı. LME resmî fiyatı ücretsiz/anahtarsız
// hiçbir yerde yayımlanmıyor; varsayılan kaynak COMEX bakır vadeli.
// METAL_API_KEY tanımlanırsa LME kaynağı devreye girer ve ekrandaki
// kaynak etiketi de buna göre değişir.
var metalApiKey = builder.Configuration["METAL_API_KEY"];

if (string.IsNullOrWhiteSpace(metalApiKey))
{
    builder.Services.AddHttpClient<
        EnderunAI.Api.Services.Market.ICommodityPriceSource,
        EnderunAI.Api.Services.Market.YahooComexCopperSource>(client =>
    {
        client.BaseAddress = new Uri("https://query1.finance.yahoo.com/");
        client.Timeout = TimeSpan.FromSeconds(20);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EnderunAI/1.0");
    });
}
else
{
    builder.Services.AddHttpClient<
        EnderunAI.Api.Services.Market.ICommodityPriceSource,
        EnderunAI.Api.Services.Market.MetalPriceApiLmeSource>(client =>
    {
        client.BaseAddress = new Uri("https://api.metalpriceapi.com/");
        client.Timeout = TimeSpan.FromSeconds(20);
    });
}

builder.Services.AddScoped<
    EnderunAI.Api.Services.Market.ICommodityPriceService,
    EnderunAI.Api.Services.Market.CommodityPriceService>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Market.ICopperExposureService,
    EnderunAI.Api.Services.Market.CopperExposureService>();
builder.Services.AddScoped<EnderunAI.Api.Services.Market.CommodityAlertService>();
builder.Services.AddScoped<EnderunAI.Api.Services.Offers.OfferBoqTransferService>();
builder.Services.AddScoped<EnderunAI.Api.Services.Offers.OfferContractService>();
builder.Services
    .AddScoped<EnderunAI.Api.Services.Purchasing.SupplierPriceIntelligenceService>();
builder.Services
    .AddScoped<EnderunAI.Api.Services.Purchasing.SupplierQualityService>();
builder.Services.AddScoped<EnderunAI.Api.Services.Assets.ToolServiceWorkflow>();
builder.Services.AddScoped<EnderunAI.Api.Services.Assets.ToolAssetAlertService>();
builder.Services.AddHostedService<
    EnderunAI.Api.Services.Market.MarketDataBackgroundService>();
builder.Services.AddSingleton<
    EnderunAI.Api.Services.Projects.IProjectFileCleaner,
    EnderunAI.Api.Services.Projects.ProjectFileCleaner>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Tax.ITaxLedgerService,
    EnderunAI.Api.Services.Tax.TaxLedgerService>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Tax.IVatAccrualService,
    EnderunAI.Api.Services.Tax.VatAccrualService>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Tax.ITaxObligationService,
    EnderunAI.Api.Services.Tax.TaxObligationService>();
builder.Services.AddScoped<IFactoringService, FactoringService>();
builder.Services.AddScoped<ICashFlowService, CashFlowService>();
builder.Services.AddScoped<ICashFlowProjectionService, CashFlowProjectionService>();
builder.Services.AddScoped<EnderunAI.Api.Services.Expenses.ExpenseCenterResolver>();
builder.Services.AddScoped<EnderunAI.Api.Services.Expenses.ExpenseEntryService>();
builder.Services.AddScoped<EnderunAI.Api.Services.Expenses.RecurringExpenseService>();
builder.Services.AddScoped<EnderunAI.Api.Services.Expenses.ExpenseCenterReportService>();
builder.Services.AddScoped<EnderunAI.Api.Services.Expenses.PartnerAccountService>();
builder.Services.AddScoped<EnderunAI.Api.Services.Notifications.NotificationStore>();
builder.Services.AddScoped<EnderunAI.Api.Services.Notifications.NotificationScanner>();

// Bildirim taraması günde bir koşuyor; desen
// MarketDataBackgroundService ile aynı (açılışta gecikme, hata yutup
// devam). Yeni tetikleyici eklemek için tek yapılacak şey
// INotificationSource uygulayan bir sınıf yazıp buraya kaydetmek.
builder.Services.AddHostedService<
    EnderunAI.Api.Services.Notifications.NotificationScanBackgroundService>();

// Finans tetikleyicileri.
builder.Services.AddScoped<
    EnderunAI.Api.Services.Notifications.INotificationSource,
    EnderunAI.Api.Services.Notifications.Sources.ChequeDueNotificationSource>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Notifications.INotificationSource,
    EnderunAI.Api.Services.Notifications.Sources.InvoiceDueNotificationSource>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Notifications.INotificationSource,
    EnderunAI.Api.Services.Notifications.Sources.LoanInstallmentNotificationSource>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Notifications.INotificationSource,
    EnderunAI.Api.Services.Notifications.Sources.CreditCardStatementNotificationSource>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Notifications.INotificationSource,
    EnderunAI.Api.Services.Notifications.Sources.DutySettlementNotificationSource>();

// Finansal araçlar ortak sözleşmeyi uyguluyor; nakit akış hepsini
// IEnumerable<IFinancialInstrumentSource> olarak okuyor. Yeni bir
// araç eklemek, projeksiyonu değiştirmeden bu listeye kayıt olmak.
builder.Services.AddScoped<EnderunAI.Api.Services.FinancialInstruments.BankLoanService>();
builder.Services.AddScoped<EnderunAI.Api.Services.FinancialInstruments.CreditCardService>();
builder.Services.AddScoped<EnderunAI.Api.Services.FinancialInstruments.IFinancialInstrumentSource>(
    x => x.GetRequiredService<EnderunAI.Api.Services.FinancialInstruments.BankLoanService>());
builder.Services.AddScoped<EnderunAI.Api.Services.FinancialInstruments.IFinancialInstrumentSource>(
    x => x.GetRequiredService<EnderunAI.Api.Services.FinancialInstruments.CreditCardService>());
builder.Services.AddScoped<EnderunAI.Api.Services.FinancialInstruments.IFinancialInstrumentSource,
    EnderunAI.Api.Services.FinancialInstruments.BarterInstrumentService>();
builder.Services.AddScoped<IHakedisAnalysisService, HakedisAnalysisService>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Hakedis.IProgressTrackingService,
    EnderunAI.Api.Services.Hakedis.ProgressTrackingService>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Hakedis.IContractSummaryProgressService,
    EnderunAI.Api.Services.Hakedis.ContractSummaryProgressService>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Schedule.IProjectScheduleService,
    EnderunAI.Api.Services.Schedule.ProjectScheduleService>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Schedule.IScheduleAlertService,
    EnderunAI.Api.Services.Schedule.ScheduleAlertService>();
builder.Services.AddScoped<ICostEngine, CostEngine>();
builder.Services.AddScoped<ISecretariatService, SecretariatService>();
builder.Services.AddScoped<IHrApprovalService, HrApprovalService>();

// Hızır asistanı
builder.Services.AddHttpClient<
    EnderunAI.Api.Services.Hizir.IHizirLlmClient,
    EnderunAI.Api.Services.Hizir.ClaudeLlmClient>();
builder.Services.AddSingleton<
    EnderunAI.Api.Services.Hizir.IHizirKnowledgeBase,
    EnderunAI.Api.Services.Hizir.HizirKnowledgeBase>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Hizir.IHizirPendingActionStore,
    EnderunAI.Api.Services.Hizir.HizirPendingActionStore>();
builder.Services.AddScoped<EnderunAI.Api.Services.Hizir.HizirActionTools>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Hizir.IHizirToolRegistry,
    EnderunAI.Api.Services.Hizir.HizirToolRegistry>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Hizir.IHizirActionAuditor,
    EnderunAI.Api.Services.Hizir.HizirActionAuditor>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Hizir.IHizirChatService,
    EnderunAI.Api.Services.Hizir.HizirChatService>();

// Günlük brifing. Yeni bir modül geldiğinde yapılacak tek şey
// IHizirBriefingSource uygulayan bir sınıf yazıp buraya eklemektir;
// brifing servisi değişmez.
builder.Services.AddMemoryCache();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Hizir.Briefing.IHizirBriefingSource,
    EnderunAI.Api.Services.Hizir.Briefing.PendingApprovalsBriefingSource>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Hizir.Briefing.IHizirBriefingSource,
    EnderunAI.Api.Services.Hizir.Briefing.ChequeDueBriefingSource>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Hizir.Briefing.IHizirBriefingSource,
    EnderunAI.Api.Services.Hizir.Briefing.MissingSiteReportBriefingSource>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Hizir.Briefing.IHizirBriefingSource,
    EnderunAI.Api.Services.Hizir.Briefing.CriticalStockBriefingSource>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Hizir.Briefing.IHizirBriefingSource,
    EnderunAI.Api.Services.Isg.IsgExpiryBriefingSource>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Hizir.Briefing.IHizirBriefingSource,
    EnderunAI.Api.Services.Isg.IsgIncidentBriefingSource>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Hizir.Briefing.IHizirBriefingSource,
    EnderunAI.Api.Services.Hizir.Briefing.OfferValidityBriefingSource>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Hizir.Briefing.IHizirBriefingSource,
    EnderunAI.Api.Services.Hizir.Briefing.HizirPendingActionBriefingSource>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Hizir.Briefing.IHizirBriefingSource,
    EnderunAI.Api.Services.Hizir.Briefing.ProjectCostOverrunBriefingSource>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Hizir.Briefing.IHizirBriefingSource,
    EnderunAI.Api.Services.Hizir.Briefing.TaxDueBriefingSource>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Hizir.Briefing.IHizirBriefingSource,
    EnderunAI.Api.Services.Hizir.Briefing.ProgressDeviationBriefingSource>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Hizir.Briefing.IHizirBriefingSource,
    EnderunAI.Api.Services.Market.MarketBriefingSource>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Hizir.Briefing.IHizirBriefingSource,
    EnderunAI.Api.Services.Purchasing.PurchasingBriefingSource>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Hizir.Briefing.IHizirBriefingSource,
    EnderunAI.Api.Services.Assets.ToolAssetBriefingSource>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Hizir.Briefing.IHizirBriefingSource,
    EnderunAI.Api.Services.Schedule.ScheduleBriefingSource>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Hizir.Briefing.IHizirBriefingService,
    EnderunAI.Api.Services.Hizir.Briefing.HizirBriefingService>();

builder.Services.AddScoped<PasswordService>();
builder.Services.AddScoped<TokenService>();

var defaultAllowedOrigins = new[]
{
    "https://enderunai.com.tr",
    "https://www.enderunai.com.tr",
    "https://enderun-ai.com",
    "https://www.enderun-ai.com",
    "https://srv.enderunai.com.tr",
    "http://enderunai.com.tr",
    "http://www.enderunai.com.tr",
    "http://enderun-ai.com",
    "http://www.enderun-ai.com",
    "http://srv.enderunai.com.tr"
};

var configuredOrigins = (Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS") ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

var allowedOrigins = configuredOrigins.Length > 0
    ? configuredOrigins
    : defaultAllowedOrigins;

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services
    .AddAuthentication(
        JwtBearerDefaults.AuthenticationScheme
    )
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,

                ValidIssuer = "EnderunAI",
                ValidAudience = "EnderunAI.Web",

                IssuerSigningKey =
                    new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtSecret)
                    ),

                ClockSkew = TimeSpan.FromMinutes(1),
            };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddScoped<IDocumentNumberService, DocumentNumberService>();
builder.Services.AddScoped<EnderunAI.Api.Services.Purchasing.Automation.IPurchaseRequestGenerator, EnderunAI.Api.Services.Purchasing.Automation.PurchaseRequestGenerator>();
builder.Services.AddScoped<EnderunAI.Api.Security.IUserAuthorizationService, EnderunAI.Api.Security.UserAuthorizationService>();
builder.Services.AddScoped<EnderunAI.Api.Security.ICurrentDataScopeService, EnderunAI.Api.Security.CurrentDataScopeService>();
builder.Services.AddScoped<EnderunAI.Api.Security.ISalaryVisibilityService, EnderunAI.Api.Security.SalaryVisibilityService>();
builder.Services.AddScoped<EnderunAI.Api.Security.IExtraPaymentVisibilityService, EnderunAI.Api.Security.ExtraPaymentVisibilityService>();
// Resmî net + elden ödeme + toplam ele geçen hesabı; ücret kartı
// listesi ve personel 360 kartı aynı kaynaktan beslensin diye ortak.
builder.Services.AddScoped<EnderunAI.Api.Services.HumanResources.SalaryTakeHomeService>();
builder.Services
    .AddScoped<EnderunAI.Api.Services.HumanResources.RehireGuardService>();
builder.Services
    .AddScoped<EnderunAI.Api.Services.HumanResources.DutyExpensePostingService>();
builder.Services
    .AddScoped<EnderunAI.Api.Services.HumanResources.ActualDailyWageService>();
builder.Services
    .AddScoped<EnderunAI.Api.Services.HumanResources.ExtraPaymentAllocationService>();
// Taşeron ekibi (SGK bizde) ve o ekibin bordro maliyeti.
builder.Services.AddScoped<EnderunAI.Api.Services.Subcontractors.SubcontractorTeamService>();
// Taşeron hakedişinin kesinti kalemlerini sözleşmenin kapsam
// tiklerinden kurar; tutarlar öneri olarak gelir.
builder.Services
    .AddScoped<EnderunAI.Api.Services.Subcontractors.SubcontractorReflectionSourceService>();
builder.Services.AddScoped<EnderunAI.Api.Services.Subcontractors.SubcontractorDeductionPlanner>();
builder.Services
    .AddScoped<EnderunAI.Api.Services.Subcontractors.SubcontractorInvoiceGenerator>();
// Taşeron ödemeleri, avansları ve mahsup takibi. Elden tutarlar ayrı
// tabloda; servis onları yalnızca yetki doğrulanmışsa sorguluyor.
builder.Services.AddScoped<EnderunAI.Api.Services.Subcontractors.SubcontractorLedgerService>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.HumanResources.IPersonnelTerminationService,
    EnderunAI.Api.Services.HumanResources.PersonnelTerminationService>();
builder.Services.AddScoped<EnderunAI.Api.Services.Rfq.IRfqService, EnderunAI.Api.Services.Rfq.RfqService>();
builder.Services.AddScoped<EnderunAI.Api.Services.PurchaseOrders.IPurchaseOrderService, EnderunAI.Api.Services.PurchaseOrders.PurchaseOrderService>();
builder.Services.AddScoped<EnderunAI.Api.Services.GoodsReceipts.IGoodsReceiptService, EnderunAI.Api.Services.GoodsReceipts.GoodsReceiptService>();
builder.Services.AddScoped<EnderunAI.Api.Security.IWorkHourAccessService, EnderunAI.Api.Security.WorkHourAccessService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("portal", context =>
    {
        var token = context.Request.RouteValues["token"]?.ToString() ?? "unknown";
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            $"{token}:{ip}",
            _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 60,
                QueueLimit = 0
            });
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db =
        scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

    if (string.Equals(app.Configuration["MigrationRecovery:AllowAutomaticDatabaseUpdate"], "true", StringComparison.OrdinalIgnoreCase)) await db.Database.MigrateAsync();

    var hrDb =
        scope.ServiceProvider
            .GetRequiredService<HrDbContext>();

    if (string.Equals(app.Configuration["MigrationRecovery:AllowAutomaticDatabaseUpdate"], "true", StringComparison.OrdinalIgnoreCase)) await hrDb.Database.MigrateAsync();

    var passwordService =
        scope.ServiceProvider
            .GetRequiredService<PasswordService>();

    await DatabaseSeeder.SeedAsync(
        db,
        passwordService,
        builder.Configuration
    );
}

app.UseExceptionHandler();

app.UseRouting();

app.UseCors("Frontend");

app.UseAuthentication();
app.UseMiddleware<EnderunAI.Api.Security.WorkHourAccessMiddleware>();
app.UseMiddleware<PermissionAuthorizationMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

app.MapGet("/api/health", () =>
{
    return Results.Ok(new
    {
        status = "ok",
        service = "EnderunAI.Api",
        utc = DateTime.UtcNow,
    });
});

app.Run();

public partial class Program;
