using EnderunAI.Api.Security;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// ÖDEME PLANI ONAYI YALNIZ GENEL MÜDÜR'DE (ÖP/1a · İ2).
///
/// ADMIN AÇIKÇA HARİÇ. Gerekçe: onay, paranın kime ve ne kadar
/// çıkacağına dair KARARDIR. "Tam sistem yetkisi" teknik bir roldür;
/// teknik yetki ödeme kararı vermez.
///
/// BU TEST NEDEN ZORUNLU: `RoleCatalog` izinleri YANSIMAYLA dağıtıyor
/// (`typeof(PermissionCatalog.Keys).GetFields()`). Yani kataloğa
/// eklenen her yeni anahtar, hiçbir şey yazılmasa bile rollere
/// SESSİZCE geçer. Hassas kümeye almak da tek başına yetmiyordu:
/// eski `KWithSensitive = [.. K, .. SensitiveKeys]` kümesi hassas
/// anahtarları Admin'e DE veriyordu.
///
/// Mekanizma bu paket için değiştirildi: her rol aldığı hassas
/// anahtarı KENDİ listesinde gösteriyor. Bu test o değişikliğin
/// bekçisi.
/// </summary>
public sealed class OdemePlaniIzinTests
{
    private static IReadOnlyCollection<string> RolunAnahtarlari(string rol)
    {
        var tanim = RoleCatalog.Roles.FirstOrDefault(
            x => string.Equals(x.Name, rol, StringComparison.Ordinal));

        Assert.True(tanim is not null, $"'{rol}' rolü katalogda yok.");
        return tanim!.PermissionKeys;
    }

    /// <summary>ONAY ANAHTARI GM'DE OLMALI — kural işe yaramazsa kimse onaylayamaz.</summary>
    [Fact]
    public void OnayAnahtari_GenelMudurdeVar()
        => Assert.Contains(
            PermissionCatalog.Keys.PaymentPlanApprove,
            RolunAnahtarlari("Genel Müdür"));

    /// <summary>
    /// ADMIN'DE OLMAMALI — paketin asıl güvenlik kontrolü.
    /// </summary>
    [Fact]
    public void OnayAnahtari_AdminDeYOK()
        => Assert.DoesNotContain(
            PermissionCatalog.Keys.PaymentPlanApprove,
            RolunAnahtarlari("Admin"));

    /// <summary>
    /// BAŞKA HİÇBİR ROLDE OLMAMALI. Tek tek saymak yerine TÜM
    /// katalog taranıyor: ileride eklenen bir rol de bu testten
    /// geçmek zorunda.
    /// </summary>
    [Fact]
    public void OnayAnahtari_YalnizcaGenelMudurde()
    {
        var sahipler = RoleCatalog.Roles
            .Where(x => x.PermissionKeys.Contains(
                PermissionCatalog.Keys.PaymentPlanApprove))
            .Select(x => x.Name)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "Genel Müdür" }, sahipler);
    }

    /// <summary>
    /// HAZIRLAMA İZNİ İKİ ROLDE (İ1): Ön Muhasebe + Finans Sorumlusu.
    /// GM de hazırlayabilir (tam yetki), ama K4 gereği kendi
    /// hazırladığını onaylayamaz — o kural kodda, izinde değil.
    /// </summary>
    [Theory]
    [InlineData("Ön Muhasebe")]
    [InlineData("Finans Sorumlusu")]
    public void HazirlamaIzni_IlgiliRollerdeVar(string rol)
        => Assert.Contains(
            PermissionCatalog.Keys.PaymentPlanPrepare, RolunAnahtarlari(rol));

    /// <summary>
    /// HAZIRLAMA İZNİ OLAN ROL, ONAY İZNİNİ KENDİLİĞİNDEN ALMAZ.
    /// İkisi ayrı anahtar; birinin ötekini getirmesi görevler
    /// ayrılığını (K4) izin düzeyinde de bozardı.
    /// </summary>
    [Theory]
    [InlineData("Ön Muhasebe")]
    [InlineData("Finans Sorumlusu")]
    public void HazirlayanRoller_OnayIzniniAlmaz(string rol)
        => Assert.DoesNotContain(
            PermissionCatalog.Keys.PaymentPlanApprove, RolunAnahtarlari(rol));

    /// <summary>
    /// KATALOGDA TANIMLI OLMAYAN ANAHTAR SİSTEMDE YOKTUR.
    /// `Keys`e eklenip `Permissions` listesine eklenmeyen anahtar
    /// kodda var ama veritabanına hiç düşmez — dosyanın kendi
    /// uyarısı. İki anahtar da tam tanımlı olmalı.
    /// </summary>
    [Theory]
    [InlineData("payment.plan.prepare")]
    [InlineData("payment.plan.approve")]
    public void Anahtarlar_KatalogdaTamTanimli(string anahtar)
    {
        var tanim = PermissionCatalog.Permissions
            .FirstOrDefault(x => x.Key == anahtar);

        Assert.True(tanim is not null,
            $"'{anahtar}' Permissions listesinde yok; sistemde hiç oluşmaz.");
        Assert.False(string.IsNullOrWhiteSpace(tanim!.Name));
        Assert.False(string.IsNullOrWhiteSpace(tanim.Description));
    }
}
