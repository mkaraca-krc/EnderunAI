using System.Collections.Concurrent;

namespace EnderunAI.Api.Security;

/// <summary>
/// ERİŞİM REDDİ GÜNLÜĞÜ — "BU KULLANICI NEDEN ÇIKARILDI?" (GÜNLÜK/1).
///
/// ═══ DOĞURAN OLAY (2026-09-09) ═══
///
/// Bir kullanıcı yayın sonrası oturumunu kaybetti. Sebebi bulmak için
/// arka uç günlüğüne bakıldı: 30 dakikalık pencerede 2034 satır vardı
/// ve HEPSİ EF komut kaydıydı. 401 kararlarının HİÇBİRİ yazılmıyordu.
///
/// Sekiz aday elenerek ilerlendi ve sebep BULUNAMADI. Sistem
/// "bu kullanıcı neden çıkarıldı" sorusuna cevap veremiyordu.
///
/// ═══ SEBEP SAYILI BİR KÜME, SERBEST METİN DEĞİL ═══
///
/// Serbest metin sayılamaz, gruplanamaz ve zamanla ayrışır. Sekiz
/// sebep sekiz FARKLI HİKÂYEDİR: "jeton yok" ile "hesap pasif"
/// birbirine benzemez ve karıştırılırsa teşhis yine tahmine düşer.
///
/// ═══ NE YAZILMAZ ═══
///
/// Parola, jeton, çerez değeri, başlık içeriği ASLA. Kimlik olarak
/// kullanıcı kimliği yeter. YOLUN SORGU DİZESİ DE YAZILMAZ — orada
/// kimlik ya da kişisel veri taşınabilir.
///
/// ═══ SEL BASKINI ═══
///
/// Giriş döngüsü kusurunda 10 SANİYEDE 862 istek ölçülmüştü. Aynı şey
/// tekrarlarsa günlük diski doldurur ve kusuru düzeltirken yeni bir
/// olay yaratırız.
///
/// Pencere 10 saniye — ÖLÇÜLMÜŞ o pencereyle aynı, tesadüfi değil.
/// İlk olay HEMEN yazılır (gecikme yok), penceredeki tekrarlar
/// sayılır ve pencere kapanınca tek satırda "xN" olarak düşer.
/// </summary>
public enum ErisimRetSebebi
{
    /// <summary>Jeton hiç gelmedi.</summary>
    JetonYok = 1,

    /// <summary>Jetonun süresi dolmuş.</summary>
    SuresiDolmus = 2,

    /// <summary>İmza doğrulanamadı.</summary>
    ImzaGecersiz = 3,

    /// <summary>Jeton başka bir sebeple geçersiz.</summary>
    JetonGecersiz = 4,

    /// <summary>Parola değişimi eski jetonu iptal etti.</summary>
    OturumIptal = 5,

    /// <summary>Hesap pasif ya da bulunamadı.</summary>
    HesapPasif = 6,

    /// <summary>İzin yok — uçtaki nitelikten.</summary>
    IzinYok = 7,

    /// <summary>İzin yok — yoldan türetilen izinden.</summary>
    IzinYokYoldan = 8,

    /// <summary>
    /// Mesai penceresi kapalı — oturum mesai ara katmanında kesildi.
    ///
    /// SONRADAN EKLENDİ, ÇÜNKÜ ÖLÇÜM EKSİĞİ GÖSTERDİ (2026-09-10): ilk
    /// sekiz sebep "her 401/403 kararı" diye yayına alındı ama
    /// `WorkHourAccessMiddleware`in 401'i HİÇ satır yazmıyordu. Muaf
    /// olmayan personelin oturumunun en sık düşeceği yol buydu ve
    /// günlükte izi yoktu.
    /// </summary>
    MesaiDisi = 9,

    /// <summary>
    /// Rol yok — `[RolGerekli]` kapısı, taze anlık görüntüden (ROL/1).
    /// Eskiden `[Authorize(Roles = …)]` jetondaki rolden karar veriyor ve
    /// reddi SESSİZ dönüyordu.
    /// </summary>
    RolYok = 10
}

