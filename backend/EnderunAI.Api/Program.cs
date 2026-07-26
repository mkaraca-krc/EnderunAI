using EnderunAI.Api.Services.AI;
using EnderunAI.Api.Services.Inventory;
using EnderunAI.Api.Services.Procurement;
using System.Text;
using EnderunAI.Api.Data;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Upload;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? Environment.GetEnvironmentVariable("DB_CONNECTION")
    ?? throw new InvalidOperationException("DB_CONNECTION tanımlı değil.");

var jwtSecret =
    builder.Configuration["Jwt:Secret"]
    ?? Environment.GetEnvironmentVariable("JWT_SECRET")
    ?? throw new InvalidOperationException("JWT_SECRET tanımlı değil.");

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddDbContext<ProcurementDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddDbContext<ProcurementApprovalDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddDbContext<ProcurementDocumentDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddDbContext<ProcurementNotificationDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddDbContext<ProcurementTechnicalDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddDbContext<SupplierPerformanceDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddHttpClient("OpenAI", client => client.Timeout = TimeSpan.FromSeconds(90));

builder.Services.AddSingleton<IUploadService, UploadService>();
builder.Services.AddScoped<IHakedisAnalysisService, HakedisAnalysisService>();
builder.Services.AddScoped<IHizirDashboardAggregator, HizirDashboardAggregator>();
builder.Services.AddScoped<IHizirChatService, HizirChatService>();
builder.Services.AddScoped<IHizirActionService, HizirActionService>();
builder.Services.AddScoped<IGoodsReceiptPostingService, GoodsReceiptPostingService>();
builder.Services.AddScoped<IOfferEvaluationService, OfferEvaluationService>();
builder.Services.AddScoped<IProcurementApprovalService, ProcurementApprovalService>();
builder.Services.AddScoped<IProcurementNotificationService, ProcurementNotificationService>();
builder.Services.AddScoped<ITechnicalComplianceService, TechnicalComplianceService>();
builder.Services.AddScoped<ISupplierPerformanceService, SupplierPerformanceService>();
builder.Services.AddHostedService<ProcurementNotificationWorker>();

builder.Services.AddScoped<PasswordService>();
builder.Services.AddScoped<TokenService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = "EnderunAI",
            ValidAudience = "EnderunAI.Web",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    var procurementDb = scope.ServiceProvider.GetRequiredService<ProcurementDbContext>();
    await procurementDb.Database.MigrateAsync();

    var approvalDb = scope.ServiceProvider.GetRequiredService<ProcurementApprovalDbContext>();
    await approvalDb.Database.MigrateAsync();

    var documentDb = scope.ServiceProvider.GetRequiredService<ProcurementDocumentDbContext>();
    await documentDb.Database.MigrateAsync();

    var notificationDb = scope.ServiceProvider.GetRequiredService<ProcurementNotificationDbContext>();
    await notificationDb.Database.MigrateAsync();

    var technicalDb = scope.ServiceProvider.GetRequiredService<ProcurementTechnicalDbContext>();
    await technicalDb.Database.MigrateAsync();

    var supplierPerformanceDb = scope.ServiceProvider.GetRequiredService<SupplierPerformanceDbContext>();
    await supplierPerformanceDb.Database.MigrateAsync();

    var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();
    await DatabaseSeeder.SeedAsync(db, passwordService, builder.Configuration);
}

app.UseRouting();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    service = "EnderunAI.Api",
    utc = DateTime.UtcNow,
}));

app.Run();
