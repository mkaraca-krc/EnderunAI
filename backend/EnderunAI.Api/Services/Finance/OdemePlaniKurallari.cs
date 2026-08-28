using EnderunAI.Api.Models.Finance;

namespace EnderunAI.Api.Services.Finance;

/// <summary>
/// ÖDEME PLANININ SAF KARARLARI (ÖP/1a · K2, K3, K4, K8).
///
/// VERİTABANINA BAKMAZ, HİÇBİR ŞEY YAZMAZ. Dört kontrolün mantığı
/// burada tek noktada duruyor; servis yalnız çağırıyor.
///
/// NEDEN AYRI: bu oturumda ÇEK/2'de kilidi iki yerde kurmuş ve
/// sondanın hangi bariyeri ölçtüğünü göremez hâle gelmiştim
/// (Kural 25, 45). Kararı saf bir fonksiyona çıkarmak, sabotajın
/// TEK bir yeri devre dışı bırakmasını ve testin gerçekten o kararı
/// ölçmesini sağlıyor.
/// </summary>
public static class OdemePlaniKurallari
{
    /// <summary>Onay üç haftadan eskiyse düşer (K8).</summary>
    public const int OnayGecerlilikHaftasi = 3;

    // ═══════════════════════════════════════════════════════════════
    // K2 — ONAY ANLIK GÖRÜNTÜSÜ
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Onaylanan değerler — satırdan bağımsız, taşınabilir.
    /// ÖNCELİK DAHİL (K7): sırayı değiştirmek ödeme kararını
    /// değiştirmektir.
    /// </summary>
    public sealed record OnayGoruntusu(
        Guid CurrentAccountId,
        decimal Tutar,
        OdemeYontemi Yontem,
        DateTime? CekVadesi,
        int Oncelik,
        Guid? CashAccountId);

    /// <summary>Satırın GÜNCEL hâli.</summary>
    public static OnayGoruntusu Guncel(OdemePlaniSatiri satir) =>
        new(satir.CurrentAccountId,
            satir.OnaylananTutar ?? satir.OnerilenTutar,
            satir.Yontem,
            satir.CekVadesi,
            satir.Oncelik,
            satir.CashAccountId);

    /// <summary>Satırda SAKLANAN onay görüntüsü; onaylanmamışsa null.</summary>
    public static OnayGoruntusu? Onayli(OdemePlaniSatiri satir)
    {
        if (satir.OnayliCurrentAccountId is not { } cari) return null;
        if (satir.OnayliTutar is not { } tutar) return null;
        if (satir.OnayliYontem is not { } yontem) return null;
        if (satir.OnayliOncelik is not { } oncelik) return null;

        return new OnayGoruntusu(
            cari, tutar, yontem, satir.OnayliCekVadesi, oncelik,
            satir.OnayliCashAccountId);
    }

    /// <summary>
    /// ONAYDAN SONRA DEĞİŞEN ALANLARIN ADLARI.
    ///
    /// Boş liste = satır onaylandığı gibi duruyor, ödenebilir.
    /// Dolu liste = ödeme YAPILMAZ, satır yeniden onaya döner.
    ///
    /// TARİH GÜN BAZINDA karşılaştırılıyor: aynı günün farklı saati
    /// bir karar değişikliği değildir, ama farklı gün öyledir.
    /// </summary>
    public static IReadOnlyList<string> DegisenOnayAlanlari(
        OdemePlaniSatiri satir)
    {
        var onayli = Onayli(satir);
        if (onayli is null) return ["Onay kaydı yok"];

        var guncel = Guncel(satir);
        var degisenler = new List<string>();

        if (guncel.CurrentAccountId != onayli.CurrentAccountId)
            degisenler.Add("Cari");

        if (decimal.Round(guncel.Tutar, 2) != decimal.Round(onayli.Tutar, 2))
            degisenler.Add("Tutar");

        if (guncel.Yontem != onayli.Yontem)
            degisenler.Add("Ödeme yöntemi");

        if (guncel.CekVadesi?.Date != onayli.CekVadesi?.Date)
            degisenler.Add("Çek vadesi");

        if (guncel.Oncelik != onayli.Oncelik)
            degisenler.Add("Öncelik");

        if (guncel.CashAccountId != onayli.CashAccountId)
            degisenler.Add("Çıkış hesabı");

        return degisenler;
    }

