using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Offers;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Teklif fırsat hunisi: durum takibi, kayıp nedeni ve kazanma oranı
/// (T1).
///
/// Asıl güvenceler:
/// - Durum SERBEST atanamaz. Geçiş haritası dışında bir atama hunide
///   "verilmeden kazanılmış" gibi imkânsız satırlar üretirdi.
/// - KAZANILDI / KAYBEDİLDİ / İPTAL nihaidir. Kazanılan teklif sözleşme
///   doğurur, kaybedilen ise arşivin kendisidir; ikisi de sonradan
///   oynanabilseydi hiçbirine güvenilemezdi.
/// - Kayıp NEDENSİZ kaydedilemez; nedeni yazılmayan kayıp ileride
///   sayılamaz.
/// - Kime verildiği bilinmeyen teklif "verildi" olamaz; huninin ve
///   kazanma oranının kırılımı buna dayanıyor.
/// </summary>
[Collection("Integration")]
public sealed class OfferFunnelTests(DatabaseFixture fixture)
{
    private sealed record Context(Guid CompanyId, Guid ProjectId, Guid AccountId);

    private async Task<Context> CreateContextAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var employer = await db.Projects
            .Where(x => x.Id == project.Id)
            .Select(x => x.EmployerCurrentAccountId!.Value)
            .SingleAsync();

