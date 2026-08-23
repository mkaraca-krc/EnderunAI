using System.Text.RegularExpressions;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// KAYNAK KODDA SIR ARAYAN BEKÇİ.
///
/// NEDEN VAR: 2026-08-23'te bu paketi yazarken, canlıda O SIRADA
/// GEÇERLİ olan portal tokenını test verisi olarak
/// SensitivePathMaskingTests içine yazdım. Commit edildi ve push
/// edildi. Yani bu paketin bütün konusu olan hatayı — sırrın düz
/// metin bir yere yazılması — testin kendisi tekrarlıyordu.
///
/// Kendim buldum ve düzelttim, ama bir kez olan bir daha olur.
/// Git geçmişine dokunulmadı (geçmişi yeniden yazmak repoyu bozar);
/// bunun yerine tekrarını engelleyen bir bekçi kondu.
///
/// NE ARIYOR: 32 baytlık URL-safe base64 biçimine uyan dizgiler —
/// portal tokenının biçimi. 43 karakter, base64 alfabesi, hem harf
/// hem rakam içeriyor.
///
/// UYDURMA TEST VERİSİ NASIL AYIRT EDİLİYOR: "TEST-" önekiyle
/// başlıyor. Gerçek token bu önekle başlayamaz çünkü
/// RandomNumberGenerator çıktısının 43 karakterinin tam olarak bu
/// beş karakterle başlaması pratikte imkânsız — ve önek elle
/// konuyor, rastlantıya bırakılmıyor.
/// </summary>
public sealed class SecretInSourceGuardTests
{
    /// <summary>
    /// GEREKÇELİ İSTİSNALAR. Gerekçesiz istisna sessiz bir karardır.
    /// </summary>
    private static readonly Dictionary<string, string> Istisnalar = new()
    {
        ["SecretInSourceGuardTests.cs"] =
            "Bekçinin kendisi: desenleri ve örnekleri içeriyor.",
    };

    /*
     * 32 bayt -> base64 -> 43 karakter (sondaki '=' atılmış hâli).
     * URL-safe alfabe: A-Z a-z 0-9 - _
     *
     * Kelime sınırı (\b yerine) elle kontrol ediliyor: '-' ve '_'
     * karakterleri \b semantiğini bozuyor.
     */
    /*
     * YALNIZ DİZGİ SABİTLERİ TARANIYOR.
     *
     * İlk sürüm satırın tamamına bakıyordu ve uzun metot adlarını
     * ("Post_WritesDifferenceTo646_AndKeepsForeignBalance") sır sandı.
     * Bir sır KODA HER ZAMAN dizgi sabiti olarak girer; tanımlayıcı
     * adı olarak değil. Tırnak içine bakmak yanlış alarmların
     * neredeyse tamamını eliyor.
     */
    private static readonly Regex DizgiSabiti = new(
        "\"([A-Za-z0-9_\\-]{43,})\"",
        RegexOptions.Compiled);

    [Fact]
    public void KaynakKodda_TokenBicimindeDizgiOlmamali()
    {
        var kok = BulKok();
        var bulgular = new List<string>();

        foreach (var dizin in new[] { "EnderunAI.Api", "EnderunAI.Api.Tests" })
        {
            var tam = Path.Combine(kok, dizin);
            if (!Directory.Exists(tam)) continue;

            foreach (var dosya in Directory.EnumerateFiles(tam, "*.cs", SearchOption.AllDirectories))
            {
                if (dosya.Contains("/obj/") || dosya.Contains("/bin/")) continue;

                /*
                 * MIGRATIONS ATLANIYOR — GEREKÇE:
                 *
                 * EF'in ürettiği migration adları
                 * ("20260804132300_NetEsasliUcretKartiVeCalismaSaati")
                 * 43 karakteri aşıyor ve base64 alfabesine uyuyor;
                 * hepsi yanlış alarm veriyordu. Bu dosyalar elle
                 * yazılmıyor, sır konacak yer değil.
                 *
                 * Migration'ın Sql(...) gövdesine elle sır yazılması
                 * teorik olarak mümkün ama o da veritabanına düz metin
                 * yazmak demek — bu bekçinin değil, kod incelemesinin
                 * konusu.
                 */
                if (dosya.Contains("/Migrations/")) continue;

                var ad = Path.GetFileName(dosya);
                if (Istisnalar.ContainsKey(ad)) continue;

                var satirlar = File.ReadAllLines(dosya);

                for (var i = 0; i < satirlar.Length; i++)
                {
                    foreach (Match eslesme in DizgiSabiti.Matches(satirlar[i]))
                    {
                        var deger = eslesme.Groups[1].Value;

                        /*
                         * UYDURMA TEST VERİSİ: AÇIK ÖNEKLE İŞARETLİ.
                         *
                         * Büyük/küçük harf duyarsız, çünkü mevcut test
                         * altyapısı "test-only-jwt-secret-..." biçimini
                         * kullanıyor ve o da aynı sözü veriyor: bu
                         * değer uydurmadır, canlıda karşılığı yoktur.
                         *
                         * Önek RASTLANTIYA BIRAKILMIYOR, elle konuyor:
                         * gerçek bir tokenın bu beş karakterle
                         * başlaması pratikte imkânsız.
                         */
                        if (deger.StartsWith("TEST-", StringComparison.OrdinalIgnoreCase))
                            continue;

                        // Yalnız harf ya da yalnız rakam olan dizgiler
                        // token değildir: uzun tip adları, hex özetler,
                        // yorum içindeki kesintisiz metinler.
                        var harfVar = deger.Any(char.IsLetter);
                        var rakamVar = deger.Any(char.IsDigit);
                        if (!harfVar || !rakamVar) continue;

                        // Hex özet (yalnız 0-9a-f) token değil.
                        if (deger.All(c => char.IsDigit(c) || (c >= 'a' && c <= 'f')))
                            continue;

                        // GÖMÜLÜ DOSYA VERİSİ: testlerde 1x1 PNG gibi
                        // küçük ikili veriler base64 olarak duruyor.
                        // Sır değil; imzasından tanınıyor.
                        if (deger.StartsWith("iVBORw0KGgo", StringComparison.Ordinal) ||
                            deger.StartsWith("JVBERi0", StringComparison.Ordinal) ||
                            deger.StartsWith("data:", StringComparison.Ordinal))
                            continue;

                        bulgular.Add(
                            $"{Path.GetRelativePath(kok, dosya)}:{i + 1}  ->  {deger}");
                    }
                }
            }
        }

        Assert.True(
            bulgular.Count == 0,
            "KAYNAK KODDA SIR BİÇİMİNDE DİZGİ BULUNDU:\n  " +
            string.Join("\n  ", bulgular) +
            "\n\nGerçek bir token, anahtar ya da parola kaynağa " +
            "yazılmamalı: commit edilir, push edilir ve git geçmişinden " +
            "silinemez. Test verisi gerekiyorsa \"TEST-\" önekiyle " +
            "UYDURMA bir değer üretin — biçimin doğru olması yeter.");
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
