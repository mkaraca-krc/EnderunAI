using EnderunAI.Api.Models;

namespace EnderunAI.Api.Services.Accounting;

/// <summary>
/// VERİLEN ÇEK KASADAN ÖDENMEZ (ÇEK/2).
///
/// Banka çeki, üzerinde yazan bankadaki hesaptan tahsil edilir; kasadan
/// ödenmesi diye bir şey yoktur. Buna rağmen canlıda iki çek
/// (VCK-2026-000020, VCK-2026-000022) Garanti yaprağı oldukları hâlde
/// KASA hesabından ödenmiş işaretlendi. Sebep dikkatsizlik değildi:
/// açılır listede kasa ile banka yan yana duruyordu ve altı banka
/// hesabının adı birebir aynıydı.
///
/// Bu tek süzgeç, üç yanlış kayıttan ikisini baştan imkânsız kılardı.
///
/// KURAL 39'UN KIRILDIĞI YER: çekin ödeme hesabı "zorunlu olmayan +
/// doğrulanmayan + fiş üreten" bileşimiydi. Üçünden biri kırılıyor —
/// artık DOĞRULANIYOR.
///
/// SAF VE TEK: hem geçiş doğrulaması hem ekranın süzgeci buradan
/// besleniyor. Sunucu tarafı yetkilidir; ekranın süzgeci yalnız
/// kolaylıktır ve onu atlayan istek burada reddedilir.
/// </summary>
public static class CekOdemeHesabiKurali
{
    /// <summary>
    /// Bu geçiş VERİLEN ÇEĞİN ÖDENMESİ mi.
    ///
    /// Yalnız bu geçiş kısıtlanıyor. Alınan çekin tahsili kasaya
    /// girebilir (elden tahsil edilen çek gerçek bir akıştır) ve
    /// faktoring net parası da kasaya girebilir; kısıt oraya
    /// taşınmıyor.
    /// </summary>
    public static bool VerilenCekOdemesiMi(
        ChequeDirection yon, ChequeStatus from, ChequeStatus to) =>
        yon == ChequeDirection.Issued
        && from == ChequeStatus.Issued
        && to == ChequeStatus.Paid;

    /// <summary>
    /// Seçilen hesap bu geçişte kullanılabilir mi.
    /// Hesap seçilmemişse (null) bu kuralın söyleyeceği bir şey yok —
    /// zorunluluk denetimi ayrı kapıdır.
    /// </summary>
    public static bool Uygun(
        ChequeDirection yon, ChequeStatus from, ChequeStatus to, CashAccountType? hesapTuru)
    {
        if (hesapTuru is null) return true;
        if (!VerilenCekOdemesiMi(yon, from, to)) return true;
        return hesapTuru != CashAccountType.Cash;
    }

    /// <summary>Reddedilen seçim için kullanıcıya gösterilen cümle.</summary>
    public const string RetMesaji =
        "Verilen çek kasadan ödenemez; çekin ödendiği BANKA hesabını seçin.";
}
