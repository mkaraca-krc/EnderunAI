using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Satın alma talebinin reddi ve düzeltmeye iadesi (S1).
///
/// Asıl güvenceler:
/// - RED ve İADE ayrı şeylerdir: red kapıyı kapatır, iade "şunu
///   düzelt ve yeniden gönder" der. Tek duruma sıkıştırmak
///   düzeltilip alınabilecek işleri de öldürürdü.
/// - İkisinde de GEREKÇE ZORUNLU. Gerekçesiz red talep sahibine neyi
///   yanlış yaptığını söylemez ve aynı talep birkaç gün sonra aynı
///   haliyle geri gelir; gerekçesiz iade ise talebi ne yapacağı belli
///   olmadan bekletir.
/// - İade edilen talep DÜZENLENEBİLİR ve yeniden gönderilebilir;
///   yalnız taslağa izin verilseydi iade edilen talep ölü kalırdı.
/// - Reddedilen talep nihaidir.
/// </summary>
[Collection("Integration")]
public sealed class PurchaseRequestRejectionTests(DatabaseFixture fixture)
{
    private sealed record Context(Guid CompanyId, Guid ProjectId);

    private async Task<Context> CreateContextAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        return new Context(project.CompanyId, project.Id);
    }

    private async Task<HttpClient> ClientAsync() =>
        await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    private static object RequestPayload(Context context, decimal quantity = 10m) =>
        new
        {
            companyId = context.CompanyId,
            projectId = context.ProjectId,
            requestDate = DateTime.UtcNow.Date,
            neededByDate = (DateTime?)null,
            requestedByName = "Şantiye Şefi",
            description = "Kalıp malzemesi",
            priority = 1,
            items = new[]
            {
                new
                {
                    materialDescription = "Kalıp tahtası",
                    quantity,
                    unit = "adet",
                    requestedDeliveryDate = (DateTime?)null,
                    notes = (string?)null
                }
            }
        };

    private async Task<Guid> SubmittedRequestAsync(
        HttpClient client, Context context)
    {
        var created = await client.PostAsJsonAsync(
            "/api/purchase-requests", RequestPayload(context));

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var id = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync(
                $"/api/purchase-requests/{id}/submit", new { })).StatusCode);

        return id;
    }

    // ---------- Red ----------

    /// <summary>
    /// Red gerekçesiyle birlikte kaydedilir ve talep nihai olur.
    /// </summary>
    [Fact]
    public async Task Reject_StoresReasonAndIsFinal()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();
        var id = await SubmittedRequestAsync(client, context);

        var response = await client.PostAsJsonAsync(
            $"/api/purchase-requests/{id}/reject",
            new { reason = "Bütçe dışı; bu kalem gelecek aya ertelendi." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var entity = await db.PurchaseRequests.SingleAsync(x => x.Id == id);

        Assert.Equal(PurchaseRequestStatus.Rejected, entity.Status);
        Assert.Contains("Bütçe dışı", entity.RejectionReason!);
        Assert.NotNull(entity.RejectedAtUtc);
        Assert.NotNull(entity.RejectedByUserId);
    }

    /// <summary>Gerekçesiz red kabul edilmez.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Reject_WithoutReason_IsRejected(string reason)
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();
        var id = await SubmittedRequestAsync(client, context);

        var response = await client.PostAsJsonAsync(
            $"/api/purchase-requests/{id}/reject", new { reason });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var entity = await db.PurchaseRequests.SingleAsync(x => x.Id == id);
        Assert.Equal(PurchaseRequestStatus.Submitted, entity.Status);
    }

    /// <summary>
    /// Reddedilen talep yeniden gönderilemez ve düzenlenemez —
    /// nihai karar.
    /// </summary>
    [Fact]
    public async Task Rejected_CannotBeResubmittedOrEdited()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();
        var id = await SubmittedRequestAsync(client, context);

        await client.PostAsJsonAsync(
            $"/api/purchase-requests/{id}/reject",
            new { reason = "Uygun değil" });

        var resubmit = await client.PostAsJsonAsync(
            $"/api/purchase-requests/{id}/submit", new { });

        Assert.Equal(HttpStatusCode.Conflict, resubmit.StatusCode);
    }

    /// <summary>
    /// Taslak talep reddedilemez: henüz kimseye sunulmamış bir talebi
    /// reddetmek anlamsız, sahibi zaten iptal edebilir.
    /// </summary>
    [Fact]
    public async Task Reject_OnDraft_IsRejected()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var created = await client.PostAsJsonAsync(
            "/api/purchase-requests", RequestPayload(context));

        var id = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/purchase-requests/{id}/reject", new { reason = "Olmaz" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---------- Düzeltmeye iade ----------

    /// <summary>
    /// İade edilen talep düzeltilip yeniden gönderilebilir; revizyon
    /// sayacı artar ve eski iade gerekçesi temizlenir (onaylayanı
    /// yanıltmasın).
    /// </summary>
    [Fact]
    public async Task Return_ThenFixAndResubmit_Works()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();
        var id = await SubmittedRequestAsync(client, context);

        var returned = await client.PostAsJsonAsync(
            $"/api/purchase-requests/{id}/iade",
            new { reason = "Miktar fazla görünüyor, metrajla teyit edin." });

        Assert.Equal(HttpStatusCode.OK, returned.StatusCode);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = await db.PurchaseRequests.SingleAsync(x => x.Id == id);

            Assert.Equal(PurchaseRequestStatus.ReturnedForRevision, entity.Status);
            Assert.Contains("metrajla", entity.ReturnReason!);
            Assert.NotNull(entity.ReturnedAtUtc);
            Assert.Equal(0, entity.RevisionCount);
        }

        // Talep sahibi düzeltiyor.
        var update = await client.PutAsJsonAsync(
            $"/api/purchase-requests/{id}",
            new
            {
                requestDate = DateTime.UtcNow.Date,
                neededByDate = (DateTime?)null,
                requestedByName = "Şantiye Şefi",
                description = "Kalıp malzemesi (metraj sonrası düzeltildi)",
                priority = 1,
                items = new[]
                {
                    new
                    {
                        materialDescription = "Kalıp tahtası",
                        quantity = 6m,
                        unit = "adet",
                        requestedDeliveryDate = (DateTime?)null,
                        notes = (string?)null
                    }
                }
            });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var resubmit = await client.PostAsJsonAsync(
            $"/api/purchase-requests/{id}/submit", new { });

        Assert.Equal(HttpStatusCode.OK, resubmit.StatusCode);

        var body = await resubmit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("revisionCount").GetInt32());

        using var verify = fixture.Factory.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var final = await verifyDb.PurchaseRequests
            .Include(x => x.Items)
            .SingleAsync(x => x.Id == id);

        Assert.Equal(PurchaseRequestStatus.Submitted, final.Status);
        Assert.Equal(1, final.RevisionCount);
        Assert.Equal(6m, final.Items.Single().Quantity);

        // Düzeltilmiş talep onaya giderken eski iade gerekçesi
        // taşınmaz.
        Assert.Null(final.ReturnReason);
    }

    /// <summary>Gerekçesiz iade kabul edilmez.</summary>
    [Fact]
    public async Task Return_WithoutReason_IsRejected()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();
        var id = await SubmittedRequestAsync(client, context);

        var response = await client.PostAsJsonAsync(
            $"/api/purchase-requests/{id}/iade", new { reason = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// İade edilen talep onaylanamaz — önce düzeltilip yeniden
    /// gönderilmeli.
    /// </summary>
    [Fact]
    public async Task Returned_CannotBeApprovedDirectly()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();
        var id = await SubmittedRequestAsync(client, context);

        await client.PostAsJsonAsync(
            $"/api/purchase-requests/{id}/iade", new { reason = "Düzelt" });

        var approve = await client.PostAsJsonAsync(
            $"/api/purchase-requests/{id}/approve", new { });

        Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode);
    }

    /// <summary>
    /// Onaylanmış talep artık iade edilemez; sipariş süreci başlamış
    /// olabilir.
    /// </summary>
    [Fact]
    public async Task Return_AfterApproval_IsRejected()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();
        var id = await SubmittedRequestAsync(client, context);

        await client.PostAsJsonAsync(
            $"/api/purchase-requests/{id}/approve", new { });

        var response = await client.PostAsJsonAsync(
            $"/api/purchase-requests/{id}/iade", new { reason = "Vazgeçtik" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---------- Projeksiyon ----------

    /// <summary>
    /// Gerekçe listede ve detayda görünür; ekran onu göstermek zorunda
    /// yoksa talep sahibi neden geri geldiğini bilemez.
    /// </summary>
    [Fact]
    public async Task Reasons_AreVisibleInListAndDetail()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();
        var id = await SubmittedRequestAsync(client, context);

        await client.PostAsJsonAsync(
            $"/api/purchase-requests/{id}/iade",
            new { reason = "Poz numarası eksik" });

        var list = await client.GetFromJsonAsync<JsonElement>(
            $"/api/purchase-requests?projectId={context.ProjectId}");

        var row = list.EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == id);

        Assert.Equal(
            (int)PurchaseRequestStatus.ReturnedForRevision,
            row.GetProperty("status").GetInt32());

        Assert.Equal(
            "Poz numarası eksik", row.GetProperty("returnReason").GetString());

        var detail = await client.GetFromJsonAsync<JsonElement>(
            $"/api/purchase-requests/{id}");

        Assert.Equal(
            "Poz numarası eksik", detail.GetProperty("returnReason").GetString());
        Assert.Equal(
            JsonValueKind.Null, detail.GetProperty("rejectionReason").ValueKind);
    }

    // ---------- Yetki ----------

    /// <summary>
    /// Red ve iade ONAY yetkisi ister. Talep açabilen herkes kendi
    /// talebini reddedebilseydi onay kademesi anlamsızlaşırdı.
    /// </summary>
    [Theory]
    [InlineData("reject")]
    [InlineData("iade")]
    public async Task Decisions_RequireApprovalPermission(string action)
    {
        var context = await CreateContextAsync();
        var admin = await ClientAsync();
        var id = await SubmittedRequestAsync(admin, context);

        var client = await CreateClientForRoleAsync("Şantiye Şefi");

        var response = await client.PostAsJsonAsync(
            $"/api/purchase-requests/{id}/{action}",
            new { reason = "Olmaz" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Düzeltmede kalem SAYISI azalabilir. Fazla kalemler kaldırılır
    /// ama silmeler yumuşak olduğu için satır numaraları tabloda
    /// kalır; tekil indeks IsDeleted=false ile filtreli olmasaydı
    /// talep bir daha büyüyemezdi.
    /// </summary>
    [Fact]
    public async Task Return_ThenShrinkAndGrowItems_Works()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var created = await client.PostAsJsonAsync("/api/purchase-requests", new
        {
            companyId = context.CompanyId,
            projectId = context.ProjectId,
            requestDate = DateTime.UtcNow.Date,
            requestedByName = "Şantiye Şefi",
            description = "Üç kalemli talep",
            priority = 1,
            items = new[]
            {
                new { materialDescription = "A", quantity = 1m, unit = "adet",
                      requestedDeliveryDate = (DateTime?)null, notes = (string?)null },
                new { materialDescription = "B", quantity = 2m, unit = "adet",
                      requestedDeliveryDate = (DateTime?)null, notes = (string?)null },
                new { materialDescription = "C", quantity = 3m, unit = "adet",
                      requestedDeliveryDate = (DateTime?)null, notes = (string?)null }
            }
        });

        var id = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        await client.PostAsJsonAsync($"/api/purchase-requests/{id}/submit", new { });
        await client.PostAsJsonAsync(
            $"/api/purchase-requests/{id}/iade", new { reason = "Fazla kalem var" });

        // Üçten bire in.
        var shrink = await client.PutAsJsonAsync(
            $"/api/purchase-requests/{id}",
            new
            {
                requestDate = DateTime.UtcNow.Date,
                neededByDate = (DateTime?)null,
                requestedByName = "Şantiye Şefi",
                description = "Tek kaleme indirildi",
                priority = 1,
                items = new[]
                {
                    new { materialDescription = "A", quantity = 1m, unit = "adet",
                          requestedDeliveryDate = (DateTime?)null, notes = (string?)null }
                }
            });

        Assert.Equal(HttpStatusCode.OK, shrink.StatusCode);

        // Sonra tekrar ikiye çık — bırakılan satır numarası yeniden
        // kullanılabilmeli.
        var grow = await client.PutAsJsonAsync(
            $"/api/purchase-requests/{id}",
            new
            {
                requestDate = DateTime.UtcNow.Date,
                neededByDate = (DateTime?)null,
                requestedByName = "Şantiye Şefi",
                description = "İki kaleme çıkarıldı",
                priority = 1,
                items = new[]
                {
                    new { materialDescription = "A", quantity = 1m, unit = "adet",
                          requestedDeliveryDate = (DateTime?)null, notes = (string?)null },
                    new { materialDescription = "D", quantity = 4m, unit = "adet",
                          requestedDeliveryDate = (DateTime?)null, notes = (string?)null }
                }
            });

        Assert.Equal(HttpStatusCode.OK, grow.StatusCode);

        var detail = await client.GetFromJsonAsync<JsonElement>(
            $"/api/purchase-requests/{id}");

        var items = detail.GetProperty("items").EnumerateArray().ToList();

        Assert.Equal(2, items.Count);
        Assert.Equal(1, items[0].GetProperty("lineNumber").GetInt32());
        Assert.Equal(2, items[1].GetProperty("lineNumber").GetInt32());
        Assert.Equal("D", items[1].GetProperty("materialDescription").GetString());
    }

    private async Task<HttpClient> CreateClientForRoleAsync(string roleName)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider
            .GetRequiredService<EnderunAI.Api.Security.PasswordService>();

        const string password = "PurchaseReject!2026";
        var username = $"test-preject-{Guid.NewGuid():N}"[..40];
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
}
