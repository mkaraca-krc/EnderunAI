namespace EnderunAI.Api.Services.Hizir.Briefing;

/// <summary>
/// Bir brifing kaleminin aciliyeti. Sıralama buna göre yapılır;
/// kullanıcı önce yanmakta olanı görmeli.
/// </summary>
public enum BriefingSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

/// <summary>
/// Brifingdeki tek bir madde. Metin tamamen veriden üretilir; dil
/// modeli bu maddelerin içeriğine karışmaz, dolayısıyla brifingde
/// uydurma sayı çıkamaz.
/// </summary>
/// <param name="Title">Kısa başlık — "3 fatura onay bekliyor".</param>
/// <param name="Detail">Açıklama; boş olabilir.</param>
/// <param name="TargetPath">İlgili ekranın yolu; kart oradan link verir.</param>
public sealed record BriefingItem(
    string Title,
    string? Detail,
    BriefingSeverity Severity,
    string? TargetPath);

/// <summary>
/// Brifing kaynağı. Yeni bir modül geldiğinde yapılacak tek şey bu
/// arayüzü uygulayan bir sınıf yazıp DI'ya kaydetmektir; brifing
/// servisine dokunulmaz (istek #6).
///
/// YETKİ: Kaynak, kullanıcının izni yoksa hiç çalıştırılmaz. Ayrıca
/// kaynağın kendi sorgusu da <see cref="HizirToolContext.Scope"/> ile
/// sınırlanmalıdır — Katman 1'deki ilke aynen geçerli: kullanıcıya
/// göstermediğimiz veriyi hesaplamayız da.
/// </summary>
public interface IHizirBriefingSource
{
    /// <summary>Kaynağın kararlı anahtarı (test ve günlük için).</summary>
    string Key { get; }

    /// <summary>
    /// Bu kaynağın istediği izin. null ise herkese açık.
    /// </summary>
    string? RequiredPermission { get; }

    Task<IReadOnlyList<BriefingItem>> BuildAsync(
        HizirToolContext context, CancellationToken cancellationToken);
}
