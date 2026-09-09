using EnderunAI.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests.Infrastructure;

/// <summary>
/// MUAF UÇ SONDASI — TEK ŞEKİL, 22 ÖRNEK (EKSİK/1 E3).
///
/// ═══ NEDEN VAR ═══
///
/// `MuafUclar.txt` içindeki her satır bir GEREKÇE taşıyor: "izin
/// aranmıyor, ÇÜNKÜ uç yalnız çağıranın kendi verisini döndürür".
/// Gerekçe bir İDDİADIR. Sınanmamış iddia kapı değil, kapı
/// görüntüsüdür.
///
/// ÖLÇÜM: sınanan tek kategoride (`uyelik-kapisi`) ilk denemede kusur
/// çıktı — `MesajEkiServisi.ListeleAsync` gerekçesinin söylediği
/// süzgeci uygulamıyordu (MESAJ/3 C7). Sınanan 2 gerekçeden 1'i
/// yanlıştı; kalan 20'sinin doğru olduğunu varsaymak için sebep yok.
///
/// ═══ TEK ŞEKİL: "SAHİBİN VERİSİNE ULAŞILDI MI" ═══
///
/// Her uç için sahibin verisine benzersiz bir İZ gömülüyor. Sonra tek
/// bir soru soruluyor — okuma ucunda "iz yanıtta göründü mü", yazma
/// ucunda "sahibin kaydı değişti mi". İki uç türü de aynı cümleyi
/// cevapladığı için 22 kopya yerine 22 ÖRNEK yazılıyor. Kopyalanan
/// şekil bir gün ayrışır; tek şekil ayrışamaz.
///
/// ═══ İKİ KOL, İKİSİ DE ZORUNLU ═══
///
///   KOL B (önce koşar) — HAK SAHİBİ çağırır, verisine ULAŞMALI.
///   KOL A             — HAK SAHİBİ OLMAYAN çağırır, ULAŞMAMALI.
///
/// B ÖNCE KOŞUYOR ÇÜNKÜ pozitif kontrolü olmayan bir kırmızı-yeşil
/// hiçbir şey ayırt etmez: uç tamamen bozuk olsaydı (her zaman 500,
/// ya da her zaman boş) A kolu da yeşil verirdi. Bugün bu tuzağa
/// birkaç kez düşüldü; sıra bu yüzden sabit.
///
/// ═══ İKİ KULLANICI AYNI ROLDE — BU BİR KONTROL ═══
///
/// Yabancıya dar bir rol verilseydi A kolu YANLIŞ SEBEPLE yeşil
/// olurdu: engelleyen şey üyelik değil izin eksikliği olurdu ve sonda
/// ölçtüğünü sandığı şeyi ölçmezdi (Kural 65). İki kullanıcı arasındaki
/// TEK fark, kaydın sahibi olmalarıdır.
///
/// ═══ HER KOL KENDİ ZEMİNİNDE ═══
///
/// İki kol ayrı kullanıcı çiftiyle koşuyor. Aynı zeminde koşsalardı B
/// kolunun yan etkisi (okundu damgası, parola değişimi) A kolunun
/// ölçtüğü durumu bozardı.
///
/// ═══ AJANDA/1'E BAĞ ═══
///
/// AJANDA/1'in G1-G3 gizlilik şartı — "atanmamış kişisel kaydı
/// sahibinden başkası göremez, Admin dahil" — tam olarak
/// `kendi-kimligi` kategorisinin sorusudur. Ajandanın gizlilik sondası
/// bu koşumun bir ÖRNEĞİ olarak yazılacak; üçüncü kez aynı şekil
/// yazılmayacak. Tek farkı, yabancının Admin rolünde olması.
/// </summary>
public sealed class MuafUcZemini
{
    public required DatabaseFixture Fixture { get; init; }

    /// <summary>Sahibin verisine gömülen benzersiz iz.</summary>
    public required string Iz { get; init; }

