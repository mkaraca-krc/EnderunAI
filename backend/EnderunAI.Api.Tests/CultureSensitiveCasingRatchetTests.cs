using System.Text.RegularExpressions;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// KÜLTÜRE BAĞLI BÜYÜTME/KÜÇÜLTME — CIRCIR (yalnız aşağı iner).
///
/// Türkçe kültürde "I" harfi noktasız "ı"ya döner. Bu kod tabanında tam
/// olarak bu tuzağa düşüldü ve iki farklı yerde kullanıcıyı engelledi:
/// arama seçicisinde "SCHNEIDER" bulunamıyordu, proje silme onay
/// kodunda doğru kod yazıldığı hâlde eşleşmiyordu.
///
/// SUNUCU BUGÜN DOĞRU ÇALIŞIYOR — AMA TESADÜFEN: konteyner temel
/// imajının dil ayarı C.UTF-8 (ölçüldü) ve EF sorguları küçültmeyi
/// PostgreSQL'e çeviriyor. Yani doğruluk, kimsenin bakmadığı bir ortam
/// değişkenine bağlı. İmaj değişirse arama SESSİZCE bozulur.
///
/// NEDEN UYARI YETMİYOR: CA1311 açık ama uyarı seviyesinde ve derleme
/// yüzlerce uyarı üretiyor; yenisi aralarında görünmez. Bu test sayıyı
/// tavan olarak tutuyor — yeni bir çağrı eklendiğinde DÜŞER.
///
/// MEVCUT ÇAĞRILARIN ÇEVRİLMESİ G2 PAKETİNDE. O iş ilerledikçe
/// buradaki tavan da düşürülecek; asla yükseltilmeyecek.
/// </summary>
public sealed class CultureSensitiveCasingRatchetTests
{
    /// <summary>
    /// Ölçülen mevcut çağrı sayısı: 91 (2026-08-22, yorumlar hariç). YALNIZ AŞAĞI İNER.
    /// Yükseltmek, kapatılan bir tuzağı geri açmaktır.
    /// </summary>
    private const int UstSinir = 91;

    [Fact]
    public void KultureBagliCagriSayisi_ArtmamisOlmali()
    {
        var kok = BulProjeKoku();
        var desen = new Regex(@"\.To(Lower|Upper)\(\)", RegexOptions.Compiled);

        var sayim = 0;
        var dosyalar = Directory.EnumerateFiles(
            Path.Combine(kok, "EnderunAI.Api"), "*.cs", SearchOption.AllDirectories);

        foreach (var dosya in dosyalar)
        {
            // Üretilmiş dosyalar sayılmıyor: migration'lar ve tasarım
            // dosyaları elle yazılmıyor.
            if (dosya.Contains("/obj/") || dosya.Contains("/bin/")) continue;

            // YORUMLAR SAYILMIYOR. Bu dosyalarda kuralın kendisi
            // anlatılırken "ToLower()" yazısı geçiyor; sayılsaydı tavan
            // şişer ve araya gerçek bir çağrı sızabilirdi (yorum
            // silinince yer açılırdı).
            var kod = YorumlariAt(File.ReadAllText(dosya));

            foreach (Match _ in desen.Matches(kod))
                sayim++;
        }

        Assert.True(
            sayim <= UstSinir,
            $"Kültüre bağlı ToLower()/ToUpper() sayısı {sayim}, tavan {UstSinir}. " +
            "Yeni çağrı eklenmiş: karşılaştırma/arama amaçlıysa " +
            "ToLowerInvariant()/ToUpperInvariant() kullanın. Gösterim " +
            "amaçlıysa kültürü AÇIKÇA yazın (CultureInfo.CurrentCulture).");

        // Tavan gerçeğin ÇOK üstünde kalırsa cırcır işlevini yitirir:
        // araya sessizce yenisi eklenebilir. G2 sayıyı düşürdükçe bu
        // sabit de güncellenmeli.
        Assert.True(
            sayim >= UstSinir - 5,
            $"Sayı {sayim}, tavan {UstSinir}. Tavanı {sayim} yapın ki " +
            "cırcır dişlerini kaybetmesin.");
    }

    /// <summary>Satır ve blok yorumlarını atar — sayım yalnız KODU görsün.</summary>
    private static string YorumlariAt(string kaynak)
    {
        var bloksuz = Regex.Replace(kaynak, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(bloksuz, @"//[^\n]*", string.Empty);
    }

    private static string BulProjeKoku()
    {
        var dizin = new DirectoryInfo(AppContext.BaseDirectory);

        while (dizin is not null &&
               !Directory.Exists(Path.Combine(dizin.FullName, "EnderunAI.Api")))
        {
            dizin = dizin.Parent;
        }

        Assert.NotNull(dizin);
        return dizin!.FullName;
    }
}
