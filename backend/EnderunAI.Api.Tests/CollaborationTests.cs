using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Collaboration;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// YORUM VE EK DOSYA — KAPSAM, DÜZENLEME PENCERESİ, GİZLEME.
///
/// Yorum bileşeni `(varlık tipi + kayıt no)` ile her ekrana takılıyor;
/// bu genel bir kapı. Kapsam kontrolü olmadan bırakılsaydı kullanıcı
/// göremediği bir çeke yorum yazabilir, göremediği hakedişin
/// tartışmasını okuyabilirdi.
/// </summary>
[Collection("Integration")]
public sealed class CollaborationTests(DatabaseFixture fixture)
{
    private const string ParaRolu = "Finans Sorumlusu";

    private static async Task<Project> ProjeAsync(AppDbContext db, string suffix) =>
        await TestDataFactory.CreateProjectAsync(db, $"COL{suffix}");

    // ---------------------------------------------------------------
    // KAPSAM
    // ---------------------------------------------------------------

    /// <summary>
    /// A şirketinin kullanıcısı, B şirketinin kaydına yorum YAZAMAZ.
    /// Yazabilseydi yorum, kapsam disiplinine açılmış genel bir kapı
    /// olurdu.
    /// </summary>
    [Fact]
    public async Task BaskaSirketinKaydina_YorumYazilamaz()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var a = await ProjeAsync(db, $"A{suffix}");
        var b = await ProjeAsync(db, $"B{suffix}");

        var client = await TestUserFactory.CreateCompanyScopedClientAsync(
            fixture, "isbirligi-a", [ParaRolu], a.CompanyId);

        // Kendi projesi: yazabilmeli.
        var kendi = await client.PostAsJsonAsync("/api/collaboration/comments", new
        {
            entityType = "Project",
            entityId = a.Id,
            body = "Kendi projeme yorum"
        });

        Assert.Equal(HttpStatusCode.OK, kendi.StatusCode);

        // B'nin projesi: YAZAMAMALI.
        var yabanci = await client.PostAsJsonAsync("/api/collaboration/comments", new
        {
            entityType = "Project",
            entityId = b.Id,
            body = "Başkasının projesine yorum"
        });

        Assert.Equal(HttpStatusCode.NotFound, yabanci.StatusCode);

        // VERİ SIZMIYOR: yorum gerçekten yazılmamış.
        var sayi = await db.TaskComments
            .CountAsync(x => x.EntityType == "Project" && x.EntityId == b.Id);