public static class ErisimGunlugu
{
    /// <summary>
    /// Sel penceresi. 10 saniye, çünkü giriş döngüsü kusurunda
    /// ÖLÇÜLEN pencere buydu (10 sn / 862 istek).
    /// </summary>
    private static readonly TimeSpan SelPenceresi = TimeSpan.FromSeconds(10);

    private sealed record Sayac(DateTime Baslangic, int Adet);

    private static readonly ConcurrentDictionary<string, Sayac> Sayaclar = new();

    /// <summary>
    /// SORGU DİZESİ ATILIR. `/api/x?token=...` gibi bir yolda sorgu
    /// dizesi kimlik taşıyabilir; günlüğe yalnız yol girer.
    /// </summary>
    public static string YoluTemizle(string? tamYol)
    {
        if (string.IsNullOrEmpty(tamYol)) return "-";

        var i = tamYol.IndexOf('?');
        return i < 0 ? tamYol : tamYol[..i];
    }

    public static void Ret(
        ILogger logger,
        ErisimRetSebebi sebep,
        Guid? kullaniciId,
        string? yol)
    {
        var temizYol = YoluTemizle(yol);
        var kimlik = kullaniciId?.ToString() ?? "-";
        var anahtar = $"{kimlik}|{(int)sebep}|{temizYol}";
        var simdi = DateTime.UtcNow;

        var yeniPencere = true;
        int tasan = 0;

        Sayaclar.AddOrUpdate(
            anahtar,
            _ => new Sayac(simdi, 1),
            (_, mevcut) =>
            {
                if (simdi - mevcut.Baslangic > SelPenceresi)
                {
                    tasan = mevcut.Adet - 1;
                    return new Sayac(simdi, 1);
                }

                yeniPencere = false;
                return mevcut with { Adet = mevcut.Adet + 1 };
            });

        // KAPANAN PENCEREDE BİRİKEN TEKRARLAR ÖNCE BİLDİRİLİR.
        if (tasan > 0)
        {
            logger.LogWarning(
                "ERISIM-RET-TEKRAR sebep={Sebep} kullanici={Kullanici} yol={Yol} tekrar=x{Adet}",
                sebep, kimlik, temizYol, tasan);
        }

        // İLK OLAY HEMEN YAZILIR — sel toplama gecikme yaratmaz.
        if (yeniPencere)
        {
            logger.LogWarning(
                "ERISIM-RET sebep={Sebep} kullanici={Kullanici} yol={Yol}",
                sebep, kimlik, temizYol);
        }
    }

    /// <summary>
    /// Açık pencereleri kapatıp biriken tekrarları yazar ve son
    /// saatin toplamını döndürür. Zamanlanmış iş çağırır.
    /// </summary>
    public static IReadOnlyDictionary<ErisimRetSebebi, int> PencereleriKapat(ILogger logger)
    {
        var dagilim = new Dictionary<ErisimRetSebebi, int>();
        var simdi = DateTime.UtcNow;

        foreach (var (anahtar, sayac) in Sayaclar.ToArray())
        {
            var parcalar = anahtar.Split('|');
            var sebep = (ErisimRetSebebi)int.Parse(parcalar[1]);

            dagilim[sebep] = dagilim.GetValueOrDefault(sebep) + sayac.Adet;

            if (simdi - sayac.Baslangic <= SelPenceresi) continue;
            if (!Sayaclar.TryRemove(anahtar, out var kapanan)) continue;

            if (kapanan.Adet > 1)
            {
                logger.LogWarning(
                    "ERISIM-RET-TEKRAR sebep={Sebep} kullanici={Kullanici} yol={Yol} tekrar=x{Adet}",
                    sebep, parcalar[0], parcalar[2], kapanan.Adet - 1);
            }
        }

        return dagilim;
    }
}
