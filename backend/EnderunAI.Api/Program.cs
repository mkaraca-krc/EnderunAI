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
builder.Services.AddHttpClient<EnderunAI.Api.Services.Email.IEmailService, EnderunAI.Api.Services.Email.EmailService>();
builder.Services.AddSingleton<EnderunAI.Api.Security.ILoginAttemptService, EnderunAI.Api.Security.LoginAttemptService>();

builder.Services.AddExceptionHandler<EnderunAI.Api.Security.GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddScoped<IAccountingAccountService, AccountingAccountService>();
builder.Services.AddScoped<IAccountingAccountSeedService, AccountingAccountSeedService>();
builder.Services.AddScoped<IAccountingVoucherService, AccountingVoucherService>();
builder.Services.AddScoped<IAccountingIntegrationService, AccountingIntegrationService>();
builder.Services.AddScoped<ISupplierInvoiceService, SupplierInvoiceService>();
builder.Services.AddScoped<IChequeService, ChequeService>();
builder.Services.AddScoped<IFactoringService, FactoringService>();
builder.Services.AddScoped<ICashFlowService, CashFlowService>();
builder.Services.AddScoped<IHakedisAnalysisService, HakedisAnalysisService>();
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
    EnderunAI.Api.Services.Hizir.IHizirToolRegistry,
    EnderunAI.Api.Services.Hizir.HizirToolRegistry>();
builder.Services.AddScoped<
    EnderunAI.Api.Services.Hizir.IHizirChatService,
    EnderunAI.Api.Services.Hizir.HizirChatService>();

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
