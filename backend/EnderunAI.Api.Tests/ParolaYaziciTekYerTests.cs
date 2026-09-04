using System.Text.RegularExpressions;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// PAROLA YAZMANIN TEK NOKTASI — BEKÇİ.
///
/// ── DOĞURAN OLAY (2026-09-04) ──
///
/// Parola değiştirmek üç şeyi BİRLİKTE yapmak demek: karma, damga
/// (`PasswordChangedAtUtc`) ve oturum önbelleği. Yönetici sıfırlama
/// yolu birincisini yapıyor, diğer ikisini YAPMIYORDU — ölçüldü.
///
/// Sonuç: yöneticinin parolasını sıfırladığı kullanıcının açık
/// oturumları yaşamaya devam ediyordu. Oysa sıfırlamanın kullanıldığı
/// senaryo tam olarak "parola başkasının elinde" senaryosudur;
/// oturum düşmezse sıfırlama işini yapmamış olur.
///
/// ── BU TESTİN İŞİ ──
///
/// Üç adımı AYIRAN yeni bir yol doğmasın. `ParolaYazici` dışında
/// kimse parola alanlarına yazmasın.
/// </summary>
public sealed class ParolaYaziciTekYerTests
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
    /// Parola alanlarına ATAMA: `user.PasswordHash = …`,
    /// `x.PasswordChangedAtUtc = …`.
    /// </summary>
    private static readonly Regex ParolaAtamasi = new(
        @"\.(PasswordHash|PasswordSalt|PasswordChangedAtUtc)\s*=(?!=)",
        RegexOptions.Compiled);

    /// <summary>
    /// Kuralın EVİ ve tohumlama. Tohumlayıcı ilk admini kurarken
    /// parolayı yazıyor; orada düşürülecek oturum yok ve
    /// `ParolaYazici` henüz bir kapsam (scope) içinde değil.
    /// </summary>
    private static readonly Dictionary<string, string> Muaf = new()
    {
        ["ParolaYazici.cs"] = "Kuralın evi: üç adımı birlikte yapan yer.",
        ["DatabaseSeeder.cs"] =
            "İlk kurulum: kullanıcı henüz yokken parola yazılıyor, " +
            "düşürülecek oturum da yok. Tohumlayıcı DI kapsamı dışında " +
            "çalışıyor.",
    };

    [Fact]
    public void Desen_BilinenOrnekleri_Yakaliyor_POZITIF_KONTROL()
    {
        /*
         * KURAL 48: desen bozulursa aşağıdaki test sessizce yeşile
         * düşerdi. Bilinen örneklerle sınanıyor — kaynağa bakmıyor,
         * çünkü kaynak yarın değişebilir.
         */
        Assert.Matches(ParolaAtamasi, "user.PasswordHash = karma.Hash;");
        Assert.Matches(ParolaAtamasi, "user.PasswordChangedAtUtc = simdi;");
        Assert.Matches(ParolaAtamasi, "x.PasswordSalt = p.Salt;");

        // KARŞILAŞTIRMA ATAMA DEĞİL: yakalanmamalı.
        Assert.DoesNotMatch(
            ParolaAtamasi, "if (user.PasswordHash == beklenen)");
    }

    [Fact]
    public void Parola_Alanlarina_YALNIZ_ParolaYazici_Yaziyor()
    {
        var kok = Kok();
        var bulgular = new List<string>();

        foreach (var dosya in Directory.EnumerateFiles(
                     kok, "*.cs", SearchOption.AllDirectories))
        {
            if (dosya.Contains("/obj/") || dosya.Contains("/bin/")) continue;
            if (dosya.Contains("/Migrations/")) continue;

            var ad = Path.GetFileName(dosya);
            if (Muaf.ContainsKey(ad)) continue;

            var satirlar = File.ReadAllLines(dosya);

            for (var i = 0; i < satirlar.Length; i++)
            {
                var s = satirlar[i].Trim();

                // Yorumlar serbest: kuralı ANLATMAK, yeniden YAZMAK
                // değildir.
                if (s.StartsWith("//") || s.StartsWith("*") || s.StartsWith("/*"))
                    continue;

                if (ParolaAtamasi.IsMatch(s))
                    bulgular.Add($"{Path.GetRelativePath(kok, dosya)}:{i + 1}  ->  {s}");
            }
        }

        Assert.True(
            bulgular.Count == 0,
            "PAROLA ALANINA `ParolaYazici` DIŞINDAN YAZILIYOR:\n  " +
            string.Join("\n  ", bulgular) +
            "\n\nParola değiştirmek ÜÇ şeyi birlikte yapmak demek: karma, " +
            "`PasswordChangedAtUtc` damgası ve oturum önbelleği. Üçünü " +
            "ayıran her yol, oturum düşürmeyi SESSİZCE kapatır.\n\n" +
            "Bunun gerçekleştiği ölçüldü: yönetici sıfırlama yolu karmayı " +
            "yazıyor, damgayı yazmıyordu — sıfırlanan kullanıcının " +
            "oturumları yaşamaya devam ediyordu.\n\n" +
            "`IParolaYazici.Uygula(...)` kullanın.");
    }

    [Fact]
    public void HerMuafiyet_Gerekceli()
    {
        // Gerekçesiz istisna sessiz bir karardır.
        foreach (var (ad, gerekce) in Muaf)
            Assert.False(string.IsNullOrWhiteSpace(gerekce), $"Gerekçesiz: {ad}");
    }
}
