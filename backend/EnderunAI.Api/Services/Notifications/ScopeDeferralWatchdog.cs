using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Notifications;

/// <summary>
/// G3 ERTELEMESİNİN TETİKLEYİCİLERİNİ İZLEYEN BEKÇİ.
///
/// NEDEN KOD, NEDEN KOMUT DEĞİL: G3/2-3-4 paketleri ertelenirken
/// gerekçe ölçülmüştü — canlıda kapsamı sınırlı AKTİF kullanıcı yok
/// ve tek şirket var, yani temel çizgideki ~450 kapsamsız okuma
/// GİZİL bir borç, fiilî sızıntı değil.
///
/// Erteleme iki koşula bağlıydı:
///   1) Kapsamı sınırlı bir kullanıcı tanımlanması,
///   2) Sisteme ikinci bir şirket eklenmesi.
///
/// Bu koşulları DURUM.md'ye "şu sorguyu çalıştır" diye yazmak
/// yetmezdi: o sorguyu birinin çalıştırmayı HATIRLAMASI gerekir ve
/// hatırlamaz. Koşul gerçekleştiği gün sistemin kendisi haber
/// vermeli.
///
/// AYRI SERVİS DEĞİL: günlük taramanın içinde koşuyor. İkinci bir
/// zamanlayıcı, ikinci bir hata yüzeyi demekti.
/// </summary>
public sealed class ScopeDeferralWatchdog(
    AppDbContext db,
    ILogger<ScopeDeferralWatchdog> logger) : IScopeDeferralWatchdog
{
    public const string ActionScopedUser = "ScopeDeferralTriggered.ScopedUser";
    public const string ActionSecondCompany = "ScopeDeferralTriggered.SecondCompany";

    /// <summary>
    /// TETİKLENDİĞİNDE FİİLÎ SIZINTIYA DÖNÜŞEN, ADI KONMUŞ BORÇLAR.
    ///
    /// NEDEN BURADA, NEDEN BİR NOTTA DEĞİL: bu borçların ortak özelliği
    /// BUGÜN sömürülemez olmalarıdır — tetikleyici koşul gerçekleşene
    /// kadar zararsız dururlar. "Şu paketten önce bakarız" demek, birinin
    /// o günü hatırlamasına güvenmektir; hatırlamaz. Tetikleyici zaten
    /// burada ölçülüyor; borcun adı da burada dursun ki uyarı düştüğü gün
    /// NEYE bakılacağı uyarının İÇİNDE yazsın.
    ///
    /// KAPSAM BORCU ÇİZGİSİ AYRI VE YETERLİ: aşağıdaki altı KPI yolunun
    /// alt servisleri `kapsam-temel-cizgi.txt` içinde zaten sayılıyor
    /// (CashFlowProjection 13, Expense 29, Payroll 14, Cheque 8,
    /// FinancialInstrument 5, Profitability 1 = 70 okuma), yani
    /// ÇOĞALAMAZLAR. Buraya yazılan şey borcun kendisi değil, borcun
    /// TETİKLENDİĞİNDE NE ANLAMA GELDİĞİ.
    /// </summary>
    public static readonly IReadOnlyList<string> AdiKonmusBorclar =
    [
        "KPI KAPSAM SÜZGECİ (ManagementKpiController + ManagementKpiService): " +
        "yedi göstergeden yalnız SATIN ALMA veri kapsamını alt servise " +
        "geçiriyor. Diğer altısı — nakit kapanış, kâr marjı, gider toplamı, " +
        "bordro maliyeti, açık çek, finansal araç yükü — `companyId`'yi " +
        "sorgu dizesinden alıyor ve alt servislerde de kapsam kontrolü yok " +
        "(CashFlowProjectionService'te tek kapsam referansı bile yok). " +
        "TEK ŞİRKETLE SÖMÜRÜLEMEZ. İkinci şirkette: `CashFlowView` izni olan " +
        "ama kapsamı o şirketi içermeyen bir kullanıcı, şirket kimliğini elle " +
        "yazarak o şirketin nakit kapanışını okuyabilir. " +
        "Ölçüm: KAPI/1, 2026-09-04.",
    ];

    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        await KapsamliKullaniciKontrolAsync(cancellationToken);
        await IkinciSirketKontrolAsync(cancellationToken);
    }

    /// <summary>
    /// TETİKLEYİCİ 1: kapsamı sınırlı aktif kullanıcı.
    ///
    /// `ScopeType = 0` (All) global erişim demek; ondan farklı bir
    /// kapsam kaydı olan AKTİF kullanıcı, kapsam süzgeçlerinin
    /// gerçekten devreye girdiği ilk andır. O andan itibaren
    /// kapsamsız okumalar gizil borç olmaktan çıkıp FİİLÎ SIZINTI
    /// haline gelir.
    /// </summary>
    private async Task KapsamliKullaniciKontrolAsync(CancellationToken cancellationToken)
    {
        var sayi = await db.UserDataScopes
            .AsNoTracking()
            .CountAsync(
                x => x.ScopeType != DataScopeType.All && x.User.IsActive,
                cancellationToken);

        if (sayi == 0)
            return;

        await UyariYazAsync(
            ActionScopedUser,
            $"Kapsamı sınırlı aktif kullanıcı tanımlandı ({sayi} adet) — " +
            "G3/2-3-4 ertelemesi sona erdi, kapsam paketleri öne alınmalı.",
            new { kapsamliKullaniciSayisi = sayi },
            cancellationToken);
    }

    /// <summary>
    /// TETİKLEYİCİ 2: ikinci şirket.
    ///
    /// Tek şirketle şirket izolasyonu ölçülemez; ikinci şirket
    /// eklendiği an bütün kapsam açıkları ölçülebilir ve
    /// sömürülebilir hale gelir.
    /// </summary>
    private async Task IkinciSirketKontrolAsync(CancellationToken cancellationToken)
    {
        var sayi = await db.Companies
            .AsNoTracking()
            .CountAsync(x => x.IsActive, cancellationToken);

        if (sayi <= 1)
            return;

        await UyariYazAsync(
            ActionSecondCompany,
            $"Sistemde {sayi} aktif şirket var — G3/2-3-4 ertelemesi sona erdi, " +
            "kapsam paketleri öne alınmalı.",
            new { aktifSirketSayisi = sayi },
            cancellationToken);
    }

    /// <summary>
    /// UYARI GÜNDE BİR KEZ TEKRARLANIR.
    ///
    /// Tek satır kaçar; tekrarlayan satır fark edilir. Ama günde
    /// birden fazla yazılsa kayıt gürültüye boğulur ve asıl bilgi
    /// yine kaybolur — bu yüzden gün başına bir kez.
    /// </summary>
    private async Task UyariYazAsync(
        string action,
        string ozet,
        object ayrinti,
        CancellationToken cancellationToken)
    {
        var bugun = DateTime.UtcNow.Date;

        var buGunYazilmis = await db.SecurityAuditEvents
            .AsNoTracking()
            .AnyAsync(
                x => x.Action == action && x.OccurredAtUtc >= bugun,
                cancellationToken);

        if (buGunYazilmis)
            return;

        logger.LogWarning("G3 ERTELEME TETİKLEYİCİSİ: {Ozet}", ozet);

        db.SecurityAuditEvents.Add(new SecurityAuditEvent
        {
            Action = action,
            EntityType = "ScopeDeferral",
            DetailsJson = JsonSerializer.Serialize(new
            {
                summary = ozet,
                ayrinti,
                adiKonmusBorclar = AdiKonmusBorclar,
                kaynak =
                    "DURUM.md — G3/2-3-4 erteleme gerekçesi ve tetikleyici koşullar."
            }),
            OccurredAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
    }
}
