using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// YEDEK BETİĞİ NÖBETİ — ŞİFRESİZ YEDEK DİSKE DÜŞMESİN.
///
/// 2026-08-25: yedek dizininde 2 Ağustos'tan beri birikmiş 532 DÜZ
/// veritabanı yedeği bulundu. İçlerinde aynı gün tablodan, kayıttan ve
/// günlükten temizlenen token açık metin duruyordu. Diskteki düz kopya,
/// temizliğin tamamını anlamsız kılıyor.
///
/// Betik o gün akışta şifrelemeye çevrildi: pg_dump çıktısı doğrudan
/// gpg'ye akıyor, düz hali diske HİÇ düşmüyor. Önce yazıp sonra
/// şifrelemek arada bir pencere bırakıyordu; süreç o pencerede ölürse
/// düz dump orada KALIYORDU.
///
/// BU TESTİN İŞİ: betiğin o eski desene geri dönmesini yakalamak.
/// Kabuk betiği testsiz bir yüzey — bir "geçici olarak şifrelemeyi
/// kapatalım" düzenlemesi hiçbir yerde kırmızıya dönmezdi.
/// </summary>
public sealed class BackupScriptGuardTests
{
    private static string BetigiOku()
    {
        var dizin = new DirectoryInfo(AppContext.BaseDirectory);

        while (dizin is not null &&
               !Directory.Exists(Path.Combine(dizin.FullName, "scripts")))
        {
            dizin = dizin.Parent;
        }

        Assert.True(dizin is not null, "Repo kökü bulunamadı.");

        var yol = Path.Combine(dizin!.FullName, "scripts", "enderun-backup.sh");
        Assert.True(File.Exists(yol), $"Yedek betiği yok: {yol}");

        return File.ReadAllText(yol);
    }

    /// <summary>
    /// Satır devamlarını (ters bölü) BİRLEŞTİRİR, sonra yorumları atar.
    ///
    /// Birleştirmeden bakmak yanıltıyor: `pg_dump ... \` satırında boru
    /// görünmüyor, boru bir alt satırda. Satır satır bakan bir nöbetçi
    /// "boruya akmıyor" diye yanlış alarm verirdi.
    /// </summary>
    private static List<string> KomutSatirlari(string betik)
    {
        var birlesik = betik.Replace("\\\n", " ");

        return birlesik.Split('\n')
            .Where(x => !x.TrimStart().StartsWith('#') && x.Trim().Length > 0)
            .ToList();
    }

    /// <summary>
    /// pg_dump çıktısı DOSYAYA değil BORUYA yazmalı.
    ///
    /// `-f "$DB_BACKUP_FILE"` düz dump'ı diske yazan eski desendi.
    /// </summary>
    [Fact]
    public void PgDump_DosyayaYazmaz_BoruyaAkar()
    {
        var satirlar = KomutSatirlari(BetigiOku());

        Assert.DoesNotContain(satirlar, x => x.Contains("pg_dump") && x.Contains(" -f "));
        Assert.Contains(satirlar, x => x.Contains("pg_dump") && x.Contains('|'));
    }

    /// <summary>
    /// tar çıktısı da boruya akmalı: `-czf -`, `-czf dosya` değil.
    /// </summary>
    [Fact]
    public void Tar_DosyayaYazmaz_BoruyaAkar()
    {
        var satirlar = KomutSatirlari(BetigiOku());

        Assert.Contains(satirlar, x => x.Contains("tar -czf -"));
        Assert.DoesNotContain(satirlar, x => x.Contains("tar -czf \"$"));
    }

