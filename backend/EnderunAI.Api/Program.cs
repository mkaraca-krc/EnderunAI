using EnderunAI.Api.Services.Costing;
using EnderunAI.Api.Services.DocumentNumbers;
using EnderunAI.Api.Services.AI;
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
    ?? throw new InvalidOperationException(
        "DB_CONNECTION tanımlı değil."
    );

var jwtSecret =
    builder.Configuration["Jwt:Secret"]
    ?? Environment.GetEnvironmentVariable("JWT_SECRET")
    ?? throw new InvalidOperationException(
        "JWT_SECRET tanımlı değil."
    );

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

builder.Services.AddHttpClient("OpenAI", client =>
{
    client.Timeout = TimeSpan.FromSeconds(90);
});

builder.Services.AddSingleton<IUploadService, UploadService>();
builder.Services.AddScoped<IHakedisAnalysisService, HakedisAnalysisService>();
builder.Services.AddScoped<IHizirChatService, HizirChatService>();
builder.Services.AddScoped<ICostEngine, CostEngine>();

builder.Services.AddScoped<PasswordService>();
builder.Services.AddScoped<TokenService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .AllowAnyOrigin()
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
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db =
        scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

    await db.Database.MigrateAsync();

    var passwordService =
        scope.ServiceProvider
            .GetRequiredService<PasswordService>();

    await DatabaseSeeder.SeedAsync(
        db,
        passwordService,
        builder.Configuration
    );
}

app.UseRouting();

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

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
