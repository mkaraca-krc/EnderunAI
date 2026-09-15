using System.Text.RegularExpressions;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// İZLEYİCİYİ ATLAYAN YAZMA — DIŞLAMAYLA KAPALI KAPI.
///
/// ═══ NEDEN VAR ═══
///
/// Damga sorumluluğu servislerde değil, `SaveChanges` araya
/// giricisindedir (`AuditSaveChangesInterceptor`). Bu, riski yer
/// değiştirir: artık "bir yol damgayı unutur" değil, **"bir yazma
/// izleyiciyi hiç görmez"** riski vardır.
///
/// `ExecuteUpdateAsync`, `ExecuteDeleteAsync` ve ham SQL `SaveChanges`i
/// HİÇ ÇAĞIRMAZ. O yazmalarda:
///   · `UpdatedAtUtc` / `UpdatedByUserId` yazılmaz,
///   · `security_audit_events` satırı oluşmaz,
///   · yumuşak silme (`IsDeleted`) devreye girmez — satır GERÇEKTEN gider.
///
/// 2026-09-14'te bu açık ölçüldü ve dürüst sınır olarak kaydedildi:
/// *"bugünkü güvence tek seferlik bir taramadır; yarın eklenen bir
/// yazmayı kimse durdurmaz."* Bu test o cümleyi kapatıyor —
/// **tek seferlik tarama bir fotoğraftır, muhafız bir alışkanlıktır.**
///
/// ═══ NEDEN DIŞLAMA, NEDEN KARA LİSTE DEĞİL ═══
///
/// Bugünkü 12 kullanımın hepsi İŞLEVSEL (satır kilidi, proje silme,
/// izin temizliği). Hepsini bir gecede çevirmek gereksiz risk. Bunun
/// yerine: bilinenler GEREKÇESİYLE listelenir, **listede olmayan her
/// YENİ kullanım kırmızı yakar.** İstisnaya eklemek bilinçli bir
/// hamledir ve gerekçe yazmayı zorunlu kılar.
/// </summary>
public sealed class IzleyiciyiAtlayanYazmaTests
{
    private static string DepoKoku()
    {
        var dizin = new DirectoryInfo(AppContext.BaseDirectory);
        while (dizin is not null && !Directory.Exists(Path.Combine(dizin.FullName, "deploy")))
        {
            dizin = dizin.Parent;
        }

        return dizin?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    /// <summary>
    /// `SaveChanges`i atlayan EF ve ham SQL yazma çağrıları.
    /// `FromSql*` LİSTEDE YOK: o okuma yapar, yazma değil.
    /// </summary>
    private static readonly Regex AtlayanYazma = new(
        @"\b(ExecuteUpdate(Async)?|ExecuteDelete(Async)?|ExecuteSqlRaw(Async)?|ExecuteSqlInterpolated(Async)?)\s*\(",
        RegexOptions.Compiled);

    /// <summary>
    /// GEREKÇELİ İSTİSNALAR — dosya bazında, her biri NİÇİN.
    /// 2026-09-15 ölçümü: 8 dosyada 12 kullanım.
    /// </summary>
    private static readonly Dictionary<string, string> Istisnalar = new()
    {
        ["backend/EnderunAI.Api/Controllers/UserManagementController.cs"] =
            "Kullanıcının rol / izin geçersiz kılma / veri kapsamı satırlarını TOPLUCA siler. " +
            "Bunlar bağlantı (join) satırlarıdır: kendi başlarına iş verisi taşımazlar ve " +
            "denetim izi ÜST kayıtta (kullanıcı) tutulur. Tek tek yüklemek yüzlerce satırı " +
            "belleğe çeker.",

        ["backend/EnderunAI.Api/Services/Secretariat/SecretariatService.cs"] =
            "Evrak kategorisi ağacında toplu güncelleme. Kategori satırı iş verisi değil " +
            "sınıflandırmadır; silme değil güncelleme yapar.",

        ["backend/EnderunAI.Api/Services/Projects/ProjectDeletionService.cs"] =
            "PROJE SİLME — bilinçli ham SQL: 16 bağımlı tablo sıralı silinir. " +
            "Yumuşak silme burada İSTENMİYOR; proje silme zaten kendi denetim kaydını " +
            "yazıyor (WriteAudit). Tablolar adıyla sayılıdır, açık uçlu değildir.",

        ["backend/EnderunAI.Api/Services/Portal/PortalLinkResolver.cs"] =
            "Süresi dolmuş portal bağlantılarının toplu temizliği. Satırlar zaten " +
            "ölü; denetim izi portal erişim kaydında tutuluyor.",

        ["backend/EnderunAI.Api/Services/Inventory/StokSatirKilidiService.cs"] =
            "`SELECT ... FOR UPDATE` — YAZMA DEĞİL, SATIR KİLİDİ. Ham SQL kullanılıyor " +
            "çünkü EF satır kilidi üretmiyor. Hiçbir sütunu değiştirmez, hiçbir satır " +
            "silmez; dolayısıyla damgalanacak ya da denetlenecek bir değişiklik yoktur.",

        ["backend/EnderunAI.Api/Services/Finance/OdemeSatirKilidiService.cs"] =
            "`SELECT ... FOR UPDATE` — YAZMA DEĞİL, SATIR KİLİDİ. Ham SQL kullanılıyor " +
            "çünkü EF satır kilidi üretmiyor. Hiçbir sütunu değiştirmez, hiçbir satır " +
            "silmez; dolayısıyla damgalanacak ya da denetlenecek bir değişiklik yoktur.",

        ["backend/EnderunAI.Api/Data/DatabaseSeeder.cs"] =
            "Tohumlama: kullanıcı-rol bağlantılarının yeniden kurulması. Uygulama " +
            "açılışında koşar, kullanıcı eylemi değildir; denetim aktörü zaten yok.",

        ["backend/EnderunAI.Api/Controllers/ProjectBoqController.cs"] =
            "İcmal (BOQ) kalemlerinin toplu silinmesi — kalemler her aktarımda yeniden " +
            "kurulur; üst kayıt (icmal) denetleniyor.",
    };

    private static IEnumerable<string> KaynakDosyalari()
    {
        var kok = Path.Combine(DepoKoku(), "backend", "EnderunAI.Api");
        foreach (var d in Directory.EnumerateFiles(kok, "*.cs", SearchOption.AllDirectories))
        {
            if (d.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                d.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            yield return d;
        }
    }

    [Fact]
    public void Tarama_BosaDusmuyor_POZITIF_KONTROL()
    {
        var dosyalar = KaynakDosyalari().ToList();

        Assert.True(dosyalar.Count > 500,
            $"Yalnız {dosyalar.Count} dosya tarandı — kapsam çökmüş olabilir.");

        // DEDEKTÖR KÖR MÜ: sentetik çağrılar YAKALANMALI.
        Assert.Matches(AtlayanYazma, "await db.Users.ExecuteUpdateAsync(x => x);");
        Assert.Matches(AtlayanYazma, "db.Database.ExecuteSqlRaw(\"delete from x\");");
        Assert.Matches(AtlayanYazma, ".ExecuteDeleteAsync(ct);");

        // MASUM SATIR YAKALANMAMALI (yanlış kırmızı, Kural 86).
        Assert.DoesNotMatch(AtlayanYazma, "await db.SaveChangesAsync(ct);");
        Assert.DoesNotMatch(AtlayanYazma, "db.Users.FromSqlRaw(\"select 1\");");
    }

    [Fact]
    public void HerIstisna_GerekceliOlmali()
    {
        foreach (var (yol, gerekce) in Istisnalar)
        {
            Assert.False(string.IsNullOrWhiteSpace(gerekce), $"Gerekçesiz istisna: {yol}");
            Assert.True(gerekce.Length > 80,
                $"Gerekçe fazla kısa — niçin güvenli olduğunu anlatmıyor: {yol}");
        }
    }

    [Fact]
    public void Istisnalar_HalaGecerliOlmali()
    {
        // ÖLÜ İSTİSNA LİSTEYİ YALANCI YAPAR: dosya taşınmış ya da
        // çağrı kaldırılmış olabilir.
        foreach (var yol in Istisnalar.Keys)
        {
            var tam = Path.Combine(DepoKoku(), yol);
            Assert.True(File.Exists(tam), $"İstisna dosyası yok, liste çürümüş: {yol}");

            var metin = File.ReadAllText(tam);
            Assert.True(AtlayanYazma.IsMatch(metin),
                $"İstisna artık gereksiz — dosyada atlayan yazma kalmamış, listeden ÇIKARIN: {yol}");
        }
    }

    [Fact]
    public void YeniAtlayanYazma_Eklenmemis()
    {
        var kok = DepoKoku();
        var bulgular = new List<string>();

        foreach (var dosya in KaynakDosyalari())
        {
            var goreli = Path.GetRelativePath(kok, dosya).Replace('\\', '/');
            if (Istisnalar.ContainsKey(goreli))
            {
                continue;
            }

            var satirlar = File.ReadAllLines(dosya);
            for (var i = 0; i < satirlar.Length; i++)
            {
                if (AtlayanYazma.IsMatch(satirlar[i]))
                {
                    bulgular.Add($"{goreli}:{i + 1}  {satirlar[i].Trim()}");
                }
            }
        }

        Assert.True(bulgular.Count == 0,
            "İZLEYİCİYİ ATLAYAN YENİ YAZMA:\n  " + string.Join("\n  ", bulgular) +
            "\n\nBu çağrılar SaveChanges'i hiç çağırmaz: UpdatedAtUtc yazılmaz, " +
            "denetim satırı oluşmaz, yumuşak silme devreye girmez.\n" +
            "İşlevsel olarak gerekliyse IzleyiciyiAtlayanYazmaTests.Istisnalar " +
            "listesine GEREKÇESİYLE ekleyin — gerekçe, niçin güvenli olduğunu anlatmalı.");
    }
}
