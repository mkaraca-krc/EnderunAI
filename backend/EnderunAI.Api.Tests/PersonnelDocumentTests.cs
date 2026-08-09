using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.HumanResources;
using EnderunAI.Api.Security;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Personel özlük belgeleri (H8).
///
/// Asıl güvence GİZLİLİK: bu kayıtlar kimlik fotokopisi ve adli sicil
/// gibi belgeler taşıyor. personnel.view izni sahada da var (Şantiye
/// Şefi, Formen) ve bu belgeler oradan görünmemeli — kendi dar
/// anahtarıyla korunuyor, tıpkı elden ödemenin extra_payment.* ile
/// korunması gibi.
///
/// İkinci güvence: dosya deposu ve geçerlilik hesabı YENİDEN
/// YAZILMADI; şantiye fotoğrafları ve İSG belgeleriyle aynı depo, İSG
/// ile aynı eşikler.
/// </summary>
[Collection("Integration")]
public sealed class PersonnelDocumentTests(DatabaseFixture fixture)
{
    private sealed record Context(Guid CompanyId, Guid PersonnelId);

    private async Task<HttpClient> ClientAsync() =>
        await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    private async Task<Context> CreateContextAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);
        var personnel = await TestDataFactory.CreatePersonnelAsync(
            db, company.Id, suffix);

        return new Context(company.Id, personnel.Id);
    }

    private static MultipartFormDataContent Form(
        Guid personnelId,
        int documentType,
        string title,
        DateTime? issueDate = null,
        DateTime? expiryDate = null,
        string fileName = "sozlesme.pdf")
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(personnelId.ToString()), "personnelId" },
            { new StringContent(documentType.ToString()), "documentType" },
            { new StringContent(title), "title" },
            { new StringContent("false"), "isMandatory" }
        };

        if (issueDate is DateTime issued)
            form.Add(new StringContent(issued.ToString("yyyy-MM-dd")), "issueDate");

        if (expiryDate is DateTime expires)
            form.Add(new StringContent(expires.ToString("yyyy-MM-dd")), "expiryDate");

        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("test-belge-icerigi"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", fileName);

        return form;
    }

    private async Task<Guid> UploadAsync(
        HttpClient client,
        Context context,
        int documentType = (int)PersonnelDocumentType.EmploymentContract,
        string title = "İş sözleşmesi",
        DateTime? expiryDate = null)
    {
        var response = await client.PostAsync(
            "/api/hr/personel-belgeleri",
            Form(context.PersonnelId, documentType, title,
                new DateTime(2026, 1, 15), expiryDate));

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> ListAsync(
        HttpClient client, Context context, bool expiringOnly = false)
    {
        var response = await client.GetAsync(
            $"/api/hr/personel-belgeleri?personnelId={context.PersonnelId}" +
            (expiringOnly ? "&expiringOnly=true" : ""));

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // ---------- Yükleme ve okuma ----------

    [Fact]
    public async Task UploadedDocument_AppearsInTheList()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await UploadAsync(client, context);

        var payload = await ListAsync(client, context);
        var item = payload.GetProperty("items").EnumerateArray().Single();

        Assert.Equal("İş sözleşmesi", item.GetProperty("title").GetString());
        Assert.Equal("İş sözleşmesi",
            item.GetProperty("documentTypeName").GetString());
        Assert.Equal("sozlesme.pdf", item.GetProperty("originalName").GetString());
    }

    /// <summary>
    /// Doğrulama durumu LİSTEDE dönüyor: özlük ekranındaki "aslı
    /// görüldü / görülmedi" rozetinin kaynağı bu. Yalnızca kayda
    /// yazılıp listede dönmeseydi ekran her belgeyi doğrulanmamış
    /// gösterirdi.
    /// </summary>
    [Fact]
    public async Task VerificationState_IsVisibleInTheList()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await UploadAsync(client, context);

        var before = (await ListAsync(client, context))
            .GetProperty("items").EnumerateArray().Single();

        Assert.False(before.GetProperty("isVerified").GetBoolean());
        Assert.Equal(JsonValueKind.Null,
            before.GetProperty("verifiedAtUtc").ValueKind);

        var documentId = before.GetProperty("id").GetGuid();

        var verify = await client.PostAsJsonAsync(
            $"/api/hr/personel-belgeleri/{documentId}/dogrula",
            new { isVerified = true, notes = (string?)null });

        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

        var after = (await ListAsync(client, context))
            .GetProperty("items").EnumerateArray().Single();

        Assert.True(after.GetProperty("isVerified").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null,
            after.GetProperty("verifiedAtUtc").ValueKind);
    }

    /// <summary>Dosya depodan indirilebiliyor ve özgün adıyla dönüyor.</summary>
    [Fact]
    public async Task Document_CanBeDownloaded()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var id = await UploadAsync(client, context);

        var response = await client.GetAsync($"/api/hr/personel-belgeleri/{id}/indir");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "test-belge-icerigi", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DeletedDocument_Disappears()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var id = await UploadAsync(client, context);

        var response = await client.DeleteAsync($"/api/hr/personel-belgeleri/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await ListAsync(client, context))
            .GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task UploadWithoutTitle_IsRejected()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var response = await client.PostAsync(
            "/api/hr/personel-belgeleri",
            Form(context.PersonnelId, 0, "   "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnknownDocumentType_IsRejected()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var response = await client.PostAsync(
            "/api/hr/personel-belgeleri",
            Form(context.PersonnelId, 77, "Bilinmeyen"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ExpiryBeforeIssueDate_IsRejected()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var response = await client.PostAsync(
            "/api/hr/personel-belgeleri",
            Form(context.PersonnelId, 3, "Ehliyet",
                new DateTime(2026, 6, 1), new DateTime(2026, 1, 1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Geçerlilik ----------

    /// <summary>
    /// Süresiz belge (diploma) uyarı listesine girmez.
    /// </summary>
    [Fact]
    public async Task DocumentWithoutExpiry_IsNotInTheExpiringList()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await UploadAsync(client, context,
            (int)PersonnelDocumentType.Diploma, "Diploma");

        Assert.Empty((await ListAsync(client, context, expiringOnly: true))
            .GetProperty("items").EnumerateArray());
    }

    /// <summary>
    /// Süresi geçmiş belge uyarı listesinde ve durumu İSG ile aynı
    /// hesaptan geliyor.
    /// </summary>
    [Fact]
    public async Task ExpiredDocument_IsReported()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await UploadAsync(client, context,
            (int)PersonnelDocumentType.DriverLicense, "Ehliyet",
            expiryDate: DateTime.UtcNow.Date.AddDays(-10));

        var payload = await ListAsync(client, context, expiringOnly: true);

        Assert.Equal(1, payload.GetProperty("expiredCount").GetInt32());
        Assert.Equal("Süresi doldu",
            payload.GetProperty("items").EnumerateArray().Single()
                .GetProperty("statusName").GetString());
    }

    // ---------- Gizlilik ----------

    /// <summary>
    /// personnel.view TEK BAŞINA yetmiyor: sahadaki roller (Şantiye
    /// Şefi, Formen) bu izne sahip ve kimlik fotokopisi görmemeli.
    /// </summary>
    [Fact]
    public async Task PersonnelViewAlone_CannotReadDocuments()
    {
        var context = await CreateContextAsync();
        var admin = await ClientAsync();

        await UploadAsync(admin, context);

        var client = await CreateClientWithPermissionsAsync(
            PermissionCatalog.Keys.PersonnelView);

        var response = await client.GetAsync(
            $"/api/hr/personel-belgeleri?personnelId={context.PersonnelId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PersonnelViewAlone_CannotDownload()
    {
        var context = await CreateContextAsync();
        var admin = await ClientAsync();

        var id = await UploadAsync(admin, context);

        var client = await CreateClientWithPermissionsAsync(
            PermissionCatalog.Keys.PersonnelView);

        var response = await client.GetAsync(
            $"/api/hr/personel-belgeleri/{id}/indir");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Okuma yetkisi YÜKLEME yetkisi değil: belge görebilen herkes
    /// özlük dosyasına ekleme yapamaz.
    /// </summary>
    [Fact]
    public async Task ViewPermission_CannotUpload()
    {
        var context = await CreateContextAsync();

        var client = await CreateClientWithPermissionsAsync(
            PermissionCatalog.Keys.PersonnelDocumentView,
            PermissionCatalog.Keys.PersonnelView);

        var response = await client.PostAsync(
            "/api/hr/personel-belgeleri",
            Form(context.PersonnelId, 0, "İş sözleşmesi"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DocumentPermission_CanReadAndDownload()
    {
        var context = await CreateContextAsync();
        var admin = await ClientAsync();

        var id = await UploadAsync(admin, context);

        var client = await CreateClientWithPermissionsAsync(
            PermissionCatalog.Keys.PersonnelDocumentView);

        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync(
                $"/api/hr/personel-belgeleri?personnelId={context.PersonnelId}"))
            .StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync($"/api/hr/personel-belgeleri/{id}/indir"))
            .StatusCode);
    }

    private async Task<HttpClient> CreateClientWithPermissionsAsync(
        params string[] permissionKeys)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        string username;
        const string password = "TestBelge!2026";

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var passwords = scope.ServiceProvider
                .GetRequiredService<PasswordService>();

            var role = new AppRole { Name = $"TestBelge-{suffix}" };
            db.Roles.Add(role);
            await db.SaveChangesAsync();

            var permissions = await db.Permissions
                .Where(x => permissionKeys.Contains(x.Key))
                .ToListAsync();

            foreach (var permission in permissions)
            {
                db.RolePermissions.Add(new RolePermission
                {
                    RoleId = role.Id,
                    PermissionId = permission.Id
                });
            }

            username = $"belge-{suffix}";
            var hash = passwords.Hash(password);

            var user = new AppUser
            {
                Username = username,
                FullName = "Test Belge Kullanıcısı",
                PasswordHash = hash.Hash,
                PasswordSalt = hash.Salt,
                IsActive = true,
                WorkHoursExempt = true
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();

            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
            db.UserDataScopes.Add(new UserDataScope
            {
                UserId = user.Id,
                ScopeType = DataScopeType.All
            });

            await db.SaveChangesAsync();
        }

        var client = fixture.Factory.CreateClient();
        var token = await AuthHelper.LoginAsync(client, username, password);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }
}
