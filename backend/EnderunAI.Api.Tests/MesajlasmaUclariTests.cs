using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Messaging;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// MESAJLAŞMA UÇLARI — M3/2.
///
/// Erişimin tek kaynağı ÜYELİK. Bu testler kapsam ile üyeliğin AYRI
/// kapılar olduğunu ve ikisinin de gerektiğini sınıyor: aynı şirkette
/// çalışan bir yabancı, kapsam süzgecini geçse bile başkasının
/// konuşmasını göremiyor.
/// </summary>
[Collection("Integration")]
public sealed class MesajlasmaUclariTests(DatabaseFixture fixture)
{
    private sealed record Kisi(Guid UserId, Guid PersonnelId, HttpClient Client);

    private sealed record Sahne(Guid CompanyId, Kisi Ali, Kisi Veli, Kisi Yabanci);

    /// <summary>
    /// Kullanıcıyı PERSONELE BAĞLAYARAK kurar.
    ///
    /// Bağ şart: "kime yazabilirim" sorusu personelin şirketinden
    /// cevaplanıyor. Personelsiz kullanıcı (yalnız sistem hesabı)
    /// kişi listesinde çıkmıyor — dar olan seçildi.
    /// </summary>
    private async Task<Kisi> KisiKurAsync(Guid companyId, string ek, string ad)
    {
        var client = await TestUserFactory.CreateCompanyScopedClientAsync(
            fixture, ek, ["Şantiye Şefi"], companyId);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var personel = await TestDataFactory.CreatePersonnelAsync(db, companyId, ek);

        var kullanici = await db.Users
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstAsync(x => x.Username.Contains(ek));

        kullanici.PersonnelId = personel.Id;
        kullanici.FullName = ad;
        await db.SaveChangesAsync();

        return new Kisi(kullanici.Id, personel.Id, client);
    }

    private async Task<Sahne> SahneKurAsync(string ek)
    {
        Guid companyId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var proje = await TestDataFactory.CreateProjectAsync(db, $"MSJ{ek}");
            companyId = proje.CompanyId;
        }

        var ali = await KisiKurAsync(companyId, $"a{ek}", "Ali Yılmaz");
        var veli = await KisiKurAsync(companyId, $"v{ek}", "Veli Şahin");
        var yabanci = await KisiKurAsync(companyId, $"y{ek}", "Yabancı Kişi");

