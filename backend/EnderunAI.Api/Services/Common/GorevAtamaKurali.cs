using EnderunAI.Api.Models;

namespace EnderunAI.Api.Services.Common;

/// <summary>
/// GÖREV TÜRÜ VE ATAMA KURALI — TEK KAYNAK.
///
/// NEDEN AYRI SINIF: `MasrafMerkeziKurali` ile aynı ders. Kural
/// denetleyicinin gövdesinde yaşasaydı Hızır'ın yolu (doğrudan
/// `db.WorkTasks.Add(...)`) onu HİÇ görmezdi ve PUT ile POST iki ayrı
/// kopya taşırdı. Bugün üç yazma yolu var — POST, PUT, Hızır — ve
/// üçü de bu metottan geçiyor.
///
/// SAF: veritabanı istemiyor. Personelin var olup olmadığı ve şirket
/// kapsamı denetleyicide ölçülüyor; burada yalnız İSTEĞİN KENDİ
/// İÇİNDEKİ çelişki sınanıyor. Bu ayrım testleri milisaniyeye indiriyor
/// ve kuralı her çağıranın aynı biçimde çağırdığını göstermeyi mümkün
/// kılıyor.
/// </summary>
public static class GorevAtamaKurali
{
    /// <summary>
    /// İKİ İDDİA:
    ///   1. Görev türü seçilmiş olmalı. <see cref="WorkTaskKind.Belirsiz"/>
    ///      bir tür değil, tür seçilmediğinin kaydıdır.
    ///   2. İşi yapacak taraf TEK olmalı: ya bir sistem kullanıcısı ya
    ///      bir personel. İkisi birden dolduğunda "bu işi kim yapacak"
    ///      sorusunun iki cevabı olur.
    ///
    /// ATAMASIZ GÖREV MEŞRUDUR: ikisi de boş bırakılabilir — henüz
    /// kimseye verilmemiş bir iş emri gerçek bir durumdur. Reddedilen
    /// şey yokluk değil, ÇELİŞKİ.
    /// </summary>
    /// <returns>Hata mesajı; kural sağlanıyorsa <c>null</c>.</returns>
    public static string? Dogrula(
        WorkTaskKind kind,
        Guid? assignedToUserId,
        Guid? assignedToPersonnelId)
    {
        if (kind == WorkTaskKind.Belirsiz)
        {
            return "Görev türü zorunludur: iş emri ya da hatırlatma seçin.";
        }

        if (!Enum.IsDefined(kind))
        {
            /*
             * TANIMSIZ SAYI DA REDDEDİLİR.
             *
             * `Kind = 99` gönderen bir istemci bugün sessizce geçerdi:
             * `Belirsiz` değil, dolayısıyla yukarıdaki kapı görmez.
             * Veritabanına yazılır ve ekranda türü olmayan bir görev
             * olarak görünürdü. Enum bir sayı sütunudur; sınırını
             * kendisi savunmaz.
             */
            return "Görev türü tanınmıyor.";
        }

        if (assignedToUserId.HasValue && assignedToPersonnelId.HasValue)
        {
            return
                "Görev ya bir sistem kullanıcısına ya bir personele " +
                "atanabilir; ikisi birden seçilemez.";
        }

        return null;
    }
}