    // ═══════════════════════════════════════════════════════════════
    // K3 — ÖDENEN ≤ ONAYLANAN
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Bu ödeme onaylanan tutarı aşıyor mu.
    ///
    /// AZ ÖDEMEK SORUN DEĞİL, ÇOK ÖDEMEK SORUN: kısmi ödeme serbest,
    /// aşan ödeme reddedilir.
    /// </summary>
    public static bool OdemeSiniriAsiliyorMu(
        decimal onaylananTutar, decimal halihazirOdenen, decimal yeniOdeme) =>
        decimal.Round(halihazirOdenen + yeniOdeme, 2)
            > decimal.Round(onaylananTutar, 2);

    // ═══════════════════════════════════════════════════════════════
    // K4 — HAZIRLAYAN ≠ ONAYLAYAN
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Aynı kişi hazırlayıp onaylayabilir mi (hayır).
    ///
    /// KOD DÜZEYİNDE, AYAR DEĞİL. Ayar olsaydı "bu hafta acele var"
    /// diye kapatılır ve bir daha açılmazdı.
    ///
    /// SATIRI DEĞİŞTİREN DE HAZIRLAYANDIR: satırı son güncelleyen
    /// kişi de onaylayamaz, yoksa kural "hazırla, başkasına
    /// onaylat, sonra değiştir" ile atlatılırdı.
    /// </summary>
    public static bool OnaylayabilirMi(
        Guid onaylayanUserId, Guid? hazirlayanUserId, Guid? sonDegistirenUserId)
    {
        if (hazirlayanUserId == onaylayanUserId) return false;
        if (sonDegistirenUserId == onaylayanUserId) return false;
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    // K8 — YAŞLANMA
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Onay hâlâ geçerli mi.
    ///
    /// ESKİ ONAYLA BUGÜN PARA ÇIKMAMALI. Üç haftayı aşan onay düşer
    /// ve satır "Bekliyor"a döner — yeniden onaya gelir.
    /// </summary>
    public static bool OnayGecerliMi(DateTime kararAnUtc, DateTime simdiUtc) =>
        (simdiUtc.Date - kararAnUtc.Date).TotalDays
            < OnayGecerlilikHaftasi * 7;

    /// <summary>Kaç haftadır bekliyor — planın başında gösterilir.</summary>
    public static int BeklemeHaftasi(DateTime kararAnUtc, DateTime simdiUtc) =>
        (int)((simdiUtc.Date - kararAnUtc.Date).TotalDays / 7);

    // ═══════════════════════════════════════════════════════════════
    // K6 — İKİ AYRI BÜTÇE SAYISI
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// BU CUMA ÇIKACAK NAKİT ile BU CUMA YARATILAN GELECEK
    /// YÜKÜMLÜLÜK ayrı sayılardır (K6).
    ///
    /// TEK SAYIYA TOPLANMAZ: çekle ödeme bu cumanın parasını
    /// harcamaz, ileriki bir cumanınkini harcar. Toplanırsa hafta
    /// olduğundan pahalı görünür ve gerçek nakit ihtiyacı kaybolur.
    /// </summary>
    public static bool NakitCikisiMi(OdemeYontemi yontem) =>
        yontem is OdemeYontemi.HavaleEft or OdemeYontemi.Nakit;

    /// <summary>Çek satırı gelecek yükümlülük yaratır, bugün nakit çıkarmaz.</summary>
    public static bool GelecekYukumlulukMu(OdemeYontemi yontem) =>
        yontem is OdemeYontemi.Cek;

    // ═══════════════════════════════════════════════════════════════
    // K10 — KAPANIŞ SEBEBİ
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Bu satır kapanış sebebi taşımak zorunda mı.
    ///
    /// ONAYLANMIŞ AMA ÖDENMEMİŞ ya da KISMEN ödenmiş satırlar sebep
    /// ister. Reddedilen satır istemez — reddin kendisi zaten karar.
    /// </summary>
    public static bool KapanisSebebiGerekliMi(OdemePlaniSatiri satir) =>
        satir.Karar is OdemeSatirKarari.Onaylandi or OdemeSatirKarari.Kismi
        && satir.OdemeDurumu != OdemeSatirOdemeDurumu.Odendi;

    /// <summary>"Diğer" seçilirse serbest metin ZORUNLU.</summary>
    public static bool KapanisAciklamasiGerekliMi(OdemeKapanisSebebi sebep) =>
        sebep == OdemeKapanisSebebi.Diger;
}
