using System.Text.RegularExpressions;
using EnderunAI.Api.Services.Collaboration;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// YORUM BİLEŞENİNİN TAKILDIĞI HER EKRANIN VARLIK TİPİ,
/// ÇÖZÜMLEYİCİDE OLMALI.
///
/// NEDEN: bileşen `(varlık tipi + kayıt no)` ile takılıyor. Yeni bir
/// modül bileşeni takar ama tipi `EntityContextResolver`'a yazmazsa
/// iki kötü sonuçtan biri olur:
///   - Yorum sessizce çalışmaz (kullanıcı yazar, 404 alır, sebebini
///     anlamaz), ya da
///   - Daha kötüsü: çözümleyici gevşetilirse KAPSAMSIZ çalışır.
///
/// Bu bekçi birinciyi derleme zamanına yakın bir yere çeker: ekranda
/// takılı bir tip listede yoksa test düşer.
///
/// ÖNYÜZ TARAMASI: bileşen frontend'de `entityType="..."` ile
/// çağrılıyor. Kaynak taraması, "ekranda takılı" olmanın tek
/// güvenilir kanıtı — uçtan bakmak, hiç çağrılmayan bir tipi de
/// listeye sokardı.
/// </summary>
public sealed class CommentEntityTypeGuardTests
{
    /// <summary>
    /// GEREKÇELİ İSTİSNALAR — cırcırda kurduğumuz desen.
    /// Gerekçesiz istisna sessiz bir karardır.
    /// </summary>
    private static readonly Dictionary<string, string> Istisnalar = new()
    {
        // Şimdilik boş: bileşen henüz ekranlara takılmadı (M1/7).
        // Bir tip buraya girecekse SEBEBİ yazılmalı.
    };

    [Fact]
    public void EkranlardaKullanilanVarlikTipleri_CozumleyicideOlmali()
    {
        var kok = BulKok();
        var onyuz = Path.Combine(kok, "..", "frontend", "enderun-ai");

        if (!Directory.Exists(onyuz))
            return; // Ön yüz yoksa (yalnız backend derlemesi) sınanacak bir şey yok.

        var desen = new Regex(
            @"entityType\s*[:=]\s*[""']([A-Za-z]+)[""']",
            RegexOptions.Compiled);

        var eksikler = new List<string>();

        foreach (var dosya in Directory
                     .EnumerateFiles(onyuz, "*.tsx", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(onyuz, "*.ts", SearchOption.AllDirectories)))
        {
            if (dosya.Contains("/node_modules/") || dosya.Contains("/.next/")) continue;

            foreach (Match eslesme in desen.Matches(File.ReadAllText(dosya)))
            {
                var tip = eslesme.Groups[1].Value;

                if (Istisnalar.ContainsKey(tip)) continue;
                if (EntityContextResolver.SupportedTypes.Contains(tip)) continue;

                eksikler.Add($"{Path.GetFileName(dosya)}  ->  {tip}");
            }
        }

        Assert.True(
            eksikler.Count == 0,
            "YORUM BİLEŞENİ, ÇÖZÜMLEYİCİDE OLMAYAN BİR VARLIK TİPİYLE " +
            "TAKILMIŞ:\n  " + string.Join("\n  ", eksikler.Distinct()) +
            "\n\nEntityContextResolver.SupportedTypes listesine tipi ve " +
            "çözümleme sorgusunu ekleyin. Eklenmezse yorum o ekranda " +
            "çalışmaz; çözümleyici gevşetilirse kapsamsız çalışır.");
    }

    /// <summary>
    /// Desteklenen her tipin GERÇEKTEN çözümlenebildiği: listede olup
    /// switch'te karşılığı olmayan bir tip, "destekleniyor" görünüp
    /// çalışmazdı.
    /// </summary>
    [Fact]
    public void DesteklenenHerTip_CozumleyiciSwitchindeOlmali()
    {
        var kok = BulKok();
        var kaynak = File.ReadAllText(Path.Combine(
            kok, "EnderunAI.Api", "Services", "Collaboration",
            "EntityContextResolver.cs"));

        var eksikler = EntityContextResolver.SupportedTypes
            .Where(tip => !kaynak.Contains($"\"{tip}\", StringComparison.OrdinalIgnoreCase"))
            .ToList();

        Assert.True(
            eksikler.Count == 0,
            "Bu tipler SupportedTypes içinde ama çözümleme sorgusu yok:\n  " +
            string.Join("\n  ", eksikler));
    }

    [Fact]
    public void Istisnalar_GerekcesizOlamaz()
    {
        foreach (var (tip, gerekce) in Istisnalar)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(gerekce),
                $"{tip} istisna listesinde ama gerekçesi yok.");
        }
    }

    private static string BulKok()
    {
        var dizin = new DirectoryInfo(AppContext.BaseDirectory);

        while (dizin is not null &&
               !Directory.Exists(Path.Combine(dizin.FullName, "EnderunAI.Api")))
        {
            dizin = dizin.Parent;
        }

        return dizin?.FullName
            ?? throw new InvalidOperationException("Çözüm kökü bulunamadı.");
    }
}