        Assert.Equal(0, sayi);
    }

    [Fact]
    public async Task BaskaSirketinKaydinin_YorumlariOkunamaz()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var a = await ProjeAsync(db, $"OA{suffix}");
        var b = await ProjeAsync(db, $"OB{suffix}");

        db.TaskComments.Add(new TaskComment
        {
            CompanyId = b.CompanyId,
            EntityType = "Project",
            EntityId = b.Id,
            Body = "Gizli kalması gereken yorum"
        });

        await db.SaveChangesAsync();

        var client = await TestUserFactory.CreateCompanyScopedClientAsync(
            fixture, "isbirligi-b", [ParaRolu], a.CompanyId);

        var yanit = await client.GetAsync(
            $"/api/collaboration/comments?entityType=Project&entityId={b.Id}");

        Assert.Equal(HttpStatusCode.NotFound, yanit.StatusCode);

        var govde = await yanit.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Gizli kalması gereken yorum", govde);
    }

    /// <summary>
    /// DESTEKLENMEYEN VARLIK TİPİ SESSİZCE GEÇMEZ. Geçseydi yeni bir
    /// modül yorum bileşenini takar ve KAPSAMSIZ çalışırdı — üstelik
    /// çalışıyor göründüğü için kimse fark etmezdi.
    /// </summary>
    [Fact]
    public async Task DesteklenmeyenVarlikTipi_Reddedilir()
    {
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.PostAsJsonAsync("/api/collaboration/comments", new
        {
            entityType = "UydurmaVarlik",
            entityId = Guid.NewGuid(),
            body = "Bu yazılmamalı"
        });

        Assert.Equal(HttpStatusCode.NotFound, yanit.StatusCode);
    }

    // ---------------------------------------------------------------
    // DÜZENLEME PENCERESİ
    // ---------------------------------------------------------------

    [Fact]
    public async Task Yorum_IlkOnBesDakikada_Duzenlenebilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proje = await ProjeAsync(db, suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var olustur = await client.PostAsJsonAsync("/api/collaboration/comments", new
        {
            entityType = "Project",
            entityId = proje.Id,
            body = "İlk hali"
        });

        var yorumId = (await olustur.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var duzenle = await client.PutAsJsonAsync(
            $"/api/collaboration/comments/{yorumId}",
            new { body = "Düzeltilmiş hali" });

        Assert.Equal(HttpStatusCode.OK, duzenle.StatusCode);

        var guncel = await db.TaskComments.AsNoTracking()
            .SingleAsync(x => x.Id == yorumId);

        Assert.Equal("Düzeltilmiş hali", guncel.Body);
        Assert.Equal(1, guncel.EditCount);
        Assert.NotNull(guncel.EditedAtUtc);
    }

    /// <summary>
    /// PENCERE KAPANDIKTAN SONRA DÜZENLENEMEZ: birinin cevap verdiği
    /// cümle sonradan başka bir cümleye dönüşmemeli.
    /// </summary>
    [Fact]
    public async Task Yorum_OnBesDakikaSonra_Duzenlenemez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proje = await ProjeAsync(db, suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var olustur = await client.PostAsJsonAsync("/api/collaboration/comments", new
        {
            entityType = "Project",
            entityId = proje.Id,
            body = "Eski yorum"
        });

        var yorumId = (await olustur.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        /*
         * YORUMU 20 DAKİKA GERİYE ALIYORUZ — DOĞRUDAN UPDATE İLE.
         *
         * `SaveChanges` işe yaramıyor: AuditSaveChangesInterceptor,
         * Modified durumunda `CreatedAtUtc`'yi bilerek
         * "değiştirilmemiş" işaretliyor (oluşturma zamanı sonradan
         * değişmemeli — doğru davranış). Test bunu ilk yazışımda
         * fark etmedim ve pencere testi hiçbir şey ölçmüyordu.
         */
        await db.TaskComments
            .Where(x => x.Id == yorumId)
            .ExecuteUpdateAsync(s => s.SetProperty(
                x => x.CreatedAtUtc, DateTime.UtcNow.AddMinutes(-20)));

        var duzenle = await client.PutAsJsonAsync(
            $"/api/collaboration/comments/{yorumId}",
            new { body = "Geç kalan düzeltme" });

        Assert.Equal(HttpStatusCode.BadRequest, duzenle.StatusCode);

        var guncel = await db.TaskComments.AsNoTracking()
            .SingleAsync(x => x.Id == yorumId);

        Assert.Equal("Eski yorum", guncel.Body);
    }

    // ---------------------------------------------------------------
    // GİZLEME — SİLME YOK
    // ---------------------------------------------------------------

    /// <summary>
    /// Gizlenen yorum SATIR OLARAK DURUYOR ama metni dönmüyor.
    /// Silinseydi cevap verilmiş bir cümle konuşmadan çıkar ve kalan
    /// cevaplar anlamsızlaşırdı.
    /// </summary>
    [Fact]
    public async Task Yorum_GizlenirSilinmez_MetniDonmez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proje = await ProjeAsync(db, suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var olustur = await client.PostAsJsonAsync("/api/collaboration/comments", new
        {
            entityType = "Project",
            entityId = proje.Id,
            body = "Gizlenecek metin"
        });

        var yorumId = (await olustur.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        await client.PostAsync($"/api/collaboration/comments/{yorumId}/hide", null);

        // SATIR DURUYOR.
        var kayit = await db.TaskComments.AsNoTracking()
            .SingleAsync(x => x.Id == yorumId);

        Assert.NotNull(kayit.HiddenAtUtc);
        Assert.NotNull(kayit.HiddenByUserId);
        Assert.Equal("Gizlenecek metin", kayit.Body);

        // AMA METNİ DÖNMÜYOR.
        var liste = await client.GetFromJsonAsync<JsonElement>(
            $"/api/collaboration/comments?entityType=Project&entityId={proje.Id}");

        var govde = liste.GetRawText();

        Assert.DoesNotContain("Gizlenecek metin", govde);
        Assert.Contains("\"isHidden\":true", govde);
    }

    // ---------------------------------------------------------------
    // KEYSET
    // ---------------------------------------------------------------

    [Fact]
    public async Task Yorumlar_KeysetIleSayfalanir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proje = await ProjeAsync(db, suffix);

        for (var i = 0; i < 5; i++)
        {
            db.TaskComments.Add(new TaskComment
            {
                CompanyId = proje.CompanyId,
                EntityType = "Project",
                EntityId = proje.Id,
                Body = $"Yorum {i}",
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-i)
            });
        }

        await db.SaveChangesAsync();

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var ilk = await client.GetFromJsonAsync<JsonElement>(
            $"/api/collaboration/comments?entityType=Project&entityId={proje.Id}&pageSize=2");

        var ilkKimlikler = ilk.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ToList();

        Assert.Equal(2, ilkKimlikler.Count);
        Assert.True(ilk.GetProperty("hasMore").GetBoolean());

        var imlec = ilk.GetProperty("nextCursor");

        var ikinci = await client.GetFromJsonAsync<JsonElement>(
            $"/api/collaboration/comments?entityType=Project&entityId={proje.Id}&pageSize=2" +
            $"&cursorCreatedAtUtc={imlec.GetProperty("createdAtUtc").GetDateTime():O}" +
            $"&cursorId={imlec.GetProperty("id").GetGuid()}");

        var ikinciKimlikler = ikinci.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ToList();

        Assert.NotEmpty(ikinciKimlikler);
        Assert.Empty(ilkKimlikler.Intersect(ikinciKimlikler));
    }

    // ---------------------------------------------------------------
    // EK DOSYA — İNDİRME AYRI KAPI
    // ---------------------------------------------------------------

    /// <summary>
    /// EK İNDİRME DE KAPSAMDAN GEÇER.
    ///
    /// Yorum ucunu kapsamlayıp indirmeyi unutmak, sızıntıyı ekrandan
    /// DOSYAYA taşırdı — G3/1b'de dışa aktarım uçlarında tam olarak
    /// bu yaşandı ve ayrı test yazma kuralı oradan geldi.
    /// </summary>
    [Fact]
    public async Task EkIndirme_BaskaSirketinEkiniVermez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var a = await ProjeAsync(db, $"EA{suffix}");
        var b = await ProjeAsync(db, $"EB{suffix}");

        /*
         * GERÇEK DOSYA YÜKLENİYOR — YALNIZ DB KAYDI YETMEZ.
         *
         * İlk sürümde diske dosya koymadan sadece `Attachment` satırı
         * açıyordum. Sonda bunu yakaladı: kapsam kontrolünü
         * kaldırdığımda test YİNE geçti, çünkü uç "dosya bulunamadı"
         * diyordu — yani test kapsamı değil, dosyanın yokluğunu
         * ölçüyordu.
         */
        var bClient = await TestUserFactory.CreateCompanyScopedClientAsync(
            fixture, "isbirligi-ek-b", [ParaRolu], b.CompanyId);

        using var icerik = new MultipartFormDataContent();
        var dosya = new ByteArrayContent("gizli sözleşme içeriği"u8.ToArray());
        dosya.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");

        icerik.Add(dosya, "file", "gizli-sozlesme.pdf");
        icerik.Add(new StringContent("Project"), "entityType");
        icerik.Add(new StringContent(b.Id.ToString()), "entityId");

        var yukle = await bClient.PostAsync("/api/collaboration/attachments", icerik);

        Assert.Equal(HttpStatusCode.OK, yukle.StatusCode);

        var ekId = (await yukle.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        // B kendi ekini indirebiliyor — koruma meşru erişimi kapatmamalı.
        var bIndir = await bClient.GetAsync(
            $"/api/collaboration/attachments/{ekId}/download");

        Assert.Equal(HttpStatusCode.OK, bIndir.StatusCode);

        // A İNDİREMEZ.
        var aClient = await TestUserFactory.CreateCompanyScopedClientAsync(
            fixture, "isbirligi-ek-a", [ParaRolu], a.CompanyId);

        var aIndir = await aClient.GetAsync(
            $"/api/collaboration/attachments/{ekId}/download");

        Assert.Equal(HttpStatusCode.NotFound, aIndir.StatusCode);

        // DOSYA İÇERİĞİ SIZMIYOR.
        var govde = await aIndir.Content.ReadAsStringAsync();

        Assert.DoesNotContain("gizli sözleşme içeriği", govde);
        Assert.DoesNotContain("gizli-sozlesme.pdf", govde);
    }

    /// <summary>
    /// HEIC TARAYICIDA GÖSTERİLEMEZ — ekran bunu bilmeli.
    ///
    /// Yükleme kabul ediliyor (iPhone varsayılanı) ama Chrome ve
    /// Firefox HEIC'i açamıyor. `isBrowserViewable=false` sayesinde
    /// ekran "indirin" diyor, bozuk resim simgesi göstermiyor.
    /// </summary>
    [Fact]
    public async Task HeicEki_TarayicidaGoruntulenemezIsaretlenir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proje = await ProjeAsync(db, suffix);

        db.Attachments.AddRange(
            new Attachment
            {
                CompanyId = proje.CompanyId,
                EntityType = "Project",
                EntityId = proje.Id,
                Category = "collaboration",
                StoredName = $"TEST-{Guid.NewGuid():N}.heic",
                OriginalName = "saha-fotografi.heic",
                ContentType = "image/heic",
                SizeBytes = 2048
            },
            new Attachment
            {
                CompanyId = proje.CompanyId,
                EntityType = "Project",
                EntityId = proje.Id,
                Category = "collaboration",
                StoredName = $"TEST-{Guid.NewGuid():N}.jpg",
                OriginalName = "saha-fotografi.jpg",
                ContentType = "image/jpeg",
                SizeBytes = 2048
            });

        await db.SaveChangesAsync();

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var liste = await client.GetFromJsonAsync<JsonElement>(
            $"/api/collaboration/attachments?entityType=Project&entityId={proje.Id}");

        var ekler = liste.EnumerateArray().ToList();

        var heic = ekler.Single(x =>
            x.GetProperty("originalName").GetString() == "saha-fotografi.heic");

        var jpeg = ekler.Single(x =>
            x.GetProperty("originalName").GetString() == "saha-fotografi.jpg");

        Assert.False(heic.GetProperty("isBrowserViewable").GetBoolean());
        Assert.True(jpeg.GetProperty("isBrowserViewable").GetBoolean());
    }
}
