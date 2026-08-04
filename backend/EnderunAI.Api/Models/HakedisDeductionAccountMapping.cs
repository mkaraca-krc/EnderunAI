namespace EnderunAI.Api.Models;

/// <summary>
/// Kesinti türü → muhasebe hesabı eşlemesi (şirket bazlı).
///
/// Hakediş kesintileri farklı hesaplara borç yazılır: kesin teminat
/// verilen depozito/teminat hesabına, barter işverenden mal/hizmet
/// alacağına, yemek ve konaklama personelden alacak/gider hesabına,
/// stopaj ödenecek vergilere.
///
/// Eşleme tabloda tutuluyor çünkü finans ayarlarına her kesinti türü
/// için ayrı kolon eklemek tabloyu şişirir ve yeni tür eklendiğinde
/// migration gerektirirdi.
///
/// Çözüm sırası: kesinti satırındaki hesap → bu eşleme → finans
/// ayarındaki genel kesinti hesabı.
/// </summary>
public sealed class HakedisDeductionAccountMapping : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>Bkz. <see cref="HakedisDeductionType"/>.</summary>
    public int DeductionType { get; set; }

    public Guid AccountingAccountId { get; set; }
    public AccountingAccount AccountingAccount { get; set; } = null!;

    public string? Notes { get; set; }
}