    /// <summary>
    /// ANAHTAR YOKSA BETİK DURMALI.
    ///
    /// Eski davranış "anahtar yoksa yedeği yine al, düz bırak, ERROR
    /// yaz" idi. Karar 2026-08-25'te değişti. Bu test o geri dönüşü
    /// yakalıyor: `fail` çağrısı olmadan geçemez.
    /// </summary>
    [Fact]
    public void AnahtarYoksa_YedekAlinmaz()
    {
        var betik = BetigiOku();
        var satirlar = KomutSatirlari(betik);

        var anahtarKontrolu = satirlar.FirstOrDefault(
            x => x.Contains("BACKUP_KEY_FILE") && x.Contains("-s "));

        Assert.NotNull(anahtarKontrolu);
        Assert.Contains("fail", anahtarKontrolu);

        // "düz bırakıldı" diye devam eden eski yol geri gelmemeli.
        Assert.DoesNotContain(satirlar, x => x.Contains("DÜZ bırakıldı"));
    }

    /// <summary>
    /// Yazılan her şifreli dosya AÇILDIĞI DOĞRULANMADAN kabul edilmemeli.
    ///
    /// Bu dizinde "BOZUK-YARIM_db_20260814" adlı bir dosya duruyor:
    /// doğrulanmamış yedeğin ne demek olduğunun kanıtı.
    /// </summary>
    [Fact]
    public void HerYedek_AcildigiDogrulanir()
    {
        var satirlar = KomutSatirlari(BetigiOku());

        Assert.Contains(satirlar, x => x.Contains("dogrula()"));
        Assert.True(
            satirlar.Count(x => x.TrimStart().StartsWith("dogrula ")) >= 1,
            "dogrula çağrısı yok.");
    }

    /// <summary>
    /// BORUNUN İKİ UCU DA KONTROL EDİLMELİ.
    ///
    /// pg_dump yarıda ölse bile gpg geçerli bir .gpg üretir — içinde
    /// YARIM bir dump'la. Tek başına `pipefail` hangi ucun düştüğünü
    /// söylemiyor; PIPESTATUS söylüyor.
    /// </summary>
    [Fact]
    public void BorununIkiUcuDaKontrolEdilir()
    {
        var satirlar = KomutSatirlari(BetigiOku());

        /*
         * KOMUT SATIRLARINA BAKILIYOR, METNİN TAMAMINA DEĞİL.
         *
         * İlk yazılışı `betik.Contains("PIPESTATUS")` idi ve SONDAYI
         * GEÇTİ: atamalar `DURUM=(0 0)` ile etkisizleştirildiği hâlde
         * kelime YORUMLARDA yaşamaya devam ettiği için test yeşil
         * kaldı. Kelimenin varlığı, denetimin çalıştığını kanıtlamaz.
         *
         * Şimdi aranan şey atamanın KENDİSİ: PIPESTATUS bir diziye
         * kopyalanıyor ve o dizinin iki elemanı da sıfıra karşı
         * sınanıyor.
         */
        var atamalar = satirlar
            .Where(x => x.Contains("PIPESTATUS") && x.Contains('='))
            .ToList();

        Assert.True(
            atamalar.Count >= 2,
            $"PIPESTATUS ataması komut satırlarında bulunamadı (bulunan: {atamalar.Count}). "
            + "Borunun iki ucu da kontrol edilmiyor olabilir.");

        /*
         * İKİ UCUN DA sınandığı satırlar sayılıyor.
         *
         * Önce `-ne 0 ]` geçen SATIR sayısı sayılıyordu ve eşik yanlış
         * konmuştu: iki uç AYNI satırda sınanıyor, dolayısıyla iki
         * denetim iki satır ediyor, dört değil. Ölçmeden eşik koymanın
         * bedeli.
         */
        var ikiUcSinanan = satirlar
            .Where(x => x.Contains("[0]") && x.Contains("[1]") && x.Contains("-ne 0"))
            .ToList();

        Assert.True(
            ikiUcSinanan.Count >= 2,
            $"Borunun iki ucunu birden sınayan koşul sayısı: {ikiUcSinanan.Count} (en az 2 bekleniyor: "
            + "veritabanı dökümü ve klasör arşivi).");

        Assert.Contains(satirlar, x => x.Contains("pipefail"));
    }
}
