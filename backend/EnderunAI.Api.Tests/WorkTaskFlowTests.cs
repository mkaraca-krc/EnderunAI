using System.Net;
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
/// GÖREV AKIŞI — ÇİFT ADIMLI KAPANIŞ, İADE, DEVRETME, YETKİ.
///
/// TEMEL FİKİR: görev, atanmış ve terminli bir yorumdur. Akış:
///   Açık -> Devam ediyor -> Tamamlandı (yapan) -> Onaylandı (gönderen)
///                                              -> İade (gönderen) -> Açık
/// </summary>
[Collection("Integration")]
public sealed class WorkTaskFlowTests(DatabaseFixture fixture)
{
    private const string YoneticiRol = "Genel Müdür";

    private sealed record Sahne(Project Proje, Guid GorevId, string TaskNumber);

    private static async Task<Sahne> GorevAcAsync(
        DatabaseFixture fixture,
        AppDbContext db,
        string suffix,
        Guid? atanan,
        Guid? gonderen,
        DateTime? termin = null)
    {
        var proje = await TestDataFactory.CreateProjectAsync(db, $"GRV{suffix}");

        var gorev = new WorkTask
        {
            CompanyId = proje.CompanyId,
            ProjectId = proje.Id,
            TaskNumber = $"TEST-GRV-{suffix}",
            Title = "Akış testi görevi",
            Status = WorkTaskStatus.Completed,
            AssignedToUserId = atanan,
            AssignedByUserId = gonderen,
            DueDate = termin
        };

        db.WorkTasks.Add(gorev);
        await db.SaveChangesAsync();

        return new Sahne(proje, gorev.Id, gorev.TaskNumber);
    }

    // ---------------------------------------------------------------
    // ÇİFT ADIMLI KAPANIŞ
    // ---------------------------------------------------------------

    /// <summary>
    /// Yapanın "bitti" demesi görevi KAPATMAZ: gönderenin onayı
    /// bekleniyor. Tek adımlı kapanışta gönderen, istediği işin
    /// yapılıp yapılmadığını hiç görmeden görevin listeden düştüğünü
    /// görürdü.
    /// </summary>
    [Fact]
    public async Task Tamamlandi_GoreviKapatmaz_OnayBekler()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var gonderen = Guid.NewGuid();
        var sahne = await GorevAcAsync(fixture, db, suffix, atanan: Guid.NewGuid(), gonderen: gonderen);

