using System.Text.RegularExpressions;
using EnderunAI.Api.Security;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// PAROLA POLİTİKASI TEK YERDEN ÇAĞRILIR — İKİNCİ KOPYA DOĞMASIN.
///
/// ── DOĞURAN OLAY (2026-09-03) ──
///
/// Asgari uzunluk kuralı `UserManagementController` içinde İKİ AYRI
/// YERDE, elle yazılmış sabitle duruyordu: biri kullanıcı
/// OLUŞTURMADA, biri parola SIFIRLAMADA. İkisi de `< 10` diyordu.
///
/// Kuralı tek kaynağa çekerken ilk kopyayı değiştirdim ve "bağlandı"
/// dedim. İKİNCİ KOPYAYI ancak ölçünce buldum — yani hata, tam da
/// onu ortadan kaldırırken karşıma çıktı.
///
/// Bu tesadüf değil: bu kod tabanının en sık hatası ikinci kopya.
/// Aynı gün üç kez daha görüldü — merkez kuralının PUT kopyası,
/// `dotnet ef` çağrısının üç ayrı ortamı, sır bekçisinin taranmayan
/// yüzeyi.
///
/// ── NE SINANIYOR ──
///
/// Politikanın DOĞRU olması `ParolaDegistirmeTests`'te. Burada
/// sınanan, politikanın BAŞKA BİR YERDE YENİDEN YAZILMADIĞI.
/// </summary>
public sealed class ParolaPolitikasiTekYerTests
{
    private static string Kok()
    {
        var dizin = new DirectoryInfo(AppContext.BaseDirectory);

        while (dizin is not null &&
               !Directory.Exists(Path.Combine(dizin.FullName, "EnderunAI.Api")))
        {
            dizin = dizin.Parent;
        }

        Assert.True(dizin is not null, "Çözüm kökü bulunamadı.");
        return Path.Combine(dizin!.FullName, "EnderunAI.Api");
    }

    /// <summary>
    /// Parola uzunluğunu ELLE karşılaştıran desen:
    /// `parola.Length < 10`, `password.Length >= 8` gibi.
    ///
    /// `ParolaPolitikasi`'nın kendisi muaf — kural orada YAŞIYOR.
    /// </summary>
    private static readonly Regex ElleUzunlukKontrolu = new(
        @"(?i)(password|parola|sifre|şifre)\w*\.Length\s*[<>=!]+\s*\d+",
        RegexOptions.Compiled);

    [Fact]
    public void Desen_BilinenKotuOrnegi_Yakaliyor_POZITIF_KONTROL()
    {
        /*
         * ═══ POZİTİF KONTROLÜN İLK HÂLİ YANLIŞTI ═══
         *
         * Önce deseni `ParolaPolitikasi.cs`in KENDİSİNDE arıyordum ve
         * test kırmızı verdi. Sebep desenin bozuk olması değildi:
         * oradaki gerçek satır `parola.Length < AsgariUzunluk` — yani
         * SABİTE bakıyor, sayıya değil. Muhafızın avladığı şey ise
         * ELLE YAZILMIŞ SAYI; sabite başvuran biçim meşru.
         *
         * Yani kırmızı, muhafızı değil BENİ düzeltti: pozitif kontrol
         * kaynağa değil, BİLİNEN KÖTÜ ÖRNEĞE bakmalı.
         *
         * Desen bozulursa bu test kırmızı verir ve aşağıdaki "ikinci
         * kopya yok" testinin sessizce boşa düşmesi engellenir
         * (Kural 48).
         */
        Assert.Matches(ElleUzunlukKontrolu, "if (parola.Length < 10)");
        Assert.Matches(ElleUzunlukKontrolu, "if (password.Length >= 8)");
        Assert.Matches(ElleUzunlukKontrolu, "temporaryPassword.Length < 10");

        // SABİTE BAŞVURAN BİÇİM YAKALANMAMALI: o, kuralı yeniden
        // yazmak değil, tek kaynağı KULLANMAKTIR.
        Assert.DoesNotMatch(
            ElleUzunlukKontrolu, "if (parola.Length < AsgariUzunluk)");
    }

    [Fact]
    public void Uzunluk_Kurali_YALNIZ_ParolaPolitikasi_Icinde()
    {
        var kok = Kok();
        var bulgular = new List<string>();

        foreach (var dosya in Directory.EnumerateFiles(
                     kok, "*.cs", SearchOption.AllDirectories))
        {
            if (dosya.Contains("/obj/") || dosya.Contains("/bin/")) continue;

            // KURALIN EVİ — burada olması gereken tam da bu.
            if (Path.GetFileName(dosya) == "ParolaPolitikasi.cs") continue;

            var satirlar = File.ReadAllLines(dosya);

            for (var i = 0; i < satirlar.Length; i++)
            {
                var s = satirlar[i].Trim();

                // Yorum satırları serbest: kuralı ANLATMAK, kuralı
                // yeniden YAZMAK değildir. (Bu ayrımı bugün iki kez
                // öğrendik: hatayı açıklayan yorum, hatanın testini
                // boşa çıkarıyordu.)
                if (s.StartsWith("//") || s.StartsWith("*") || s.StartsWith("/*"))
                    continue;

                if (ElleUzunlukKontrolu.IsMatch(s))
                {
                    bulgular.Add(
                        $"{Path.GetRelativePath(kok, dosya)}:{i + 1}  ->  {s}");
                }
            }
        }

        Assert.True(
            bulgular.Count == 0,
            "PAROLA UZUNLUK KURALININ İKİNCİ KOPYASI BULUNDU:\n  " +
            string.Join("\n  ", bulgular) +
            "\n\nKural `ParolaPolitikasi` içinde yaşıyor; başka yerde " +
            "yeniden yazılmamalı. İki kopya zamanla ayrışır: biri " +
            $"{ParolaPolitikasi.AsgariUzunluk}'ye çıkarılır, diğeri eski " +
            "değerde kalır ve zayıf parola O YOLDAN girilebilir.\n\n" +
            "Bunun gerçekleştiği ölçüldü: kural iki yerde duruyordu ve " +
            "ikinci kopya, tek kaynağa çekme işi sırasında bulundu.");
    }

    [Fact]
    public void Politika_Her_Iki_Yazma_Yolundan_Cagriliyor()
    {
        /*
         * "İkinci kopya yok" tek başına yetmez: kural hiç ÇAĞRILMASA
         * da o test yeşil kalırdı. Bu, çağrının varlığını sabitliyor.
         *
         * İki yazma yolu: kendi parolasını değiştirme (AuthController)
         * ve yönetici sıfırlaması + kullanıcı oluşturma
         * (UserManagementController).
         */
        var kok = Kok();

        var auth = File.ReadAllText(
            Path.Combine(kok, "Controllers", "AuthController.cs"));
        var yonetim = File.ReadAllText(
            Path.Combine(kok, "Controllers", "UserManagementController.cs"));

        Assert.Contains("ParolaPolitikasi.Dogrula", auth);

        // İKİ çağrı: oluşturma ve sıfırlama. Biri silinirse o yoldan
        // politika uygulanmaz.
        var yonetimSayisi = Regex.Matches(
            yonetim, Regex.Escape("ParolaPolitikasi.Dogrula")).Count;

        Assert.True(
            yonetimSayisi >= 2,
            $"UserManagementController'da politika {yonetimSayisi} yerde " +
            "çağrılıyor; iki yazma yolu (oluşturma ve sıfırlama) için " +
            "en az iki çağrı olmalı.");
    }
}