    public required HttpClient Sahip { get; init; }
    public required Guid SahipId { get; init; }
    public required HttpClient Yabanci { get; init; }
    public required Guid YabanciId { get; init; }

    /// <summary>Kurulumun ürettiği hedef kayıt (varsa).</summary>
    public Guid HedefId { get; set; }

    public Guid SirketId { get; set; }

    /// <summary>Son çağrının gövdesi — okuma uçlarında iz burada aranır.</summary>
    public string SonGovde { get; set; } = string.Empty;

    /// <summary>
    /// Kurulumun sakladığı "önceki durum" (ör. parola özeti).
    ///
    /// AYRI ALAN, BİLEREK: `SonGovde` her çağrıda yanıtla EZİLİYOR.
    /// Kurulum notu oraya konsaydı çağrıdan sonra kaybolur ve
    /// karşılaştırma sessizce yanlış sonuç verirdi.
    /// </summary>
    public string KurulumNotu { get; set; } = string.Empty;

    public int SonDurum { get; set; }

    public async Task VeritabaniAsync(Func<AppDbContext, Task> islem)
    {
        using var scope = Fixture.Factory.Services.CreateScope();
        await islem(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public async Task<T> VeritabaniAsync<T>(Func<AppDbContext, Task<T>> islem)
    {
        using var scope = Fixture.Factory.Services.CreateScope();
        return await islem(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>Okuma uçlarının ortak cevabı: iz yanıtta göründü mü.</summary>
    public Task<bool> IzYanittaMi() => Task.FromResult(SonGovde.Contains(Iz));
}

/// <summary>
/// Tek bir muaf ucun sondası. `Ad` alanı `MuafUclar.txt` içindeki
/// satırla BİREBİR aynı yazılır — listedeki hangi gerekçenin
/// sınandığı okunurken belli olsun diye.
/// </summary>
public sealed record MuafUc(
    string Kategori,
    string Ad,
    Func<MuafUcZemini, Task> Kur,
    Func<HttpClient, MuafUcZemini, Task<HttpResponseMessage>> Cagir,
    Func<MuafUcZemini, Task<bool>> SahibinVerisineUlasildiMi);

public static class MuafUcKosumu
{
    /// <summary>
    /// İki kullanıcının ORTAK rolü. Aynı olması şart (yukarıdaki
    /// gerekçe); hangisi olduğu değil.
    /// </summary>
    public const string OrtakRol = "Şantiye Şefi";

    public static async Task IkiKolAsync(
        DatabaseFixture fixture, MuafUc uc, string? yabanciRol = null)
    {
        // ── KOL B — POZİTİF KONTROL, ÖNCE ──────────────────────────
        var b = await KurAsync(fixture, uc, yabanciRol);
        await CagirAsync(uc, b, b.Sahip);

        Assert.True(
            await uc.SahibinVerisineUlasildiMi(b),
            $"POZİTİF KONTROL DÜŞTÜ — {uc.Kategori} | {uc.Ad}\n" +
            $"Hak sahibi KENDİ verisine ulaşamadı (HTTP {b.SonDurum}).\n" +
            "Bu durumda A kolunun yeşili hiçbir şey kanıtlamaz: uç zaten " +
            "çalışmıyor olabilir. ÖNCE BUNU DÜZELT.\n" +
            $"Yanıt: {Kirp(b.SonGovde)}");

        // ── KOL A — ASIL ÖLÇÜM, TEMİZ ZEMİNDE ──────────────────────
        var a = await KurAsync(fixture, uc, yabanciRol);
        await CagirAsync(uc, a, a.Yabanci);

        Assert.False(
            await uc.SahibinVerisineUlasildiMi(a),
            $"MUAFİYET GEREKÇESİ YANLIŞ — {uc.Kategori} | {uc.Ad}\n" +
            $"Hak sahibi OLMAYAN kullanıcı, sahibin verisine ULAŞTI " +
            $"(HTTP {a.SonDurum}).\n" +
            "MuafUclar.txt'deki gerekçe bu ucu savunmuyor.\n" +
            $"Yanıt: {Kirp(a.SonGovde)}");
    }

    /// <summary>
    /// ÜÇÜNCÜ SONUÇ: ÖLÇEMEDİ (Kural 67).
    ///
    /// Kurulum çökerse sonda ihlal BULMAMIŞTIR — ölçüm hiç yapılmamıştır.
    /// İlk koşuda tam olarak bu oldu: test veritabanında şirket yoktu,
    /// 11 sondanın hepsi kurulumda düştü ve çıktı "11 kırmızı" gibi
    /// göründü. Sayı yeterince inanılmaz olduğu için fark edildi;
    /// ihlalin bir sonraki sefer 2 tane olması hâlinde fark edilmezdi.
    ///
    /// Mesaj bu yüzden gürültülü: kurulum hatası, uç kusurundan AYRI
    /// okunmalı.
    /// </summary>
    private static async Task<MuafUcZemini> KurAsync(
        DatabaseFixture fixture, MuafUc uc, string? yabanciRol)
    {
        try
        {
            var zemin = await ZeminAsync(fixture, yabanciRol);
            await uc.Kur(zemin);
            return zemin;
        }
        catch (Exception hata)
        {
            throw new InvalidOperationException(
                $"ÖLÇEMEDİ — {uc.Kategori} | {uc.Ad}\n" +
                "Sonda KURULUMDA düştü; uç HİÇ ÇAĞRILMADI. Bu bir ihlal " +
                "bulgusu DEĞİLDİR, sondanın kendi hatasıdır. " +
                "Muafiyet gerekçesi hakkında bu koşudan hiçbir sonuç çıkmaz.\n" +
                $"Kurulum hatası: {hata.Message}", hata);
        }
    }

    private static async Task CagirAsync(
        MuafUc uc, MuafUcZemini zemin, HttpClient istemci)
    {
        using var yanit = await uc.Cagir(istemci, zemin);
        zemin.SonDurum = (int)yanit.StatusCode;
        zemin.SonGovde = await yanit.Content.ReadAsStringAsync();
    }

    private static async Task<MuafUcZemini> ZeminAsync(
        DatabaseFixture fixture, string? yabanciRol)
    {
        var iz = "iz" + Guid.NewGuid().ToString("N")[..10];

        var sahip = await TestUserFactory.KullaniciKurAsync(
            fixture, iz, [OrtakRol]);
        var yabanci = await TestUserFactory.KullaniciKurAsync(
            fixture, "yabanci", [yabanciRol ?? OrtakRol]);

        Guid sirket;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            /*
             * ŞİRKET VARSA KULLANILIR, YOKSA KURULUR.
             *
             * Test veritabanı rollerle ve yönetici hesabıyla doğuyor
             * ama ŞİRKETSİZ; şirketi ihtiyacı olan test kendisi
             * kuruyor. İlk koşuda `FirstAsync()` boş kümede düştü ve
             * 11 sondanın hepsi ölçüm yapmadan kırmızı verdi —
             * kırmızının sebebi uçlar değil, kurulumdu.
             */
            sirket = await db.Companies
                .OrderBy(x => x.CreatedAtUtc)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync()
                ?? (await TestDataFactory.CreateCompanyStackAsync(
                        db, "muafuc" + Guid.NewGuid().ToString("N")[..6])).Company.Id;
        }

        return new MuafUcZemini
        {
            Fixture = fixture,
            Iz = iz,
            Sahip = sahip.Istemci,
            SahipId = sahip.Id,
            Yabanci = yabanci.Istemci,
            YabanciId = yabanci.Id,
            SirketId = sirket
        };
    }

    private static string Kirp(string s) =>
        s.Length <= 400 ? s : s[..400] + "… (kırpıldı)";
}
