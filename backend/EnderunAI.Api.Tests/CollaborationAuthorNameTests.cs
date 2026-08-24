using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using EnderunAI.Api.Controllers;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// YORUM VE EK DOSYA, YAZARIN ADIYLA DÖNER.
///
/// NEDEN: uçlar önce yalnız `CreatedByUserId` / `UploadedByUserId`
/// döndürüyordu. Ekranda GUID gösteren bir yorum dizisi, kimin ne
/// dediği okunamadığı için yorum değildir; ekran da adı çözmek için
/// satır başına bir istek atmak zorunda kalırdı.
/// </summary>
[Collection("Integration")]
public sealed class CollaborationAuthorNameTests(DatabaseFixture fixture)
{
    private static async Task<Guid> GorevAsync(AppDbContext db, string suffix)
    {
        var proje = await TestDataFactory.CreateProjectAsync(db, $"YRM{suffix}");

        var gorev = new WorkTask
        {
            CompanyId = proje.CompanyId,
            ProjectId = proje.Id,
            TaskNumber = $"TEST-YRM-{suffix}",
            Title = "Yorum adı testi görevi",
            Status = WorkTaskStatus.Open
        };

        db.WorkTasks.Add(gorev);
        await db.SaveChangesAsync();

        return gorev.Id;
    }

    private static async Task<JsonElement> ListeAsync(
        HttpClient client, Guid gorevId)
    {
        var yanit = await client.GetAsync(
            $"/api/collaboration/comments?entityType=WorkTask&entityId={gorevId}");

        yanit.EnsureSuccessStatusCode();

        return await yanit.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>
    /// YORUM UÇTAN YAZILIYOR, DOĞRUDAN VERİTABANINA DEĞİL.
    ///
    /// İlk denemede yorumu `db.TaskComments.Add(...)` ile kurup
    /// `CreatedByUserId`'yi elle vermiştim; test kırmızı geldi.
    /// Sebep kodda değil kurulumdaydı:
    /// `AuditSaveChangesInterceptor:73` eklemede bu alanı KOŞULSUZ
    /// olarak oturum sahibiyle eziyor. Yani elle verilen yazar
    /// kimliği hiçbir zaman kaydedilmiyor — gerçek yol uçtan
    /// geçmek.
    /// </summary>
    [Fact]
    public async Task YorumListesi_YazarAdiniDonduruyor()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var gorevId = await GorevAsync(db, suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yazma = await client.PostAsJsonAsync(
            "/api/collaboration/comments",
            new { entityType = "WorkTask", entityId = gorevId, body = "Adı görünmeli" });

        yazma.EnsureSuccessStatusCode();

        // Yazma yanıtı da adı taşıyor: ekran, yeni yorumu listeyi
        // yeniden çekmeden ekleyebilsin.
        var yazilan = await yazma.Content.ReadFromJsonAsync<JsonElement>();
        var yazarAdi = yazilan.GetProperty("createdByName").GetString();

        Assert.False(
            string.IsNullOrWhiteSpace(yazarAdi),
            "Yorum yazıldı ama yazar adı boş döndü.");

        Assert.NotEqual("(bilinmeyen kullanıcı)", yazarAdi);

        // Oturum sahibinin gerçek adı — GUID değil.
        var oturumKimligi = yazilan.GetProperty("createdByUserId").GetString();
        var beklenen = await db.Users
            .Where(x => x.Id == Guid.Parse(oturumKimligi!))
            .Select(x => x.FullName)
            .SingleAsync();

        Assert.Equal(beklenen, yazarAdi);

        // LİSTE UCU DA AYNI ADI VERİYOR: tekil yanıt ile liste
        // ayrışırsa ekran, yenilemeden sonra adı kaybeder.
        var gelen = await ListeAsync(client, gorevId);
        var satir = gelen.GetProperty("items")[0];

        Assert.Equal(beklenen, satir.GetProperty("createdByName").GetString());
        Assert.Equal(oturumKimligi, satir.GetProperty("createdByUserId").GetString());
    }

    /// <summary>
    /// SİLİNMİŞ KULLANICI SESSİZ GEÇMEZ.
    ///
    /// Ad çözülemediğinde boş dönmek, yorumu YAZARSIZ gösterirdi —
    /// okuyan kişi bunu bir arıza değil, sistemin bir özelliği sanar.
    /// Açık metin, belirsizliği görünür kılıyor.
    /// </summary>
    [Fact]
    public async Task CozulemeyenYazar_AcikMetinDonduruyor()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var gorevId = await GorevAsync(db, suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yazma = await client.PostAsJsonAsync(
            "/api/collaboration/comments",
            new { entityType = "WorkTask", entityId = gorevId, body = "Yazarı silinecek" });

        yazma.EnsureSuccessStatusCode();

        /*
         * YAZARI "SİLİYORUZ": kimliği hiç var olmamış bir GUID'e
         * çeviriyoruz. `ExecuteUpdateAsync` araya giriciyi ATLAR —
         * `SaveChanges` yolundan gitseydi denetim araya girici
         * `CreatedByUserId`'yi yeniden oturum sahibine ezerdi
         * (AuditSaveChangesInterceptor:73) ve test hiçbir şey
         * ölçmezdi.
         */
        await db.TaskComments
            .Where(x => x.EntityId == gorevId)
            .ExecuteUpdateAsync(x => x.SetProperty(
                p => p.CreatedByUserId, Guid.NewGuid()));

        var gelen = await ListeAsync(client, gorevId);

        Assert.Equal(
            "(bilinmeyen kullanıcı)",
            gelen.GetProperty("items")[0].GetProperty("createdByName").GetString());
    }

    /// <summary>
    /// N+1 YAPISAL OLARAK ENGELLİ — BEKÇİ BUNU KİLİTLİYOR.
    ///
    /// DTO üreticileri `static`. Bir `static` metodun `db` alanına
    /// erişimi yoktur, yani satır başına sorgu ATAMAZ; adlar çağıran
    /// tarafta tek sorguda toplanıp parametre olarak geçer.
    ///
    /// Bu bekçi, birinin "sadece adı buradan çekiveririm" diyerek
    /// üreticiyi örnek metoduna çevirmesini yakalar — elli yorumluk
    /// bir sayfada elli sorgu, ancak canlıda yavaşlık olarak fark
    /// edilirdi. (M1/3'te aynı hata varlık çözümleyicide yakalanmıştı.)
    ///
    /// Sorgu SAYAN bir test yazmadım: sayaç, paylaşılan test
    /// fabrikasına araya girici eklemeyi gerektiriyor ve o fabrika
    /// 2500'den fazla testin altında. Yapısal kilit, ölçülen sayaçla
    /// aynı şeyi garanti ediyor — çünkü erişim yoksa sorgu da yok.
    /// </summary>
    [Theory]
    [InlineData(typeof(CollaborationController), "YorumDto")]
    [InlineData(typeof(CollaborationController), "EkDto")]
    [InlineData(typeof(CollaborationController), "AdBul")]
    [InlineData(typeof(WorkTasksController), "ToDto")]
    [InlineData(typeof(WorkTasksController), "AdBul")]
    public void DtoUreticileri_StaticKalmali(Type tip, string metotAdi)
    {
        var metot = tip.GetMethod(
            metotAdi,
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.True(
            metot is not null,
            $"{tip.Name}.{metotAdi} artık `static` değil. Örnek metodu " +
            "olması, DTO üreticisinin `db` alanına erişebilmesi ve satır " +
            "başına sorgu atabilmesi demek — elli yorumluk sayfada elli " +
            "sorgu. Adlar çağıran tarafta TEK sorguda toplanmalı.");
    }
}
