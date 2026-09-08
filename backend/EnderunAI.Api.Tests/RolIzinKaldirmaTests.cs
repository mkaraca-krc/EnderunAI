using System.Text.RegularExpressions;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// ROLDEN KALDIRMA KALICIDIR — KURAL VE TEK YAZMA YOLU (SEED/1).
///
/// ═══ ÖLÇÜLEN KUSUR ═══
///
/// Tohumlayıcı her açılışta katalogdaki eksik çiftleri geri
/// ekliyordu. Denetim kaydından ölçüldü: 18:57'de kaldırılan 6 çift,
/// 20:33'te (yeniden başlatma) geri geldi. Bugüne kadarki 6
/// kaldırmanın 6'sı da geri gelmişti.
///
/// Deneysel ölçüm: katalog 595 çift üretiyor, canlıda 597 vardı —
/// yani 597'nin 595'i kaldırılsa geri gelirdi.
/// </summary>
public sealed class RolIzinKuraliTests
{
    /// <summary>
    /// DOĞRULUK TABLOSUNUN DÖRT SATIRI — hepsi tek yerde tanımlı
    /// olduğu için hepsi tek yerde sınanabiliyor (KATALOG/1).
    ///
    /// Eski biçim üç girdiliydi (`katalogdaVar, zatenVar, kaldirilmis`)
    /// ve bir EYLEM soruyordu: "ekleyeyim mi". Eylem sorusu tek
    /// yönlüdür — silme yönünü ifade edemez. Yeni biçim bir DURUM
    /// söylüyor: "bulunmalı mı".
    /// </summary>
    [Theory]
    // katalogda VAR + kaldırma YOK -> BULUNSUN
    [InlineData(true, false, false, true)]
    // katalogda VAR + kaldırma VAR -> BULUNMASIN (SEED/1'in kusuru)
    [InlineData(true, true, false, false)]
    // katalogda YOK + elle ekleme YOK -> BULUNMASIN (AC1'in kusuru)
    [InlineData(false, false, false, false)]
    // katalogda YOK + elle ekleme VAR -> BULUNSUN
    [InlineData(false, false, true, true)]
    public void Kural_DortHalde_DogruKararVeriyor(
        bool katalogdaVar, bool kaldirilmis, bool elleEklendi, bool beklenen)
    {
        Assert.Equal(
            beklenen,
            RolIzinKurali.BulunmaliMi(katalogdaVar, kaldirilmis, elleEklendi));
    }

    /// <summary>
    /// KATALOG ÜYELİĞİ HANGİ KAYDIN GEÇERLİ OLDUĞUNU SEÇER.
    ///
    /// İki kayıt aynı anda bulunabilir: bir izin elle verilip sonra
    /// katalog**a** girebilir, ya da tersi. Kuralın bu durumda ne
    /// yaptığı belirsiz kalmamalı — belirsiz bir kural, yarın iki
    /// farklı okuyucuya iki farklı cevap verir.
    /// </summary>
    [Theory]
    // katalogda VAR: kaldırma kaydı karar verir, elle ekleme yok sayılır
    [InlineData(true, true, true, false)]
    // katalogda YOK: elle ekleme karar verir, kaldırma yok sayılır
    [InlineData(false, true, true, true)]
    public void IkiKayitBirdenVarsa_KatalogUyeligiSecer(
        bool katalogdaVar, bool kaldirilmis, bool elleEklendi, bool beklenen)
    {
        Assert.Equal(
            beklenen,
            RolIzinKurali.BulunmaliMi(katalogdaVar, kaldirilmis, elleEklendi));
    }
}

/// <summary>
/// KALDIRMA KAYDINI YAZAN TEK YER MATRİS TOGGLE'I (SEED/1 SB2).
///
/// ═══ NEDEN MUHAFIZ ═══
///
/// İkinci bir yazma yolu açılırsa kural iki yerde yaşamaya başlar ve
/// biri unutulur (Kural 79). Bu tam olarak `dashboard.view`
/// olayının sınıfı: kayıt doğruydu, çözücü doğruydu, ama başka bir
/// kod yolu onu geri alıyordu.
///
/// Ayrıca AC1'de ölçülen bir açık var: iki çift
/// (Teknik Koordinatör/Teknik Ofis -> projects.delete) veritabanında
/// duruyor ama katalogda YOK ve denetim kaydında oluşturma olayları
/// YOK. Katalog dışı bir yazma yolu varsa bu muhafız onu da
/// görünür kılar.
/// </summary>
public sealed class RolIzinKaydiTekYerTests
{
    private static string ApiKoku()
    {
        var dizin = new DirectoryInfo(AppContext.BaseDirectory);

        while (dizin is not null &&
               !Directory.Exists(Path.Combine(dizin.FullName, "EnderunAI.Api")))
        {
            dizin = dizin.Parent;
        }

        return dizin is null
            ? throw new InvalidOperationException("Çözüm kökü bulunamadı.")
            : Path.Combine(dizin.FullName, "EnderunAI.Api");
    }

