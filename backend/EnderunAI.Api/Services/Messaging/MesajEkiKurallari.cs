namespace EnderunAI.Api.Services.Messaging;

/// <summary>
/// MESAJ EKİ KURALLARI — SINIRLAR VE BEYAZ LİSTE TEK YERDE (MESAJ/3 C1-C3).
///
/// ═══ NEDEN AYRI SINIF ═══
///
/// Aynı sınır hem istemcide hem SUNUCUDA uygulanacak (C1). Sunucu
/// tarafındaki tek kaynak burası; istemci kendi kopyasını taşıyor ve
/// bir test ikisinin AYNI kaldığını tutuyor. İki liste ayrışırsa
/// kullanıcı "yükleniyor" görüp sonra reddedilir — en can sıkıcı hata
/// biçimi.
///
/// ═══ SINIRLAR (Mehmet'in kararı) ═══
///
///   dosya başına en fazla  20 MB
///   mesaj başına en fazla   5 dosya
///
/// ═══ BEYAZ LİSTE — DIŞI REDDEDİLİR ═══
///
/// Belge (pdf, docx, xlsx, pptx, odt, ods), görsel (jpg, png, webp,
/// gif), metin (txt, csv), arşiv (zip).
///
/// ═══ ÇİFT UZANTI (C3) ═══
///
/// `rapor.pdf.exe` gibi adlar `Path.GetExtension` ile SON uzantıyı
/// verir (`.exe`) ve beyaz listeye takılır. Bu bir varsayım değil,
/// test edilmiş bir davranış — `MesajEkiKurallariTests` içinde
/// açıkça sınanıyor.
///
/// Ayrıca yasaklı uzantılar AÇIKÇA da listeleniyor. Gereksiz gibi
/// duruyor ama değil: beyaz liste bir gün genişletilirse bu liste
/// ikinci savunma hattı olarak kalır ve niyeti okunur kılar.
/// </summary>
public static class MesajEkiKurallari
{
    public const long DosyaBasinaEnFazlaBayt = 20L * 1024 * 1024;
    public const int MesajBasinaEnFazlaDosya = 5;

    /// <summary>`IUploadService` kategorisi — diskteki alt dizin.</summary>
    public const string Kategori = "mesaj-ekleri";

    /// <summary>`Attachment.EntityType` değeri.</summary>
    public const string VarlikTuru = "Message";

    public static readonly IReadOnlySet<string> IzinliUzantilar =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".docx", ".xlsx", ".pptx", ".odt", ".ods",
            ".jpg", ".jpeg", ".png", ".webp", ".gif",
            ".txt", ".csv",
            ".zip"
        };

    /// <summary>
    /// AÇIKÇA YASAKLI — beyaz liste zaten kapsıyor, bu ikinci hat.
    /// </summary>
    public static readonly IReadOnlySet<string> YasakliUzantilar =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".js", ".bat", ".cmd", ".sh",
            ".ps1", ".msi", ".scr", ".jar"
        };

    public enum Karar
    {
        Kabul,
        AdBos,
        YolIceriyor,
        UzantiYasakli,
        UzantiIzinsiz,
        CokBuyuk
    }

    /// <summary>
    /// Dosya adı ve boyutu kabul edilebilir mi.
    ///
    /// İÇERİK DENETİMİ BURADA DEĞİL: o `DosyaIcerikDenetimi` işi ve
    /// dosyanın baytlarını gerektiriyor. Bu sınıf yalnız ad ve boyut
    /// bakıyor; ikisi AYRI kapı ve ikisi de geçilmeden dosya kabul
    /// edilmiyor.
    /// </summary>
    public static Karar Denetle(string? dosyaAdi, long boyut)
    {
        if (string.IsNullOrWhiteSpace(dosyaAdi)) return Karar.AdBos;

        /*
         * YOL İÇEREN AD REDDEDİLİYOR (C4).
         *
         * Kullanıcının verdiği ad YALNIZCA gösterim alanı; diskteki ad
         * sunucunun ürettiği GUID. Yine de yol içeren bir ad
         * reddediliyor: "nasılsa kullanmıyoruz" bir savunma değildir,
         * yarın onu kullanan bir kod yolu açılabilir.
         */
        if (dosyaAdi.Contains('/') || dosyaAdi.Contains('\\') ||
            dosyaAdi.Contains("..", StringComparison.Ordinal))
        {
            return Karar.YolIceriyor;
        }

        if (boyut <= 0 || boyut > DosyaBasinaEnFazlaBayt) return Karar.CokBuyuk;

        var uzanti = Path.GetExtension(dosyaAdi);

        if (string.IsNullOrEmpty(uzanti)) return Karar.UzantiIzinsiz;
        if (YasakliUzantilar.Contains(uzanti)) return Karar.UzantiYasakli;
        if (!IzinliUzantilar.Contains(uzanti)) return Karar.UzantiIzinsiz;

        return Karar.Kabul;
    }

    /// <summary>Kullanıcıya gösterilecek, sebebi SÖYLEYEN mesaj.</summary>
    public static string Mesaj(Karar karar) => karar switch
    {
        Karar.Kabul => "Kabul edildi.",
        Karar.AdBos => "Dosya adı boş.",
        Karar.YolIceriyor => "Dosya adı yol bilgisi içeremez.",
        Karar.UzantiYasakli =>
            "Bu dosya türü güvenlik nedeniyle kabul edilmiyor.",
        Karar.UzantiIzinsiz =>
            "Yalnız belge, görsel, metin ve zip dosyaları yüklenebilir.",
        Karar.CokBuyuk =>
            $"Dosya boyutu en fazla {DosyaBasinaEnFazlaBayt / 1024 / 1024} MB olabilir.",
        _ => "Dosya kabul edilmedi."
    };
}