        // Görev Open'a çekiliyor ki complete edilebilsin.
        var kayit = await db.WorkTasks.SingleAsync(x => x.Id == sahne.GorevId);
        kayit.Status = WorkTaskStatus.Open;
        await db.SaveChangesAsync();

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.PostAsJsonAsync(
            $"/api/tasks/{sahne.GorevId}/complete",
            new { completionNote = "Bitti" });

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);

        var guncel = await db.WorkTasks.AsNoTracking()
            .SingleAsync(x => x.Id == sahne.GorevId);

        // KAPANMADI: onay bekliyor.
        Assert.Equal(WorkTaskStatus.Completed, guncel.Status);
        Assert.Null(guncel.ApprovedAtUtc);
        Assert.NotNull(guncel.CompletedAtUtc);
    }

    /// <summary>
    /// GÖNDEREN KENDİNE AÇTIYSA TEK ADIM: kendini onaylatmak anlamsız
    /// bir tören olurdu ve gelen kutusunu kendi onaylarıyla doldururdu.
    /// </summary>
    [Fact]
    public async Task KendineAcilanGorev_TekAdimdaKapanir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var kisi = Guid.NewGuid();
        var sahne = await GorevAcAsync(fixture, db, suffix, atanan: kisi, gonderen: kisi);

        var kayit = await db.WorkTasks.SingleAsync(x => x.Id == sahne.GorevId);
        kayit.Status = WorkTaskStatus.Open;
        await db.SaveChangesAsync();

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await client.PostAsJsonAsync(
            $"/api/tasks/{sahne.GorevId}/complete",
            new { completionNote = "Kendi işim" });

        var guncel = await db.WorkTasks.AsNoTracking()
            .SingleAsync(x => x.Id == sahne.GorevId);

        // TEK ADIMDA KAPANDI.
        Assert.Equal(WorkTaskStatus.Approved, guncel.Status);
        Assert.NotNull(guncel.ApprovedAtUtc);
    }

    // ---------------------------------------------------------------
    // İADE — TERMİN KORUNUR
    // ---------------------------------------------------------------

    /// <summary>
    /// İADE TERMİNİ KORUR. Gönderen yeni termin vermezse ESKİSİ kalır;
    /// termini geçmiş iade görevi listede hemen kırmızı görünür —
    /// gecikme iade ile gizlenmemeli.
    /// </summary>
    [Fact]
    public async Task Iade_TerminiKorur_VeGecikmeGizlenmez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // TERMİNİ GEÇMİŞ görev.
        var gecmisTermin = DateTime.UtcNow.AddDays(-5);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var benKimim = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var benimId = benKimim.GetProperty("id").GetGuid();

        var sahne = await GorevAcAsync(
            fixture, db, suffix,
            atanan: Guid.NewGuid(),
            gonderen: benimId,
            termin: gecmisTermin);

        var yanit = await client.PostAsJsonAsync(
            $"/api/tasks/{sahne.GorevId}/return",
            new { reason = "Eksik kalmış." });

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);

        var guncel = await db.WorkTasks.AsNoTracking()
            .SingleAsync(x => x.Id == sahne.GorevId);

        // GÖREV YAPANA GERİ DÖNDÜ.
        Assert.Equal(WorkTaskStatus.Open, guncel.Status);
        Assert.Equal(1, guncel.ReturnCount);
        Assert.Equal("Eksik kalmış.", guncel.ReturnReason);

        // TERMİN AYNEN DURUYOR — ileri atılmadı.
        Assert.Equal(
            gecmisTermin.ToString("yyyy-MM-dd HH:mm"),
            guncel.DueDate!.Value.ToString("yyyy-MM-dd HH:mm"));

        // TAMAMLANMA İZİ SİLİNDİ: görev yeniden açık.
        Assert.Null(guncel.CompletedAtUtc);

        // GECİKME GÖRÜNÜYOR: uç `isOverdue` diyor.
        var govde = await yanit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(govde.GetProperty("isOverdue").GetBoolean());
    }

    /// <summary>
    /// Gönderen isterse YENİ TERMİN verebilir.
    /// </summary>
    [Fact]
    public async Task Iade_YeniTerminVerilebilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var benKimim = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var benimId = benKimim.GetProperty("id").GetGuid();

        var sahne = await GorevAcAsync(
            fixture, db, suffix,
            atanan: Guid.NewGuid(),
            gonderen: benimId,
            termin: DateTime.UtcNow.AddDays(-5));

        var yeniTermin = DateTime.UtcNow.AddDays(7).Date;

        await client.PostAsJsonAsync(
            $"/api/tasks/{sahne.GorevId}/return",
            new { reason = "Süre uzatıldı.", newDueDate = yeniTermin });

        var guncel = await db.WorkTasks.AsNoTracking()
            .SingleAsync(x => x.Id == sahne.GorevId);

        Assert.Equal(yeniTermin.Date, guncel.DueDate!.Value.Date);
    }

    /// <summary>
    /// GEREKÇESİZ İADE REDDEDİLİR: gerekçesiz iade sessiz bir
    /// "beğenmedim"dir, yapan neyi düzelteceğini bilemez.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GerekcesizIade_Reddedilir(string gerekce)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var benKimim = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var benimId = benKimim.GetProperty("id").GetGuid();

        var sahne = await GorevAcAsync(
            fixture, db, suffix, atanan: Guid.NewGuid(), gonderen: benimId);

        var yanit = await client.PostAsJsonAsync(
            $"/api/tasks/{sahne.GorevId}/return",
            new { reason = gerekce });

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);

        var guncel = await db.WorkTasks.AsNoTracking()
            .SingleAsync(x => x.Id == sahne.GorevId);

        // DURUM DEĞİŞMEDİ.
        Assert.Equal(WorkTaskStatus.Completed, guncel.Status);
        Assert.Equal(0, guncel.ReturnCount);
    }

    // ---------------------------------------------------------------
    // ONAY YALNIZ GÖNDERENE AİT
    // ---------------------------------------------------------------

    /// <summary>
    /// Başkası onaylayabilseydi çift adımlı kapanış tören olurdu: işi
    /// isteyen kişi sonucu görmeden görev kapanırdı.
    /// </summary>
    [Fact]
    public async Task Onay_YalnizGonderenTarafindanYapilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Gönderen BAŞKASI.
        var sahne = await GorevAcAsync(
            fixture, db, suffix, atanan: Guid.NewGuid(), gonderen: Guid.NewGuid());

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.PostAsync($"/api/tasks/{sahne.GorevId}/approve", null);

        Assert.Equal(HttpStatusCode.Forbidden, yanit.StatusCode);

        var guncel = await db.WorkTasks.AsNoTracking()
            .SingleAsync(x => x.Id == sahne.GorevId);

        Assert.Equal(WorkTaskStatus.Completed, guncel.Status);
    }

    // ---------------------------------------------------------------
    // KEYSET SAYFALAMA
    // ---------------------------------------------------------------

    /// <summary>
    /// Sayfalama keyset: `nextCursor` ile ilerliyor ve ikinci sayfa
    /// birinciyle ÇAKIŞMIYOR. OFFSET kullanılsaydı araya yeni görev
    /// eklendiğinde kayıtlar sayfalar arasında kayardı.
    /// </summary>
    [Fact]
    public async Task Liste_KeysetIleSayfalanir_SayfalarCakismaz()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proje = await TestDataFactory.CreateProjectAsync(db, $"SYF{suffix}");

        for (var i = 0; i < 5; i++)
        {
            db.WorkTasks.Add(new WorkTask
            {
                CompanyId = proje.CompanyId,
                ProjectId = proje.Id,
                TaskNumber = $"TEST-SYF-{suffix}-{i}",
                Title = $"Sayfa testi {i}",
                Status = WorkTaskStatus.Open,
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-i)
            });
        }

        await db.SaveChangesAsync();

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var ilk = await client.GetFromJsonAsync<JsonElement>(
            $"/api/tasks?projectId={proje.Id}&pageSize=2");

        var ilkKimlikler = ilk.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ToList();

        Assert.Equal(2, ilkKimlikler.Count);
        Assert.True(ilk.GetProperty("hasMore").GetBoolean());

        var imlec = ilk.GetProperty("nextCursor");

        var ikinci = await client.GetFromJsonAsync<JsonElement>(
            $"/api/tasks?projectId={proje.Id}&pageSize=2" +
            $"&cursorCreatedAtUtc={imlec.GetProperty("createdAtUtc").GetDateTime():O}" +
            $"&cursorId={imlec.GetProperty("id").GetGuid()}");

        var ikinciKimlikler = ikinci.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ToList();

        Assert.NotEmpty(ikinciKimlikler);

        // SAYFALAR ÇAKIŞMIYOR.
        Assert.Empty(ilkKimlikler.Intersect(ikinciKimlikler));
    }
}
