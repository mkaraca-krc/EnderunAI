namespace EnderunAI.Api.Models;

/// <summary>
/// Kırmızı (işe alınamaz) engelinin üst yetkiyle geçilmesi.
///
/// AYRI TABLO, çünkü override bir OLAYDIR. Personel kartına ya da
/// çıkış kaydına tek alan olarak yazılsaydı ikinci override
/// birincisini siler ve "kaç kez, kim, hangi gerekçeyle geçti"
/// sorusu cevapsız kalırdı — denetim izinin anlamı budur.
///
/// Kimlik numarası da kopyalanıyor: personel kaydı sonradan
/// silinirse bile hangi kişi için geçildiği izlenebilir kalmalı.
/// </summary>
public sealed class PersonnelRehireOverride : BaseEntity
{
    /// <summary>Engelin kaynağı olan ESKİ personel kaydı.</summary>
    public Guid MatchedPersonnelId { get; set; }

    /// <summary>Engeli geçilerek açılan/aktifleştirilen kayıt.</summary>
    public Guid? TargetPersonnelId { get; set; }

    public string IdentityNumber { get; set; } = string.Empty;

    /// <summary>Geçilen değerlendirme kodu (o anki hali).</summary>
    public RehireCode OverriddenCode { get; set; }

    /// <summary>
    /// Geçiş gerekçesi. ZORUNLU: gerekçesiz bir override, engeli
    /// olmayan bir sisteme eşdeğerdir.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    public Guid? OverriddenByUserId { get; set; }
    public DateTime OverriddenAtUtc { get; set; } = DateTime.UtcNow;
}