        return new Sahne(companyId, ali, veli, yabanci);
    }

    private static async Task<Guid> KonusmaAcAsync(Kisi acan, Guid karsiUserId)
    {
        var cevap = await acan.Client.PostAsJsonAsync(
            "/api/mesajlar/konusmalar/birebir", new { karsiUserId });

        Assert.Equal(HttpStatusCode.OK, cevap.StatusCode);

        var govde = await cevap.Content.ReadFromJsonAsync<JsonElement>();
        return govde.GetProperty("id").GetGuid();
    }

    private static async Task GonderAsync(Kisi kisi, Guid konusmaId, string govde)
    {
        var cevap = await kisi.Client.PostAsJsonAsync(
            $"/api/mesajlar/konusmalar/{konusmaId}/mesajlar", new { govde });

        Assert.Equal(HttpStatusCode.OK, cevap.StatusCode);
    }

    /// <summary>
    /// BİREBİR KONUŞMA İKİNCİ KEZ AÇILMAZ, MEVCUT GELİR.
    ///
    /// Her açılışta yeni konuşma üretilseydi aynı iki kişi arasında
    /// onlarca kopya birikir ve geçmiş parçalanırdı: kullanıcı dün
    /// yazdığını bugün bulamazdı.
    /// </summary>
    [Fact]
    public async Task BirebirKonusma_IkinciKezAcilmaz()
    {
        var s = await SahneKurAsync($"b{DateTime.UtcNow:ffffff}");

        var birinci = await KonusmaAcAsync(s.Ali, s.Veli.UserId);
        var ikinci = await KonusmaAcAsync(s.Ali, s.Veli.UserId);

        Assert.Equal(birinci, ikinci);

        // Karşı taraf açtığında da AYNI konuşma gelmeli.
        var karsidan = await KonusmaAcAsync(s.Veli, s.Ali.UserId);
        Assert.Equal(birinci, karsidan);
    }

    /// <summary>
    /// KENDİNE KONUŞMA AÇILMAZ.
    ///
    /// "Kendine not" ayrı bir ihtiyaç ve `yapilacaklar` orada duruyor.
    /// Dar olan seçildi.
    /// </summary>
    [Fact]
    public async Task KendineKonusma_Acilmaz()
    {
        var s = await SahneKurAsync($"k{DateTime.UtcNow:ffffff}");

        var cevap = await s.Ali.Client.PostAsJsonAsync(
            "/api/mesajlar/konusmalar/birebir", new { karsiUserId = s.Ali.UserId });

        Assert.Equal(HttpStatusCode.BadRequest, cevap.StatusCode);
    }

    /// <summary>
    /// YABANCI, BAŞKASININ KONUŞMASINI OKUYAMAZ — AYNI ŞİRKETTE OLSA BİLE.
    ///
    /// Kapsam süzgeci burada YETMİYOR: üçü de aynı şirkette. Engelleyen
    /// şey üyelik. İki kapının ayrı olmasının kanıtı bu test.
    ///
    /// Cevap "yetkiniz yok" değil "bulunamadı": yetki hatası,
    /// konuşmanın VAR OLDUĞUNU söylerdi.
    /// </summary>
    [Fact]
    public async Task Yabanci_BaskasininKonusmasiniOkuyamaz()
    {
        var s = await SahneKurAsync($"y{DateTime.UtcNow:ffffff}");

        var konusmaId = await KonusmaAcAsync(s.Ali, s.Veli.UserId);
        await GonderAsync(s.Ali, konusmaId, "Gizli kalsın");

        var cevap = await s.Yabanci.Client.GetAsync(
            $"/api/mesajlar/konusmalar/{konusmaId}/mesajlar");

        Assert.Equal(HttpStatusCode.BadRequest, cevap.StatusCode);
        Assert.Contains("bulunamadı", await cevap.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// YABANCI, BAŞKASININ KONUŞMASINA YAZAMAZ.
    ///
    /// Okuma kapalı ama yazma açık kalsaydı, yabancı içeriği
    /// göremeden konuşmaya mesaj düşürebilirdi.
    /// </summary>
    [Fact]
    public async Task Yabanci_BaskasininKonusmasinaYazamaz()
    {
        var s = await SahneKurAsync($"w{DateTime.UtcNow:ffffff}");

        var konusmaId = await KonusmaAcAsync(s.Ali, s.Veli.UserId);

        var cevap = await s.Yabanci.Client.PostAsJsonAsync(
            $"/api/mesajlar/konusmalar/{konusmaId}/mesajlar",
            new { govde = "İzinsiz mesaj" });

        Assert.Equal(HttpStatusCode.BadRequest, cevap.StatusCode);

        // Mesaj GERÇEKTEN yazılmamış olmalı — hata dönüp yine de
        // kaydetmek en sinsi hâl olurdu.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(await db.Messages.AnyAsync(x => x.Body == "İzinsiz mesaj"));
    }

    /// <summary>
    /// OKUNMAMIŞ SAYISI: KARŞI TARAFIN YAZDIĞI SAYILIR, KENDİMİNKİ SAYILMAZ.
    ///
    /// Kendi mesajım sayılsaydı kişi kendi yazdığı yüzünden rozet
    /// görürdü; rozet anlamını yitirirdi.
    /// </summary>
    [Fact]
    public async Task Okunmamis_KendiMesajimiSaymaz()
    {
        var s = await SahneKurAsync($"o{DateTime.UtcNow:ffffff}");

        var konusmaId = await KonusmaAcAsync(s.Ali, s.Veli.UserId);

        await GonderAsync(s.Ali, konusmaId, "Ali'den bir");
        await GonderAsync(s.Ali, konusmaId, "Ali'den iki");

        Assert.Equal(0, await OkunmamisAsync(s.Ali));
        Assert.Equal(2, await OkunmamisAsync(s.Veli));

        // Veli okuduğunu işaretleyince sıfırlanır.
        var okundu = await s.Veli.Client.PostAsync(
            $"/api/mesajlar/konusmalar/{konusmaId}/okundu", null);
        Assert.Equal(HttpStatusCode.OK, okundu.StatusCode);

        Assert.Equal(0, await OkunmamisAsync(s.Veli));
    }

    /// <summary>
    /// ARAMA ÜÇ HARFTEN KISA SORGUYU REDDEDER — SUNUCUDA.
    ///
    /// Ekran kuralı yalnız kolaylık; ekran atlanabilir, uç doğrudan
    /// çağrılabilir. Ölçüldü: iki harfte trigram indeksi devre dışı
    /// kalıyor ve sorgu sıra taramasına düşüyor.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("  ab  ")]
    public async Task Arama_UcHarftenKisaSorguyuReddeder(string sorgu)
    {
        var s = await SahneKurAsync($"u{DateTime.UtcNow:ffffff}");

        var cevap = await s.Ali.Client.GetAsync(
            $"/api/mesajlar/ara?q={Uri.EscapeDataString(sorgu)}");

        Assert.Equal(HttpStatusCode.BadRequest, cevap.StatusCode);
        Assert.Contains("3 harf", await cevap.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// ARAMA TÜRKÇE KATLAMA YAPAR VE YALNIZ KENDİ KONUŞMAMDA ARAR.
    ///
    /// "insaat" yazan "İNŞAAT"ı bulmalı; kullanıcı arama kutusuna
    /// Türkçe karakter yazmak için klavye değiştirmez.
    ///
    /// Aynı testte ikinci kapı da sınanıyor: yabancı aynı kelimeyi
    /// arayınca HİÇBİR ŞEY bulmuyor. Arama, erişim kapısını delen en
    /// olası yol — tüm mesajlarda arayıp "yalnız başlıkları" göstermek
    /// bile sızıntı olurdu.
    /// </summary>
    [Fact]
    public async Task Arama_TurkceKatlarVeYalnizKendiKonusmamdaArar()
    {
        var s = await SahneKurAsync($"t{DateTime.UtcNow:ffffff}");

        var konusmaId = await KonusmaAcAsync(s.Ali, s.Veli.UserId);
        await GonderAsync(s.Ali, konusmaId, "İNŞAAT ŞANTİYESİ ölçümü yapıldı");

        var bulunan = await AraAsync(s.Ali, "insaat");
        Assert.Single(bulunan);
        Assert.Contains("İNŞAAT", bulunan[0].GetProperty("govde").GetString()!);

        // Karşı taraf da kendi konuşmasında buluyor.
        Assert.Single(await AraAsync(s.Veli, "santiye"));

        // Yabancı hiçbir şey bulmuyor.
        Assert.Empty(await AraAsync(s.Yabanci, "insaat"));
    }

    /// <summary>
    /// KİŞİ ARAMA KAPSAMLA SINIRLI — BAŞKA ŞİRKETİN PERSONELİ ÇIKMAZ.
    ///
    /// Kişi listesi "kime yazabilirim" sorusunu cevaplıyor. Kapsam
    /// dışına açık olsaydı, bir şirketin kullanıcısı diğer şirketin
    /// çalışan listesini arama kutusundan dökebilirdi — mesaj
    /// göndermeden, yalnız isimleri görerek.
    /// </summary>
    [Fact]
    public async Task KisiArama_BaskaSirketinPersoneliniGostermez()
    {
        var ek = $"c{DateTime.UtcNow:ffffff}";
        var s = await SahneKurAsync(ek);

        Guid digerSirket;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var proje = await TestDataFactory.CreateProjectAsync(db, $"DGR{ek}");
            digerSirket = proje.CompanyId;
        }

        var disaridaki = await KisiKurAsync(digerSirket, $"d{ek}", "Ali Yabancı");

        var cevap = await s.Ali.Client.GetAsync("/api/mesajlar/kisiler?q=ali");
        Assert.Equal(HttpStatusCode.OK, cevap.StatusCode);

        var liste = (await cevap.Content.ReadFromJsonAsync<JsonElement>())
            .EnumerateArray()
            .Select(x => x.GetProperty("userId").GetGuid())
            .ToList();

        Assert.DoesNotContain(disaridaki.UserId, liste);

        // Kendisi de listede olmamalı: kendine mesaj yok.
        Assert.DoesNotContain(s.Ali.UserId, liste);
    }

    /// <summary>
    /// BOŞ MESAJ GÖNDERİLEMEZ, UZUN MESAJ KESİLMEZ — REDDEDİLİR.
    ///
    /// Sessizce kesmek, kullanıcının yazdığının yarısının gittiğini
    /// fark etmemesi demekti.
    /// </summary>
    [Fact]
    public async Task Mesaj_BosVeAsiriUzunReddedilir()
    {
        var s = await SahneKurAsync($"m{DateTime.UtcNow:ffffff}");
        var konusmaId = await KonusmaAcAsync(s.Ali, s.Veli.UserId);

        var bos = await s.Ali.Client.PostAsJsonAsync(
            $"/api/mesajlar/konusmalar/{konusmaId}/mesajlar", new { govde = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, bos.StatusCode);

        var uzun = await s.Ali.Client.PostAsJsonAsync(
            $"/api/mesajlar/konusmalar/{konusmaId}/mesajlar",
            new { govde = new string('x', 4001) });
        Assert.Equal(HttpStatusCode.BadRequest, uzun.StatusCode);
    }

    /// <summary>
    /// AYRILAN ÜYE ARTIK OKUYAMAZ.
    ///
    /// `LeftAtUtc` dolu olan üye, ayrıldığı tarihe kadarki mesajları
    /// da göremiyor (karar: dar olan seçildi). Üyelik satırı
    /// silinmiyor — "o tarihte kim görüyordu" sorusunun tek cevabı.
    /// </summary>
    [Fact]
    public async Task AyrilanUye_ArtikOkuyamaz()
    {
        var s = await SahneKurAsync($"a{DateTime.UtcNow:ffffff}");
        var konusmaId = await KonusmaAcAsync(s.Ali, s.Veli.UserId);
        await GonderAsync(s.Ali, konusmaId, "Ayrılmadan önce");

        // Veli okuyabiliyor.
        var once = await s.Veli.Client.GetAsync(
            $"/api/mesajlar/konusmalar/{konusmaId}/mesajlar");
        Assert.Equal(HttpStatusCode.OK, once.StatusCode);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var uyelik = await db.ConversationMembers
                .FirstAsync(x => x.ConversationId == konusmaId
                                 && x.UserId == s.Veli.UserId);
            uyelik.LeftAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var sonra = await s.Veli.Client.GetAsync(
            $"/api/mesajlar/konusmalar/{konusmaId}/mesajlar");
        Assert.Equal(HttpStatusCode.BadRequest, sonra.StatusCode);

        // Okunmamış sayısı da sıfırlanmalı: ayrılan üyenin rozeti
        // görmeye devam etmesi, göremeyeceği içeriği haber vermek olurdu.
        Assert.Equal(0, await OkunmamisAsync(s.Veli));
    }

    /// <summary>
    /// KONUŞMA LİSTESİ: EN SON KONUŞULAN ÜSTTE, SAYFA TAVANI SUNUCUDA.
    /// </summary>
    [Fact]
    public async Task KonusmaListesi_EnSonKonusulanUstte()
    {
        var ek = $"l{DateTime.UtcNow:ffffff}";
        var s = await SahneKurAsync(ek);
        var ucuncu = await KisiKurAsync(s.CompanyId, $"3{ek}", "Üçüncü Kişi");

        var veliyle = await KonusmaAcAsync(s.Ali, s.Veli.UserId);
        var ucuncuyle = await KonusmaAcAsync(s.Ali, ucuncu.UserId);

        await GonderAsync(s.Ali, veliyle, "önce");
        await GonderAsync(s.Ali, ucuncuyle, "sonra");

        var cevap = await s.Ali.Client.GetAsync("/api/mesajlar/konusmalar?limit=10");
        Assert.Equal(HttpStatusCode.OK, cevap.StatusCode);

        var kayitlar = (await cevap.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("kayitlar").EnumerateArray().ToList();

        Assert.Equal(ucuncuyle, kayitlar[0].GetProperty("id").GetGuid());
        Assert.Equal(veliyle, kayitlar[1].GetProperty("id").GetGuid());

        // Başlık karşı tarafın adı olmalı — birebir konuşmada başlık
        // taraflardır, ayrı bir alan tutulmuyor.
        Assert.Equal("Üçüncü Kişi", kayitlar[0].GetProperty("baslik").GetString());
    }

    private static async Task<int> OkunmamisAsync(Kisi kisi)
    {
        var cevap = await kisi.Client.GetAsync("/api/mesajlar/okunmamis");
        Assert.Equal(HttpStatusCode.OK, cevap.StatusCode);

        return (await cevap.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("sayi").GetInt32();
    }

    private static async Task<List<JsonElement>> AraAsync(Kisi kisi, string sorgu)
    {
        var cevap = await kisi.Client.GetAsync(
            $"/api/mesajlar/ara?q={Uri.EscapeDataString(sorgu)}");

        Assert.Equal(HttpStatusCode.OK, cevap.StatusCode);

        return (await cevap.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("kayitlar").EnumerateArray().ToList();
    }
}
