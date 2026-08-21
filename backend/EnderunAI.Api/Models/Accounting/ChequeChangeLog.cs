namespace EnderunAI.Api.Models;

/// <summary>
/// ÇEK DEĞİŞİKLİK KAYDI — kim, ne zaman, hangi alan, eski → yeni.
///
/// NEDEN AYRI TABLO: `ChequeMovement` DURUM geçişlerini tutuyor
/// (portföy → bankada → tahsil). Düzenleme bir durum geçişi değil,
/// aynı durumdaki kaydın ALAN düzeltmesi. İkisini tek tabloya
/// sıkıştırmak "bu çek ne yaşadı" sorusunu okunmaz hâle getirirdi.
///
/// SATIR BAŞINA BİR ALAN: "vade ve tutar değişti" tek satıra
/// sıkıştırılsaydı hangi alanın ne olduğunu ayrıştırmak metin
/// ayrıştırmaya kalırdı ve rapor süzgeci yazılamazdı.
/// </summary>
public sealed class ChequeChangeLog : BaseEntity
{
    public Guid ChequeId { get; set; }
    public Cheque Cheque { get; set; } = null!;

    /// <summary>Değişen alanın makine adı (Amount, DueDate, ChequeNumber…).</summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>Kullanıcının gördüğü alan adı (Tutar, Vade…).</summary>
    public string FieldLabel { get; set; } = string.Empty;

    public string? OldValue { get; set; }
    public string? NewValue { get; set; }

    /// <summary>
    /// MUHASEBEYİ ETKİLEYEN ALAN MI.
    ///
    /// Tutar, vade ve para birimi değişince bağlı fiş ters kayıtla
    /// kapatılıp yenisi kesiliyor; açıklama ya da banka adı değişince
    /// kesilmiyor. İkisi aynı listede ayırt edilemezse "hangi
    /// düzeltme mizanı oynattı" sorusu satır satır okumaya kalırdı.
    /// Rapor bu bayrakla süzülüyor.
    /// </summary>
    public bool AffectsAccounting { get; set; }

    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? ChangedByUserId { get; set; }

    /// <summary>Kullanıcının yazdığı düzeltme gerekçesi (varsa).</summary>
    public string? Reason { get; set; }
}
