using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace EnderunAI.Api.Security.UcKapisi;

/// <summary>Beyanı olmayan bir uç: ne niteliği, ne anonim işareti, ne muafiyeti var.</summary>
public sealed record BeyansizUc(string Anahtar, string Sablon);

/// <summary>
/// UÇ KAPISI — HER UCUN BİR BEYANI OLMALIDIR.
///
/// KURAL: `api/` altındaki her uç ya <see cref="RequirePermissionAttribute"/>
/// taşır, ya `[AllowAnonymous]` taşır, ya da muafiyet listesinde kategorisi
/// ve gerekçesiyle yer alır. Üçü de yoksa uygulama AÇILMAZ.
///
/// NEDEN AÇILIŞTA: niteliği silen kişi bunu deploy'da öğrenmelidir,
/// saldırgan değil.
///
/// NEDEN YÖNLENDİRME TABLOSU, KAYNAK TARAMASI DEĞİL: kaynak taraması
/// yalnız denetleyicilerdeki `[Http...]` niteliklerini görür; minimal
/// API uçlarını, hub uçlarını ve `negotiate` gibi çerçevenin ürettiği
/// uçları göremez. Tabloya bakan sayaç, gerçekte hizmet verileni sayar.
///
/// AYNI OKUMA YOLU: nitelik <see cref="PermissionAuthorizationMiddleware"/>
/// ile BİREBİR aynı çağrıyla okunur (`GetOrderedMetadata`). İki ayrı okuma
/// yazılsaydı biri güncellenip diğeri kalırdı.
///
/// DÜRÜST SINIR — BU MUHAFIZ NEYİ YAKALAMAZ:
/// Bu kapı savunmanın SİLİNMESİNİ değil, BEYANSIZLIĞINI yakalar. Muafiyet
/// gerekçesi "üyelik süzgeci serviste geçiliyor" diyorsa ve biri o süzgeci
/// servisten çıkarırsa, muafiyet satırı yerinde durur ve bu kapı YEŞİL
/// kalır. İkinci kaybolma biçimini yakalayan şey bu muhafız değil, her
/// muafiyetin arkasındaki testtir. O testlerin dolu kalması muhafızın
/// PARÇASIDIR, süsü değil.
/// </summary>
public static class UcKapisiDenetimi
{
    /// <summary>Denetlenen yüzey. Yalnız `api/` altı; çerçeve uçları dışarıda.</summary>
    private const string Yuzey = "api/";

    public static IReadOnlyList<BeyansizUc> BeyansizlariBul(
        IEnumerable<Endpoint> uclar,
        IReadOnlySet<string> muafAnahtarlar)
    {
        var bulunan = new List<BeyansizUc>();

        foreach (var uc in Yuzeydekiler(uclar))
        {
            /*
             * NİTELİK OKUMASI MIDDLEWARE İLE AYNI ÇAĞRI.
             * Değişirse ikisi birlikte değişsin diye burada da
             * `GetOrderedMetadata` kullanılıyor.
             */
            if (uc.Metadata.GetOrderedMetadata<RequirePermissionAttribute>().Count > 0)
                continue;

            // `[AllowAnonymous]` GÜRÜLTÜLÜ BİR BEYANDIR — sessiz yokluk değil.
            if (uc.Metadata.GetMetadata<IAllowAnonymous>() is not null)
                continue;

            var anahtar = Anahtar(uc);
            if (muafAnahtarlar.Contains(anahtar))
                continue;

            bulunan.Add(new BeyansizUc(anahtar, Sablon(uc)));
        }

        return bulunan
            .DistinctBy(x => x.Anahtar)
            .OrderBy(x => x.Anahtar, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// ÖLÜ MUAFİYET DE BİR HATADIR. Karşılığı kalmamış bir muafiyet satırı,
    /// bir gün adı aynı olan başka bir ucu sessizce affeder. Sayılamayan
    /// muafiyet, muafiyet değil unutmadır.
    /// </summary>
    public static IReadOnlyList<string> OluMuafiyetler(
        IEnumerable<Endpoint> uclar,
        IReadOnlySet<string> muafAnahtarlar)
    {
        var mevcut = Yuzeydekiler(uclar).Select(Anahtar).ToHashSet(StringComparer.Ordinal);

        return muafAnahtarlar
            .Where(anahtar => !mevcut.Contains(anahtar))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// BELİRSİZ MUAFİYET — bir anahtar birden fazla uca karşılık geliyorsa.
    ///
    /// NEDEN GEREKLİ: anahtar `Denetleyici.Metot` biçiminde ve bir
    /// denetleyicide aynı adı taşıyan birden fazla eylem olabilir —
    /// ölçüldü, bugün dokuz tane var (`HrMasterData.Create` üç uca,
    /// `PriceDifference.GetAll` iki uca karşılık geliyor). Bugün
    /// hiçbiri muaf değil, hepsi nitelikli. Ama biri bir gün muaf
    /// edilseydi, TEK bir satır ÜÇ ucu birden affederdi ve bunu
    /// yazan kişi bilmezdi.
    ///
    /// KAPALI TARAFA DÜŞER: belirsiz anahtar affetmez, DURDURUR.
    /// Muafiyeti yazan kişi anahtarı netleştirmek zorunda kalır.
    /// </summary>
    public static IReadOnlyList<string> BelirsizMuafiyetler(
        IEnumerable<Endpoint> uclar,
        IReadOnlySet<string> muafAnahtarlar)
    {
        var sayim = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var anahtar in Yuzeydekiler(uclar).Select(Anahtar))
            sayim[anahtar] = sayim.TryGetValue(anahtar, out var adet) ? adet + 1 : 1;

        return muafAnahtarlar
            .Where(anahtar => sayim.TryGetValue(anahtar, out var adet) && adet > 1)
            .Select(anahtar => $"{anahtar} → {sayim[anahtar]} uç")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<Endpoint> Yuzeydekiler(IEnumerable<Endpoint> uclar) =>
        uclar.Where(uc =>
            Sablon(uc).StartsWith(Yuzey, StringComparison.OrdinalIgnoreCase));

    private static string Sablon(Endpoint uc) =>
        uc is RouteEndpoint yol
            ? (yol.RoutePattern.RawText ?? string.Empty).TrimStart('/')
            : string.Empty;

    /// <summary>
    /// MUAFİYET ANAHTARI. Denetleyici uçları için `Denetleyici.Metot` —
    /// yol şablonu değiştiğinde muafiyetin kopmaması için. Denetleyicisiz
    /// uçlar (hub, minimal API) için yol şablonunun kendisi.
    /// </summary>
    public static string Anahtar(Endpoint uc) =>
        uc.Metadata.GetMetadata<ControllerActionDescriptor>() is { } tanim
            ? $"{tanim.ControllerName}.{tanim.ActionName}"
            : Sablon(uc);
}
