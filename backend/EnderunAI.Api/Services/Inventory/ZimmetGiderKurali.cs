namespace EnderunAI.Api.Services.Inventory;

using EnderunAI.Api.Models;

/// <summary>
/// ZİMMETTE GİDER YAZILIR MI — TEK KARAR YERİ.
///
/// Kural (Mehmet, 2026-08-25): sarf kategorisi zimmete verilince
/// çıkışta gider yazılır (150/740 deseni); dayanıklı taşınır gider
/// YAZMAZ, demirbaş/zimmet kaydı olarak durur.
///
/// ─────────────────────────────────────────────────────────────
/// NEDEN InventoryAccountingKind'e ÜÇÜNCÜ DEĞER EKLENMEDİ
///
/// İlk tasarım `InventoryAccountingKind`e `Durable` eklemekti.
/// Ölçüm bunu çürüttü: o enum'u İKİLİ varsayan 15 çağrı yeri var
/// (`kind == TradeGood ? a : b`). Üçüncü değer eklendiğinde dayanıklı
/// kalemler o 15 yerin HEPSİNDE sessizce sarf tarafına düşerdi —
/// mal kabul, stok sayımı ve stok-muhasebe mutabakatı dahil. Yani
/// zimmetle hiç ilgisi olmayan akışların muhasebesi değişirdi.
///
/// Kapsamı geniş olan seçenek buydu. Zimmet sorusu ayrı bir eksen:
/// "bu kalem bir kişiye verilince TÜKENİR Mİ". Karşılığı stok
/// kartında zaten var (`InventoryItem.Type`) ve o alana bugün
/// hiçbir muhasebe kararı bağlı değil — tek kullanımı reçete
/// aktarımında varsayılan atamak.
/// ─────────────────────────────────────────────────────────────
/// </summary>
public static class ZimmetGiderKurali
{
    /// <summary>
    /// TÜKENEN kalem türleri. Liste burada; modüle dağıtılmıyor.
    ///
    /// Ekipman DIŞINDAKİ her tür tükenir sayılıyor: malzeme işin
    /// içine giriyor, yedek parça takıldığında bitiyor, sarf zaten
    /// tanımı gereği tükeniyor.
    /// </summary>
    public static bool GiderYazilir(InventoryItemType tur) => tur switch
    {
        InventoryItemType.Consumable => true,
        InventoryItemType.Material => true,
        InventoryItemType.SparePart => true,

        // Dayanıklı taşınır: kişide durur, şirketin varlığı olmaya
        // devam eder. Gider yazmak onu yok saymak olurdu.
        InventoryItemType.Equipment => false,

        // Tanınmayan tür GİDER YAZMAZ.
        //
        // İki yanlıştan geri alınabilir olanı seçildi: gider
        // yazılmadıysa sonradan yazılır; yazıldıysa muhasebe kaydı
        // oluşmuştur ve düzeltmesi ters kayıt ister.
        _ => false
    };

    /// <summary>
    /// Tanınmayan tür SESSİZ GEÇMEZ. Karar `GiderYazilir` ile
    /// alınıyor, bu ise çağıranın kaydına yazacağı gerekçeyi veriyor;
    /// böylece "neden gider yazılmadı" sorusunun cevabı kayıtta durur.
    /// </summary>
    public static string Gerekce(InventoryItemType tur) => tur switch
    {
        InventoryItemType.Consumable => "sarf — çıkışta gider yazıldı",
        InventoryItemType.Material => "malzeme — çıkışta gider yazıldı",
        InventoryItemType.SparePart => "yedek parça — çıkışta gider yazıldı",
        InventoryItemType.Equipment => "dayanıklı taşınır — gider yazılmadı, zimmet kaydı olarak duruyor",
        _ => $"tanınmayan kalem türü ({(int)tur}) — gider yazılmadı"
    };
}