        return new Context(project.CompanyId, project.Id, employer);
    }

    private async Task<HttpClient> ClientAsync() =>
        await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    private static object OfferPayload(
        Guid companyId, Guid projectId, decimal listPrice = 100m) => new
        {
            companyId,
            projectId,
            title = "Huni testi teklifi",
            offerDate = new DateTime(2026, 5, 1),
            currency = "TRY",
            exchangeRate = 1m,
            items = new[]
            {
                new
                {
                    description = "Kablo çekimi",
                    quantity = 10m,
                    unit = "MTR",
                    listPrice,
                    discountRate = 0m,
                    freightRate = 0m,
                    wasteRate = 0m,
                    financeRate = 0m,
                    generalExpenseRate = 0m,
                    profitRate = 0m
                }
            }
        };

    private async Task<Guid> CreateOfferAsync(
        HttpClient client, Context context, decimal listPrice = 100m)
    {
        var response = await client.PostAsJsonAsync(
            "/api/offers", OfferPayload(context.CompanyId, context.ProjectId, listPrice));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> SetTrackingAsync(
        HttpClient client,
        Guid offerId,
        Guid? accountId,
        OfferCounterpartyRole role = OfferCounterpartyRole.Employer,
        OfferKind kind = OfferKind.UnitPrice) =>
        await client.PutAsJsonAsync(
            $"/api/offers/{offerId}/takip",
            new
            {
                counterpartyCurrentAccountId = accountId,
                counterpartyRole = (int)role,
                kind = (int)kind
            });

    private static async Task<HttpResponseMessage> ChangeStatusAsync(
        HttpClient client,
        Guid offerId,
        OfferStatus status,
        OfferLostReason lostReason = OfferLostReason.None,
        string? note = null) =>
        await client.PostAsJsonAsync(
            $"/api/offers/{offerId}/durum",
            new
            {
                status = (int)status,
                lostReason = (int)lostReason,
                lostReasonNote = (string?)null,
                note
            });

    /// <summary>Teklifi hazır ve "verildi" durumuna getirir.</summary>
    private async Task<Guid> SubmittedOfferAsync(
        HttpClient client, Context context, decimal listPrice = 100m)
    {
        var offerId = await CreateOfferAsync(client, context, listPrice);

        Assert.Equal(
            HttpStatusCode.OK,
            (await SetTrackingAsync(client, offerId, context.AccountId)).StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await ChangeStatusAsync(client, offerId, OfferStatus.Submitted)).StatusCode);

        return offerId;
    }

    // ---------- Saf geçiş kuralları (veritabanısız) ----------

    /// <summary>
    /// Huninin ileri yönü serbest, geri dönüşü kapalıdır.
    /// </summary>
    [Theory]
    [InlineData(OfferStatus.Draft, OfferStatus.Submitted, true)]
    [InlineData(OfferStatus.Draft, OfferStatus.Cancelled, true)]
    [InlineData(OfferStatus.Draft, OfferStatus.Won, false)]
    [InlineData(OfferStatus.Draft, OfferStatus.Lost, false)]
    [InlineData(OfferStatus.Submitted, OfferStatus.Pending, true)]
    [InlineData(OfferStatus.Submitted, OfferStatus.Won, true)]
    [InlineData(OfferStatus.Submitted, OfferStatus.Draft, false)]
    [InlineData(OfferStatus.Pending, OfferStatus.Lost, true)]
    [InlineData(OfferStatus.Pending, OfferStatus.Submitted, false)]
    [InlineData(OfferStatus.Won, OfferStatus.Lost, false)]
    [InlineData(OfferStatus.Won, OfferStatus.Cancelled, false)]
    [InlineData(OfferStatus.Lost, OfferStatus.Pending, false)]
    [InlineData(OfferStatus.Cancelled, OfferStatus.Draft, false)]
    public void Transitions_FollowTheFunnel(
        OfferStatus from, OfferStatus to, bool allowed)
    {
        Assert.Equal(allowed, OfferStatusTransitions.CanTransition(from, to));
    }

    /// <summary>
    /// Kazanıldı, kaybedildi ve iptal son duraktır.
    /// </summary>
    [Fact]
    public void FinalStates_HaveNoExit()
    {
        foreach (var status in new[]
                 {
                     OfferStatus.Won, OfferStatus.Lost, OfferStatus.Cancelled
                 })
        {
            Assert.True(OfferStatusTransitions.IsFinal(status));
            Assert.Empty(OfferStatusTransitions.Allowed[status]);
        }
    }

    /// <summary>
    /// Kullanımdan kalkan Reddedildi durumuna geçiş, kullanıcıyı
    /// Kaybedildi'ye yönlendiren bir mesajla reddedilir.
    /// </summary>
    [Fact]
    public void RejectedStatus_IsRetiredWithGuidance()
    {
        var problem = OfferStatusTransitions.Validate(
            OfferStatus.Submitted, OfferStatus.Rejected,
            hasCounterparty: true, OfferLostReason.None, itemCount: 1);

        Assert.NotNull(problem);
        Assert.Contains("Kaybedildi", problem);
    }

    // ---------- Kazanma oranı hesabı (veritabanısız) ----------

    /// <summary>
    /// Oranın paydası kazanılan + kaybedilendir. Sonucu belli olmamış
    /// teklifi paydaya koymak oranı yapay olarak düşürür (henüz
    /// kaybetmedik); iptali koymak ise bizim performansımız olmayan
    /// bir şeyi bize yazar.
    /// </summary>
    [Fact]
    public void WinRate_ExcludesOpenAndCancelledFromDenominator()
    {
        var summary = OfferWinRateCalculator.Calculate(
        [
            (OfferStatus.Won, 100_000m),
            (OfferStatus.Lost, 300_000m),
            (OfferStatus.Pending, 500_000m),
            (OfferStatus.Draft, 200_000m),
            (OfferStatus.Cancelled, 900_000m)
        ]);

        Assert.Equal(5, summary.TotalCount);
        Assert.Equal(1, summary.WonCount);
        Assert.Equal(1, summary.LostCount);
        Assert.Equal(2, summary.OpenCount);
        Assert.Equal(1, summary.CancelledCount);

        // Adet: 1 / (1+1) = %50
        Assert.Equal(50m, summary.CountWinRate);

        // Tutar: 100.000 / (100.000+300.000) = %25
        Assert.Equal(25m, summary.AmountWinRate);

        // Açık huninin değeri iptali içermez.
        Assert.Equal(700_000m, summary.OpenAmount);
    }

    /// <summary>
    /// Adet oranı iyi, tutar oranı kötü olabilir: küçük işleri kazanıp
    /// büyük işi kaybeden dönem. İki oranın ayrı verilmesinin sebebi bu.
    /// </summary>
    [Fact]
    public void WinRate_CountAndAmountCanDisagree()
    {
        var summary = OfferWinRateCalculator.Calculate(
        [
            (OfferStatus.Won, 10_000m),
            (OfferStatus.Won, 10_000m),
            (OfferStatus.Won, 10_000m),
            (OfferStatus.Lost, 1_000_000m)
        ]);

        Assert.Equal(75m, summary.CountWinRate);
        Assert.True(summary.AmountWinRate < 3m);
    }

    /// <summary>Sonuçlanmış teklif yoksa oran sıfırdır, hata değil.</summary>
    [Fact]
    public void WinRate_WithoutDecidedOffers_IsZero()
    {
        var summary = OfferWinRateCalculator.Calculate(
            [(OfferStatus.Draft, 50_000m)]);

        Assert.Equal(0m, summary.CountWinRate);
        Assert.Equal(0m, summary.AmountWinRate);
    }

    // ---------- Uçlar ----------

    /// <summary>
    /// Yeni teklif Taslak doğar ve durum ucuyla huniden geçer.
    /// </summary>
    [Fact]
    public async Task Status_MovesThroughFunnelAndIsPersisted()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var offerId = await SubmittedOfferAsync(client, context);

        Assert.Equal(
            HttpStatusCode.OK,
            (await ChangeStatusAsync(client, offerId, OfferStatus.Pending)).StatusCode);

        var response = await ChangeStatusAsync(
            client, offerId, OfferStatus.Won, note: "İhale bizde kaldı");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)OfferStatus.Won, body.GetProperty("status").GetInt32());

        // Kazanmak tek başına sözleşme açmaz; künye ayrı adım (T2).
        Assert.True(body.GetProperty("requiresContract").GetBoolean());

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var offer = await db.Offers.SingleAsync(x => x.Id == offerId);

        Assert.Equal(OfferStatus.Won, offer.Status);
        Assert.NotNull(offer.StatusChangedAtUtc);
        Assert.NotNull(offer.StatusChangedByUserId);
        Assert.Equal("İhale bizde kaldı", offer.StatusNote);
    }

    /// <summary>
    /// Taslaktan doğrudan Kazanıldı'ya atlanamaz: verilmemiş bir teklif
    /// kazanılamaz.
    /// </summary>
    [Fact]
    public async Task Status_CannotSkipFromDraftToWon()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var offerId = await CreateOfferAsync(client, context);
        await SetTrackingAsync(client, offerId, context.AccountId);

        var response = await ChangeStatusAsync(client, offerId, OfferStatus.Won);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Kime verildiği bilinmeyen teklif "verildi" olamaz; huninin
    /// kırılımı karşı tarafa dayanıyor.
    /// </summary>
    [Fact]
    public async Task Status_RequiresCounterpartyBeforeSubmitting()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var offerId = await CreateOfferAsync(client, context);

        var response = await ChangeStatusAsync(client, offerId, OfferStatus.Submitted);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("kime verildiği", body.GetProperty("message").GetString()!);
    }

    /// <summary>
    /// Kayıp nedensiz kaydedilemez; nedeni olmayan kayıp sayılamaz.
    /// </summary>
    [Fact]
    public async Task Lost_RequiresReason()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var offerId = await SubmittedOfferAsync(client, context);

        var without = await ChangeStatusAsync(client, offerId, OfferStatus.Lost);
        Assert.Equal(HttpStatusCode.BadRequest, without.StatusCode);

        var with = await ChangeStatusAsync(
            client, offerId, OfferStatus.Lost, OfferLostReason.PriceTooHigh);

        Assert.Equal(HttpStatusCode.OK, with.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var offer = await db.Offers.SingleAsync(x => x.Id == offerId);
        Assert.Equal(OfferLostReason.PriceTooHigh, offer.LostReason);
    }

    /// <summary>
    /// Kayıp nedeni yalnız Kaybedildi'ye aittir; kazanılan teklifte
    /// kayıp nedeni taşımak raporu bozardı.
    /// </summary>
    [Fact]
    public async Task LostReason_RejectedOnNonLostStatus()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var offerId = await SubmittedOfferAsync(client, context);

        var response = await ChangeStatusAsync(
            client, offerId, OfferStatus.Won, OfferLostReason.CompetitorWon);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Kaybedilen teklif arşivde kalır ve DEĞİŞTİRİLEMEZ — "geçen sefer
    /// bu işe şu fiyatı vermiştik" referansı ancak sonradan
    /// oynanmamışsa güvenilirdir.
    /// </summary>
    [Fact]
    public async Task Lost_StaysInArchiveAndIsImmutable()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var offerId = await SubmittedOfferAsync(client, context, listPrice: 250m);

        Assert.Equal(
            HttpStatusCode.OK,
            (await ChangeStatusAsync(
                client, offerId, OfferStatus.Lost,
                OfferLostReason.CompetitorWon)).StatusCode);

        // Ne yeniden açılabilir...
        var reopen = await ChangeStatusAsync(client, offerId, OfferStatus.Pending);
        Assert.Equal(HttpStatusCode.BadRequest, reopen.StatusCode);

        // ...ne künyesi değiştirilebilir.
        var retrack = await SetTrackingAsync(
            client, offerId, context.AccountId, OfferCounterpartyRole.MainContractor);

        Assert.Equal(HttpStatusCode.BadRequest, retrack.StatusCode);

        // Kayıt ve fiyatı listede duruyor.
        var listed = await client.GetFromJsonAsync<JsonElement>(
            $"/api/offers?companyId={context.CompanyId}&status={(int)OfferStatus.Lost}");

        var row = listed.EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == offerId);

        Assert.Equal(2500m, row.GetProperty("grandTotal").GetDecimal());
    }

    /// <summary>
    /// Kazanılan teklif de değiştirilemez: projesi ve icmali doğduktan
    /// sonra durumu geri alınabilseydi proje sahipsiz kalırdı.
    /// </summary>
    [Fact]
    public async Task Won_IsImmutable()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var offerId = await SubmittedOfferAsync(client, context);
        await ChangeStatusAsync(client, offerId, OfferStatus.Won);

        var response = await ChangeStatusAsync(client, offerId, OfferStatus.Cancelled);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Cari seçilmeden rol, rol seçilmeden cari kaydedilemez; yarım
    /// künye huniyi "kime verdiğimiz belirsiz" satırlarla doldururdu.
    /// </summary>
    [Fact]
    public async Task Tracking_RequiresAccountAndRoleTogether()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var offerId = await CreateOfferAsync(client, context);

        var roleOnly = await SetTrackingAsync(
            client, offerId, null, OfferCounterpartyRole.Employer);

        Assert.Equal(HttpStatusCode.BadRequest, roleOnly.StatusCode);

        var accountOnly = await SetTrackingAsync(
            client, offerId, context.AccountId, OfferCounterpartyRole.Unspecified);

        Assert.Equal(HttpStatusCode.BadRequest, accountOnly.StatusCode);
    }

    /// <summary>Başka şirketin carisi karşı taraf olamaz.</summary>
    [Fact]
    public async Task Tracking_RejectsForeignCurrentAccount()
    {
        var context = await CreateContextAsync();
        var other = await CreateContextAsync();
        var client = await ClientAsync();

        var offerId = await CreateOfferAsync(client, context);

        var response = await SetTrackingAsync(client, offerId, other.AccountId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Kazanma oranı ucu adet ve tutar oranını, kayıp nedeni dağılımını
    /// birlikte verir.
    /// </summary>
    [Fact]
    public async Task WinRateEndpoint_ReportsRatesAndLostReasons()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        // Kazanılan: 10 x 100 = 1.000
        var won = await SubmittedOfferAsync(client, context, listPrice: 100m);
        await ChangeStatusAsync(client, won, OfferStatus.Won);

        // Kaybedilen: 10 x 300 = 3.000
        var lost = await SubmittedOfferAsync(client, context, listPrice: 300m);
        await ChangeStatusAsync(
            client, lost, OfferStatus.Lost, OfferLostReason.PriceTooHigh);

        // Açık teklif oranı etkilemez.
        await SubmittedOfferAsync(client, context, listPrice: 900m);

        var summary = await client.GetFromJsonAsync<JsonElement>(
            $"/api/offers/kazanma-orani?companyId={context.CompanyId}");

        Assert.Equal(3, summary.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("wonCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("lostCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("openCount").GetInt32());

        Assert.Equal(50m, summary.GetProperty("countWinRate").GetDecimal());
        Assert.Equal(25m, summary.GetProperty("amountWinRate").GetDecimal());
        Assert.Equal(9000m, summary.GetProperty("openAmount").GetDecimal());

        var reasons = summary.GetProperty("lostReasons").EnumerateArray().ToList();
        Assert.Single(reasons);
        Assert.Equal(
            (int)OfferLostReason.PriceTooHigh,
            reasons[0].GetProperty("reason").GetInt32());
        Assert.Equal(3000m, reasons[0].GetProperty("amount").GetDecimal());
    }

    /// <summary>
    /// Kazanma oranı karşı tarafa göre daraltılabilir: işverene verilen
    /// tekliflerle ana yükleniciye verilenlerin rekabet koşulları
    /// farklı, tek bir ortalama ikisini de yanlış anlatır.
    /// </summary>
    [Fact]
    public async Task WinRateEndpoint_FiltersByCounterparty()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var won = await SubmittedOfferAsync(client, context);
        await ChangeStatusAsync(client, won, OfferStatus.Won);

        var summary = await client.GetFromJsonAsync<JsonElement>(
            $"/api/offers/kazanma-orani?companyId={context.CompanyId}" +
            $"&counterpartyId={Guid.NewGuid()}");

        Assert.Equal(0, summary.GetProperty("totalCount").GetInt32());
    }

    // ---------- Yetki ----------

    /// <summary>
    /// Takip katmanı kendi anahtarıyla korunuyor. Saha ve depo rolleri
    /// teklif fiyatlarını ve kazanma oranını göremez.
    /// </summary>
    [Theory]
    [InlineData("Şantiye Şefi")]
    [InlineData("Depo Sorumlusu")]
    [InlineData("Formen")]
    [InlineData("Satın Alma Sorumlusu")]
    public async Task Funnel_IsClosedToUnauthorizedRoles(string roleName)
    {
        var context = await CreateContextAsync();
        var admin = await ClientAsync();
        var offerId = await CreateOfferAsync(admin, context);

        var client = await CreateClientForRoleAsync(roleName);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.GetAsync(
                $"/api/offers/kazanma-orani?companyId={context.CompanyId}")).StatusCode);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await ChangeStatusAsync(client, offerId, OfferStatus.Submitted)).StatusCode);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await SetTrackingAsync(client, offerId, context.AccountId)).StatusCode);
    }

    /// <summary>
    /// Finans, teklif HAZIRLAMA yetkisi almadan huniyi görebilmeli:
    /// hangi işe teklif verildiği ve kazanma oranı nakit planlamasını
    /// doğrudan etkiliyor.
    /// </summary>
    [Fact]
    public async Task Finance_SeesFunnelWithoutOfferAuthoring()
    {
        var context = await CreateContextAsync();
        var admin = await ClientAsync();
        var offerId = await CreateOfferAsync(admin, context);

        var client = await CreateClientForRoleAsync("Finans Sorumlusu");

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync(
                $"/api/offers/kazanma-orani?companyId={context.CompanyId}")).StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await SetTrackingAsync(client, offerId, context.AccountId)).StatusCode);

        // Ama teklif kalemi giremez.
        var authoring = await client.PostAsJsonAsync(
            "/api/offers", OfferPayload(context.CompanyId, context.ProjectId));

        Assert.Equal(HttpStatusCode.Forbidden, authoring.StatusCode);
    }

    /// <summary>
    /// Teknik Ofis teklifi hazırlayan taraf; hazırladığı işin akıbetini
    /// de görmeli ve işaretleyebilmeli.
    /// </summary>
    [Fact]
    public async Task TechnicalOffice_CanTrackOffersItAuthors()
    {
        var context = await CreateContextAsync();
        var client = await CreateClientForRoleAsync("Teknik Ofis");

        var created = await client.PostAsJsonAsync(
            "/api/offers", OfferPayload(context.CompanyId, context.ProjectId));

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var offerId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        Assert.Equal(
            HttpStatusCode.OK,
            (await SetTrackingAsync(client, offerId, context.AccountId)).StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await ChangeStatusAsync(client, offerId, OfferStatus.Submitted)).StatusCode);
    }

    /// <summary>
    /// Rolün izni olsa bile kullanıcı bazlı reddedilirse huni kapanır —
    /// yetki role değil izne bağlı.
    /// </summary>
    [Fact]
    public async Task Funnel_RespectsUserLevelDeny()
    {
        var context = await CreateContextAsync();

        var client = await CreateClientForRoleAsync(
            "Teknik Koordinatör",
            PermissionCatalog.Keys.OfferTrackingView);

        var response = await client.GetAsync(
            $"/api/offers/kazanma-orani?companyId={context.CompanyId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<HttpClient> CreateClientForRoleAsync(
        string roleName, string? deniedPermissionKey = null)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider
            .GetRequiredService<PasswordService>();

        const string password = "OfferFunnel!2026";
        var username = $"test-funnel-{Guid.NewGuid():N}"[..40];
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

        if (deniedPermissionKey is not null)
        {
            var permission = await db.Permissions
                .SingleAsync(x => x.Key == deniedPermissionKey);

            db.UserPermissionOverrides.Add(new UserPermissionOverride
            {
                UserId = user.Id,
                PermissionId = permission.Id,
                Effect = PermissionOverrideEffect.Deny
            });
        }

        await db.SaveChangesAsync();

        var client = fixture.Factory.CreateClient();
        var token = await AuthHelper.LoginAsync(client, username, password);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }
}
