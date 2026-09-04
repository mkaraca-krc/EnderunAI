using System.Reflection;

namespace EnderunAI.Api.Security.UcKapisi;

public sealed record Muafiyet(string Kategori, string Anahtar, string Gerekce);

/// <summary>
/// MUAF UÇ LİSTESİ — GÖMÜLÜ KAYNAK.
///
/// NEDEN DOSYA DEĞİL, GÖMÜLÜ KAYNAK: yanına konan bir dosya, yayın
/// çıktısına kopyalanmayı unutulabilir. Bu depoda tam olarak bu oldu:
/// hesap planı seed dosyası çalışma dizininden okunuyordu ve canlıda
/// `FileNotFoundException` verdi (bkz. EnderunAI.Api.csproj). Açılışta
/// durduran bir muhafız, KENDİ AMBALAJ HATASIYLA duramamalıdır; yoksa
/// güvenlik kapısı dağıtım kırılganlığına dönüşür.
///
/// KAPALI TARAFA DÜŞER: kaynak okunamazsa liste BOŞ sayılmaz, istisna
/// atılır ve uygulama açılmaz. Boş bir muafiyet listesi her ucu beyansız
/// gösterirdi; sessizce boş dönen bir okuma ise TERSİNE, her ucu affeden
/// bir listeyle karışabilirdi. İkisi de yanlış — doğru cevap durmaktır.
/// </summary>
public static class MuafiyetListesi
{
    public const string KaynakAdi =
        "EnderunAI.Api.Security.UcKapisi.MuafUclar.txt";

    /// <summary>Gerekçe bir cümle olmalıdır; "ok" ya da "-" muafiyet gerekçesi değildir.</summary>
    private const int AsgariGerekceUzunlugu = 25;

    private static readonly string[] Kategoriler =
    [
        "kendi-kimligi",
        "uyelik-kapisi",
        "alici-kapisi",
        "dinamik-izin",
        "yalniz-kimlik",
    ];

    /// <summary>Kaynak yayın çıktısında var mı. Ayrı testin ölçtüğü şey.</summary>
    public static bool GomuluKaynakVar(Assembly? derleme = null) =>
        (derleme ?? typeof(MuafiyetListesi).Assembly)
            .GetManifestResourceNames()
            .Contains(KaynakAdi, StringComparer.Ordinal);

    public static IReadOnlyList<Muafiyet> Oku(Assembly? derleme = null)
    {
        var hedef = derleme ?? typeof(MuafiyetListesi).Assembly;

        using var akis = hedef.GetManifestResourceStream(KaynakAdi)
            ?? throw new InvalidOperationException(
                $"Muaf uç listesi gömülü kaynak olarak bulunamadı: {KaynakAdi}. " +
                "Uygulama açılamaz — muafiyet listesi okunamadan uç kapısı " +
                "denetlenemez. EnderunAI.Api.csproj içindeki EmbeddedResource " +
                "girdisini kontrol edin.");

        using var okuyucu = new StreamReader(akis);

        var satirlar = okuyucu.ReadToEnd()
            .Split('\n')
            .Select(s => s.Trim())
            .Where(s => s.Length > 0 && !s.StartsWith('#'))
            .ToList();

        var sonuc = new List<Muafiyet>();
        var gorulen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var satir in satirlar)
        {
            var parca = satir.Split('|', 3);

            if (parca.Length != 3)
                throw new InvalidOperationException(
                    $"Muaf uç satırı üç alanlı olmalı (kategori | anahtar | gerekçe): {satir}");

            var kategori = parca[0].Trim();
            var anahtar = parca[1].Trim();
            var gerekce = parca[2].Trim();

            if (!Kategoriler.Contains(kategori, StringComparer.Ordinal))
                throw new InvalidOperationException(
                    $"Tanınmayan muafiyet kategorisi '{kategori}'. " +
                    $"Beklenen: {string.Join(", ", Kategoriler)}");

            if (anahtar.Length == 0)
                throw new InvalidOperationException($"Muafiyet anahtarı boş: {satir}");

            /*
             * GEREKÇE ZORUNLU VE UZUNLUĞU ÖLÇÜLÜR. Kısa bir gerekçe,
             * gerekçe değil bir onay kutusudur; listeyi doldurmayı
             * kolaylaştırır ve listenin varlık sebebini yok eder.
             */
            if (gerekce.Length < AsgariGerekceUzunlugu)
                throw new InvalidOperationException(
                    $"'{anahtar}' muafiyetinin gerekçesi çok kısa " +
                    $"({gerekce.Length} < {AsgariGerekceUzunlugu} karakter). " +
                    "Muafiyetin nedeni yazılmalıdır.");

            if (!gorulen.Add(anahtar))
                throw new InvalidOperationException(
                    $"Muafiyet listesinde yinelenen anahtar: {anahtar}");

            sonuc.Add(new Muafiyet(kategori, anahtar, gerekce));
        }

        return sonuc;
    }

    public static IReadOnlySet<string> Anahtarlar(Assembly? derleme = null) =>
        Oku(derleme).Select(x => x.Anahtar).ToHashSet(StringComparer.Ordinal);
}
