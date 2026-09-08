namespace EnderunAI.Api.Services.Upload;

/// <summary>
/// DOSYANIN GERÇEKTEN NE OLDUĞU — UZANTIYA VE İSTEMCİYE GÜVENMEDEN.
///
/// ═══ NEDEN VAR (MESAJ/3 C2) ═══
///
/// Mevcut yükleme yolu YALNIZ uzantıya bakıyordu (ölçüldü: kaynakta
/// içerik doğrulaması sıfır eşleşme). İstemcinin bildirdiği MIME de
/// istemcinin sözü — saldırgan onu istediği gibi yazar.
///
/// `kotu.exe` dosyasını `rapor.pdf` diye yeniden adlandırmak uzantı
/// kapısını geçmeye yeter. İçerik okunmadan hiçbir beyaz liste
/// gerçek koruma değildir.
///
/// ═══ NE YAPIYOR ═══
///
/// Dosyanın ilk baytlarını okuyup TÜRÜNÜ tanıyor ve uzantıyla
/// uyuşup uyuşmadığını söylüyor. Uyuşmuyorsa dosya reddedilir.
///
/// ═══ NEDEN "TANIMADIM" AYRI BİR CEVAP ═══
///
/// Üç sonuç var, üçü ayrı davranış (Kural 67):
///   Uyuyor    -> kabul
///   Uymuyor   -> RED (uzantı yalan söylüyor)
///   Tanımadım -> RED (beyaz listedeki her tür burada tanınmalı;
///                tanınmayan bir tür ya yeni eklendi ve imzası
///                yazılmadı, ya da dosya bozuk. İkisinde de
///                kapalı tarafa düşülür.)
///
/// Sessizce kabul etmek, kapının olmamasından kötüdür: kapı var
/// sanılır.
///
/// ═══ DÜZ METİN TÜRLERİ (txt, csv) ═══
///
/// Bunların sihirli baytı YOKTUR. Onlar için kural farklı: içerik
/// METİN OLMALI — yani ilk baytlarda NUL ve denetim karakteri
/// bulunmamalı. Bir `.exe` `.txt` adıyla gelirse ilk baytları
/// `MZ` ve bol NUL içerir, bu kapıya takılır.
/// </summary>
public static class DosyaIcerikDenetimi
{
    public enum Sonuc
    {
        Uyuyor,
        Uymuyor,
        Tanimadim
    }

    /// <summary>
    /// Uzantı → o uzantıda kabul edilen imzalar.
    ///
    /// docx/xlsx/pptx/odt/ods/zip HEPSİ ZIP kabıdır (`PK\x03\x04`);
    /// birbirinden ayırmak kabın içini açmayı gerektirir ve bu kapı
    /// onu YAPMIYOR. Bunu bilerek kabul ediyorum: `.docx` adıyla
    /// gelen bir `.zip` yine de bir zip'tir ve beyaz listede zip
    /// zaten var. Kapının işi çalıştırılabilir/sahte dosyayı
    /// elemek, ofis biçimlerini birbirinden ayırmak değil.
    /// </summary>
    private static readonly Dictionary<string, byte[][]> Imzalar =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = [[0x25, 0x50, 0x44, 0x46]],                 // %PDF
            [".jpg"] = [[0xFF, 0xD8, 0xFF]],
            [".jpeg"] = [[0xFF, 0xD8, 0xFF]],
            [".png"] = [[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]],
            [".gif"] = [[0x47, 0x49, 0x46, 0x38]],                 // GIF8
            [".webp"] = [[0x52, 0x49, 0x46, 0x46]],                // RIFF (+WEBP)
            [".zip"] = [[0x50, 0x4B, 0x03, 0x04], [0x50, 0x4B, 0x05, 0x06]],
            [".docx"] = [[0x50, 0x4B, 0x03, 0x04]],
            [".xlsx"] = [[0x50, 0x4B, 0x03, 0x04]],
            [".pptx"] = [[0x50, 0x4B, 0x03, 0x04]],
            [".odt"] = [[0x50, 0x4B, 0x03, 0x04]],
            [".ods"] = [[0x50, 0x4B, 0x03, 0x04]],
        };

    /// <summary>Sihirli baytı olmayan, metin olması beklenen türler.</summary>
    private static readonly HashSet<string> DuzMetinTurleri =
        new(StringComparer.OrdinalIgnoreCase) { ".txt", ".csv" };

    /// <summary>Okunacak bayt sayısı: en uzun imza 8 bayt; metin kontrolü için daha fazlası.</summary>
    public const int OkunacakBayt = 512;

    public static Sonuc Denetle(string uzanti, ReadOnlySpan<byte> bas)
    {
        if (bas.Length == 0) return Sonuc.Uymuyor;

        if (DuzMetinTurleri.Contains(uzanti))
            return MetinMi(bas) ? Sonuc.Uyuyor : Sonuc.Uymuyor;

        if (!Imzalar.TryGetValue(uzanti, out var adaylar))
            return Sonuc.Tanimadim;

        foreach (var imza in adaylar)
        {
            if (bas.Length < imza.Length) continue;
            if (bas[..imza.Length].SequenceEqual(imza)) return Sonuc.Uyuyor;
        }

        return Sonuc.Uymuyor;
    }

    /// <summary>
    /// İçerik düz metin mi.
    ///
    /// NUL baytı ve C0 denetim karakterleri (tab/CR/LF hariç) metin
    /// dosyasında bulunmaz; ikili dosyalarda bolca bulunur. UTF-8
    /// BOM ve Türkçe karakterler yüksek baytlar olarak geçer ve
    /// engellenmez.
    /// </summary>
    private static bool MetinMi(ReadOnlySpan<byte> bas)
    {
        foreach (var b in bas)
        {
            if (b == 0x00) return false;
            if (b < 0x20 && b != 0x09 && b != 0x0A && b != 0x0D) return false;
        }

        return true;
    }
}
