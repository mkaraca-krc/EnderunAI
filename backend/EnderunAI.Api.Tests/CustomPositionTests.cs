using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Engineering;
using EnderunAI.Api.Services.Hizir;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Kütüphanede karşılığı olmayan kalemin şirkete özel poz olarak
/// açılması.
///
/// Asıl güvence: açılan poz BİR SONRAKİ projede kütüphaneden gelmeli.
/// Zamanla şirkete özel poz havuzu oluşması buna bağlı; poz açılıp
/// aramada çıkmazsa özellik hiçbir işe yaramaz.
/// </summary>
[Collection("Integration")]
public sealed class CustomPositionTests(DatabaseFixture fixture)
{
    private sealed class OffLlm : IHizirLlmClient
    {
        public bool IsConfigured => false;
        public string ModelId => "kapalı";

        public Task<LlmCompletion> CompleteAsync(
            string systemPrompt,
            IReadOnlyList<LlmMessage> messages,
            IReadOnlyList<LlmToolDefinition> tools,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private async Task<HttpClient> CreateClientAsync(string roleName)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

        const string password = "CustomPoz!2026";
        var username = $"test-poz-{Guid.NewGuid():N}"[..40];
        var hash = passwordService.Hash(password);

        var user = new AppUser
        {
            Username = username,
            FullName = $"Test {roleName}",
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            IsActive = true,
            WorkHoursExempt = true
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        var role = await db.Roles.SingleAsync(x => x.Name == roleName);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        db.UserDataScopes.Add(new UserDataScope
        {
            UserId = user.Id,
            ScopeType = DataScopeType.All
        });

        await db.SaveChangesAsync();

        var client = fixture.Factory.CreateClient();
        var token = await AuthHelper.LoginAsync(client, username, password);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private static async Task<Guid> CreateCompanyAsync(AppDbContext db, string suffix)
    {
        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);
        return company.Id;
    }

    [Fact]
    public async Task Create_GeneratesCodeAndMarksAsCompanyOwned()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        Guid companyId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            companyId = await CreateCompanyAsync(db, suffix);
        }

        var client = await CreateClientAsync("Genel Müdür");

        var response = await client.PostAsJsonAsync("/api/engineering-positions/custom", new
        {
            companyId,
            name = "Özel kablo kanalı imalatı",
            unit = "m",
            discipline = 0,
            unitPrice = 450.75m,
            year = 2026
        });

        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();

        var code = created.GetProperty("code").GetString();
        Assert.False(string.IsNullOrWhiteSpace(code));
        Assert.Equal("Şirket", created.GetProperty("institution").GetString());

        using var verifyScope = fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var position = await verifyDb.EngineeringPositions
            .AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId && x.Code == code);

        Assert.Equal(EngineeringPositionSource.Enderun, position.Source);

        // Taslak bırakılmamalı; keşifte hemen kullanılabilmeli.
        Assert.Equal(EngineeringPositionStatus.Active, position.Status);

        var price = await verifyDb.PositionUnitPrices
            .AsNoTracking()
            .SingleAsync(x => x.EngineeringPositionId == position.Id);

        Assert.Equal(450.75m, price.UnitPrice);
        Assert.Equal(PositionPriceInstitution.Company, price.Institution);
        Assert.Equal(2026, price.Year);
    }

    [Fact]
    public async Task Create_IsFoundByMatcherAfterwards()
    {
        // ASIL GÜVENCE: bir sonraki projede kütüphaneden gelmeli.
        var suffix = Guid.NewGuid().ToString("N")[..8];

        Guid companyId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            companyId = await CreateCompanyAsync(db, suffix);
        }

        var client = await CreateClientAsync("Genel Müdür");

        (await client.PostAsJsonAsync("/api/engineering-positions/custom", new
        {
            companyId,
            name = "Paslanmaz çelik kablo merdiveni montajı",
            unit = "m",
            discipline = 0,
            unitPrice = 980m
        })).EnsureSuccessStatusCode();

        using var scope2 = fixture.Factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();

        var matcher = new PositionMatchService(
            db2, new PositionPriceService(db2), new OffLlm(),
            NullLogger<PositionMatchService>.Instance);

        var result = await matcher.SuggestAsync(
            companyId, "paslanmaz çelik kablo merdiveni montajı", useAi: false);

        Assert.NotEmpty(result.Suggestions);
        Assert.Equal(
            "Paslanmaz çelik kablo merdiveni montajı", result.Suggestions[0].Name);
        Assert.Equal(980m, result.Suggestions[0].UnitPrice);
    }

    [Fact]
    public async Task Create_WithExplicitCode_UsesIt()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        Guid companyId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            companyId = await CreateCompanyAsync(db, suffix);
        }

        var client = await CreateClientAsync("Genel Müdür");
        var code = $"OZL-{suffix}";

        var response = await client.PostAsJsonAsync("/api/engineering-positions/custom", new
        {
            companyId,
            name = "Elle kodlanan özel iş",
            unit = "Ad",
            discipline = 0,
            code
        });

        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(code.ToUpperInvariant(), created.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_DuplicateCode_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        Guid companyId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            companyId = await CreateCompanyAsync(db, suffix);
        }

        var client = await CreateClientAsync("Genel Müdür");
        var code = $"OZL-{suffix}";

        var payload = new
        {
            companyId,
            name = "İlk kayıt",
            unit = "Ad",
            discipline = 0,
            code
        };

        (await client.PostAsJsonAsync("/api/engineering-positions/custom", payload))
            .EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync(
            "/api/engineering-positions/custom", payload);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutUnit_FallsBackToPiece()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        Guid companyId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            companyId = await CreateCompanyAsync(db, suffix);
        }

        var client = await CreateClientAsync("Genel Müdür");

        var response = await client.PostAsJsonAsync("/api/engineering-positions/custom", new
        {
            companyId,
            name = "Birimsiz özel iş",
            discipline = 0
        });

        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("AD", created.GetProperty("unit").GetString());
    }

    [Fact]
    public async Task Create_WithoutName_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        Guid companyId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            companyId = await CreateCompanyAsync(db, suffix);
        }

        var client = await CreateClientAsync("Genel Müdür");

        var response = await client.PostAsJsonAsync("/api/engineering-positions/custom", new
        {
            companyId,
            name = "   ",
            unit = "Ad",
            discipline = 0
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutEngineeringManage_IsForbidden()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        Guid companyId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            companyId = await CreateCompanyAsync(db, suffix);
        }

        // Şantiye Şefi'nde engineering.manage yok.
        var client = await CreateClientAsync("Şantiye Şefi");

        var response = await client.PostAsJsonAsync("/api/engineering-positions/custom", new
        {
            companyId,
            name = "Yetkisiz deneme",
            unit = "Ad",
            discipline = 0
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