    /// <summary>Yorum ve dize sabitleri atılıyor — kapı kendi gerekçesini ihlal saymasın.</summary>
    private static string YorumsuzGovde(string metin)
    {
        var govde = Regex.Replace(metin, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        govde = Regex.Replace(govde, @"//[^\n]*", " ");
        return Regex.Replace(govde, "\"(?:[^\"\\\\\n]|\\\\.)*\"", "\"\"");
    }

    private static IReadOnlyList<string> KaynakDosyalari() =>
        Directory.EnumerateFiles(ApiKoku(), "*.cs", SearchOption.AllDirectories)
            .Where(y => !y.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(y => !y.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(y => !y.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

    /// <summary>
    /// İDDİA: `RolePermissionRevocations` koleksiyonuna YAZAN
    /// (Add/Remove/RemoveRange) yalnız iki dosya var — matris
    /// kontrolcüsü ve `DbContext` tanımı.
    /// </summary>
    [Fact]
    public void KaldirmaKaydina_YalnizMatrisToggleI_Yaziyor()
    {
        var izinliler = new[]
        {
            "PermissionMatrixController.cs",
            "AppDbContext.cs",
        };

        var yazanlar = new List<string>();

        foreach (var yol in KaynakDosyalari())
        {
            var govde = YorumsuzGovde(File.ReadAllText(yol));

            if (Regex.IsMatch(
                    govde,
                    @"RolePermissionRevocations\s*\.\s*(Add|Remove|RemoveRange|AddRange)"))
            {
                yazanlar.Add(Path.GetFileName(yol));
            }
        }

        var kacaklar = yazanlar.Except(izinliler, StringComparer.Ordinal).ToList();

        Assert.True(
            kacaklar.Count == 0,
            "Kaldırma kaydına matris toggle'ı DIŞINDA yazan dosya(lar): "
            + string.Join(", ", kacaklar)
            + ". Kural iki yerde yaşarsa biri unutulur (Kural 79).");
    }

    /// <summary>
    /// AYNI MUHAFIZ, SİMETRİK KAYIT İÇİN (KATALOG/1 · KT1).
    ///
    /// Elle ekleme kaydı, uzlaştırıcının bir satırı SİLMEMESİNİ
    /// söylüyor. Kontrolsüz yazılabilseydi, katalog dışı herhangi bir
    /// satır "bilerek verildi" damgası alıp silinmekten kurtulurdu —
    /// yani AC1'in kusuru geri gelirdi, üstelik meşru görünerek.
    /// </summary>
    [Fact]
    public void ElleEklemeKaydina_YalnizMatrisToggleI_Yaziyor()
    {
        var izinliler = new[]
        {
            "PermissionMatrixController.cs",
            "AppDbContext.cs",
        };

        var yazanlar = new List<string>();

        foreach (var yol in KaynakDosyalari())
        {
            var govde = YorumsuzGovde(File.ReadAllText(yol));

            if (Regex.IsMatch(
                    govde,
                    @"RoleManualPermissionGrants\s*\.\s*(Add|Remove|RemoveRange|AddRange)"))
            {
                yazanlar.Add(Path.GetFileName(yol));
            }
        }

        var kacaklar = yazanlar.Except(izinliler, StringComparer.Ordinal).ToList();

        Assert.True(
            kacaklar.Count == 0,
            "Elle ekleme kaydına matris toggle'ı DIŞINDA yazan dosya(lar): "
            + string.Join(", ", kacaklar)
            + ". Kural iki yerde yaşarsa biri unutulur (Kural 79).");
    }

    /// <summary>
    /// POZİTİF KONTROL (Kural 48): muhafız gerçekten yazan bir dosya
    /// bulabiliyor mu. Hiçbir şey bulamadığı için yeşil olan bir kapı
    /// ile, ihlal olmadığı için yeşil olan kapı aynı görünür.
    /// </summary>
    [Fact]
    public void Muhafiz_BilinenYazaniBulabiliyor()
    {
        var bulundu = KaynakDosyalari()
            .Where(y => Path.GetFileName(y) == "PermissionMatrixController.cs")
            .Select(y => YorumsuzGovde(File.ReadAllText(y)))
            .Any(govde =>
                Regex.IsMatch(govde, @"RolePermissionRevocations\s*\.\s*Add")
                && Regex.IsMatch(govde, @"RoleManualPermissionGrants\s*\.\s*Add"));

        Assert.True(
            bulundu,
            "Muhafız, matris toggle'ında bilinen iki yazmayı bulamadı — " +
            "desen ya da dosya listesi yanlış yere bakıyor.");
    }

    /// <summary>
    /// POZİTİF KONTROL — MUHAFIZ GERÇEKTEN OKUYOR VE ISIRIYOR.
    ///
    /// Üstteki test hiçbir dosya okunmasaydı da yeşil kalırdı
    /// (Kural 48). Burada hem yüzeyin büyüklüğü hem desenin sahte bir
    /// ihlali yakaladığı sınanıyor.
    /// </summary>
    [Fact]
    public void Muhafiz_Isiriyor()
    {
        Assert.True(KaynakDosyalari().Count > 200, "Taranan dosya sayısı beklenenden az.");

        const string sahte = "db.RolePermissionRevocations.Add(new RolePermissionRevocation());";
        Assert.Matches(
            @"RolePermissionRevocations\s*\.\s*(Add|Remove|RemoveRange|AddRange)",
            YorumsuzGovde(sahte));

        // Meşru okuma (yazma değil) yanlış alarm vermiyor.
        const string mesru = "var x = await db.RolePermissionRevocations.ToListAsync();";
        Assert.DoesNotMatch(
            @"RolePermissionRevocations\s*\.\s*(Add|Remove|RemoveRange|AddRange)",
            YorumsuzGovde(mesru));
    }

    /// <summary>
    /// TOGGLE GERÇEKTEN YAZIYOR — "kimse yazmıyor" da testi geçerdi.
    ///
    /// Üstteki kapı bir YASAK; bu test o yasağın boş bir yasak
    /// olmadığını gösteriyor. İkisi olmadan "hiç kimse yazmıyor"
    /// durumu da yeşil görünürdü.
    /// </summary>
    [Fact]
    public void MatrisToggleI_KaydiHemYaziyor_HemSiliyor()
    {
        var yol = Path.Combine(ApiKoku(), "Controllers", "PermissionMatrixController.cs");
        var govde = YorumsuzGovde(File.ReadAllText(yol));

        Assert.Matches(@"RolePermissionRevocations\s*\.\s*Add", govde);
        Assert.Matches(@"RolePermissionRevocations\s*\.\s*Remove", govde);
    }
}

/// <summary>
/// SB5 — ÖKSÜZ KALDIRMA KAYDI BİRİKEMEZ.
///
/// İki yol ölçüldü:
///
/// 1. İZİN KATALOGDAN ÇIKARSA: `SeedPermissionsAsync` izinleri yalnız
///    EKLİYOR, hiç silmiyor (kaynakta ölçüldü). Satır veritabanında
///    kalır; kaldırma kaydı da anlamlı kalır — yalnız tohumlayıcı o
///    çifti zaten eklemeyeceği için kayıt boşta durur. Zararsız.
///
/// 2. İZİN SATIRI GERÇEKTEN SİLİNİRSE: yabancı anahtar `Cascade`
///    olduğu için kaldırma kaydı da düşer. Öksüz kayıt İMKÂNSIZ.
///
/// Bu test ikinciyi ÖLÇÜYOR — davranışı varsaymıyor.
/// </summary>
[Collection("Integration")]
public sealed class KaldirmaKaydiOksuzKalmazTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task IzinSilinince_KaldirmaKaydiDaDuser()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rol = new AppRole { Name = $"OksuzTest-{Guid.NewGuid():N}"[..30] };
        var izin = new Permission
        {
            Key = $"oksuz.test.{Guid.NewGuid():N}"[..40],
            Name = "Öksüz testi",
            Module = "Test",
            Description = "Yalnız bu test için."
        };

        db.Roles.Add(rol);
        db.Permissions.Add(izin);
        await db.SaveChangesAsync();

        db.RolePermissionRevocations.Add(new RolePermissionRevocation
        {
            RoleId = rol.Id,
            PermissionId = izin.Id
        });
        await db.SaveChangesAsync();

        // POZİTİF KONTROL: kayıt gerçekten yazıldı. Yazılmasaydı
        // "silindi" iddiası boş olurdu (Kural 48).
        Assert.True(await db.RolePermissionRevocations
            .AnyAsync(x => x.RoleId == rol.Id && x.PermissionId == izin.Id));

        db.Permissions.Remove(izin);
        await db.SaveChangesAsync();

        Assert.False(
            await db.RolePermissionRevocations
                .AnyAsync(x => x.RoleId == rol.Id && x.PermissionId == izin.Id),
            "İzin silindi ama kaldırma kaydı kaldı — öksüz kayıt birikir.");
    }
}
