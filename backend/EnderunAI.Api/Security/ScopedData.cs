using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Security;

/// <summary>
/// KAPSAMLI OKUMA DİKİŞİ — kontrolcülerin veri kapsamı taşıyan
/// varlıkları okuduğu TEK yol.
///
/// NEDEN AYRI BİR DİKİŞ, NEDEN GLOBAL SORGU SÜZGECİ DEĞİL:
///
/// Kapsamı her uçta elle uygulamak "unutulan uç = sızıntı" demek. Akla
/// gelen ilk çözüm EF global sorgu süzgeci (<c>HasQueryFilter</c>) ama
/// bu kod tabanında YANLIŞ araç olurdu:
///
///   1. BLAST RADIUS. AppDbContext'te 151 global süzgeç var ve süzgeç
///      HER sorguya uygulanır — bordro hesabı, muhasebe fişi, içe
///      aktarma, arka plan işleri dahil. Şantiye kapsamlı bir kullanıcı
///      şirket bordrosunu çalıştırdığında hata yerine SESSİZCE EKSİK
///      sonuç üretirdi. Aşırı görünürlükten daha kötü bir hata biçimi:
///      yanlış rakam, uyarısız.
///
///   2. TEKİLLİK KONTROLLERİNİ BOZARDI. PersonnelController kimlik
///      numarası tekilliğini `db.Personnel.AnyAsync(x => x.IdentityNumber
///      == ...)` ile şirket süzgeci OLMADAN kontrol ediyor — bilerek,
///      çünkü aynı TC iki şirkette iki kez açılmamalı. Global süzgeç
///      altında bu kontrol kapsam dışındaki kaydı GÖREMEZ ve mükerrer
///      kayıt sessizce oluşur.
///
///   3. ASENKRON ÇÖZÜM. Kapsam <see cref="ICurrentDataScopeService"/>
///      üzerinden asenkron çözülüyor; global süzgeç senkron durum ister.
///
///   4. KULLANICISIZ BAĞLAM. Seed ve arka plan işlerinde kapsam yoktur;
///      global süzgeç orada "kısıtlama yok"a düşmek zorunda kalır, yani
///      garanti tam da güvenilmesi gereken yerde delinir.
///
/// Bu dikiş garantiyi başka türlü veriyor: kontrolcüler kapsamlı
/// varlıklara ham <c>db.X</c> ile erişemez (bekçi test bunu yasaklıyor),
/// iş katmanı <c>db</c>'yi doğrudan kullanmaya devam eder. Kapsamı
/// atlaması GEREKEN yerler (tekillik kontrolü gibi) bekçi testinde
/// gerekçesiyle yazılı istisna olur — görünür ve sayılabilir.
///
/// ARAYÜZ DEĞİL GÜVENLİK SINIRI: buradaki bir hata veriyi açığa çıkarır.
/// R2'deki düğme kapıları yalnızca kullanıcı kolaylığıydı; bu değil.
/// </summary>
public interface IScopedData
{
    /// <summary>
    /// Kullanıcının görebileceği personel. Şantiye kapsamlı kullanıcı
    /// yalnızca KENDİ şantiyesine atanmış personeli görür.
    /// </summary>
    Task<IQueryable<Personnel>> PersonnelAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kullanıcının görebileceği aday havuzu. Aday ŞİRKET seviyesinde
    /// tutuluyor (JobCandidate yalnız CompanyId taşır); şantiye
    /// kapsamlı kullanıcının şirket kümesi boş olduğu için hiçbir aday
    /// görmez.
    /// </summary>
    Task<IQueryable<JobCandidate>> JobCandidatesAsync(
        CancellationToken cancellationToken = default);
}

public sealed class ScopedData(
    AppDbContext db,
    ICurrentDataScopeService scopeService) : IScopedData
{
    public async Task<IQueryable<Personnel>> PersonnelAsync(
        CancellationToken cancellationToken = default)
    {
        var scope = await scopeService.GetAsync(cancellationToken);

        var query = db.Personnel.AsNoTracking();

        /*
         * KAPSAM ÇÖZÜLEMEZSE HİÇBİR ŞEY DÖNMEZ (fail-closed).
         *
         * `GetAsync` yalnızca kullanıcı yoksa ya da yetkilendirme kaydı
         * pasifse null döner. Kimlik doğrulaması zorunlu bir uçta bu
         * olmamalı; olursa "kısıtlama yok" diye geçmek, kapsamın
         * çalışmadığı anda TÜM veriyi açmak olurdu.
         *
         * ProjectSitesController'daki mevcut desen de aynı: `scope is
         * null` erişim YOK demek.
         */
        if (scope is null)
            return query.Where(_ => false);

        return scope.Apply(query);
    }

    public async Task<IQueryable<JobCandidate>> JobCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        var scope = await scopeService.GetAsync(cancellationToken);

        var query = db.JobCandidates.AsNoTracking();

        // Personel ile aynı fail-closed kural.
        if (scope is null)
            return query.Where(_ => false);

        return scope.Apply(query);
    }
}
