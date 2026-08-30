using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Common;

/// <summary>
/// KAYIT SÜRÜMÜ KARŞILAŞTIRMASI — TEK KAYNAK.
///
/// İstemci, okuduğu kaydın `UpdatedAtUtc` damgasını geri gönderir;
/// sunucu onu veritabanındaki güncel damgayla karşılaştırır. Farklıysa
/// kayıt araya giren biri tarafından değiştirilmiştir.
///
/// ───────────────────────────────────────────────────────────────
/// NEDEN xmin DEĞİL
/// ───────────────────────────────────────────────────────────────
///
/// PostgreSQL'in `xmin` sistem sütunu denendi (2026-08-30, HP/1 · K8)
/// ve iki farklı API ile de EF `AddColumn&lt;uint&gt;("xmin", type:
/// "xid")` üreten bir göç çıkardı. O göç canlıda
/// `column name "xmin" conflicts with a system column name` ile
/// düşerdi. Göçü elle boşaltmak çalışırdı ama biri
/// `dotnet ef migrations add` çalıştırdığında sessizce geri gelirdi.
///
/// Bu desen zaten depoda vardı (`DepodanZimmetService`) ve canlıda
/// çalışıyordu. İkinci bir eşzamanlılık mekanizması açmak yerine
/// var olan ORTAK YERE ÇIKARILDI — iki kopya zamanla ayrışırdı.
///
/// ───────────────────────────────────────────────────────────────
/// MİLİSANİYE KIRPMASI — HİLE DEĞİL, ZORUNLULUK
/// ───────────────────────────────────────────────────────────────
///
/// PostgreSQL zaman damgasını MİKROSANİYE tutuyor; JSON'a giden değer
/// milisaniyede kesiliyor. Tam eşitlik aranırsa HER istek çakışma
/// verirdi — koruma değil, kilitlenme olurdu.
/// </summary>
public static class KayitSurumu
{
    /// <summary>
    /// Kaydın tel üzerinde taşınacak sürümü.
    ///
    /// Hiç güncellenmemiş kayıtta `CreatedAtUtc` kullanılıyor:
    /// `null` dönseydi istemci sürüm gönderemez ve ilk güncelleme
    /// hep reddedilirdi — yeni açılan kayıt düzenlenemezdi.
    /// </summary>
    public static DateTime Oku(BaseEntity kayit) =>
        kayit.UpdatedAtUtc ?? kayit.CreatedAtUtc;

    /// <summary>
    /// SÜRÜM ZORUNLU — EKSİKSE REDDEDİLİR, ATLANMAZ.
    ///
    /// "Yoksa kontrolü atla" davranışı, alanı göndermeyen herkese
    /// eşzamanlılık korumasını kapatma yolu açardı (Kural 39).
    /// </summary>
    public static void Dogrula(BaseEntity kayit, DateTime? surum)
    {
        if (surum is null)
            throw new ArgumentException(
                "Sayfanın eski bir sürümü açık. Sayfayı yenileyip "
                + "tekrar deneyin.");

        var guncel = Milisaniyeye(Oku(kayit));
        var gelen = Milisaniyeye(surum.Value.ToUniversalTime());

        if (guncel != gelen)
            throw new DbUpdateConcurrencyException(
                "Kayıt siz açtıktan sonra başka bir kullanıcı tarafından "
                + "değiştirilmiş. Sayfayı yenileyip tekrar deneyin.");
    }

    private static DateTime Milisaniyeye(DateTime deger) =>
        new(
            deger.Ticks - (deger.Ticks % TimeSpan.TicksPerMillisecond),
            DateTimeKind.Utc);
}
