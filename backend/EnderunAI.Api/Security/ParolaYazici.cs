using EnderunAI.Api.Models;

namespace EnderunAI.Api.Security;

/// <summary>
/// PAROLA YAZMANIN TEK NOKTASI.
///
/// ── NEDEN VAR (2026-09-04) ──
///
/// Parola değiştirmek üç şeyi BİRLİKTE yapmak demek:
///   1. karma ve tuzu yazmak,
///   2. `PasswordChangedAtUtc` damgasını yazmak,
///   3. oturum önbelleğini güncellemek.
///
/// Üçü ayrı ayrı yazılabildiği sürece, biri unutulan her yeni yol
/// korumayı SESSİZCE kapatır. Bu varsayım değil, ÖLÇÜM: yönetici
/// sıfırlama yolu (1)'i yapıyor, (2) ve (3)'ü YAPMIYORDU. Yani bir
/// yöneticinin parolasını sıfırladığı kullanıcının açık oturumları
/// yaşamaya devam ediyordu — üstelik sıfırlamanın kullanıldığı
/// senaryo tam da "parola başkasının elinde" senaryosudur.
///
/// ── NEDEN BİR SINIF, NEDEN "DİKKAT EDİN" NOTU DEĞİL ──
///
/// Bu kod tabanının en sık hatası, aynı kuralın ikinci bir yerde
/// eksik yazılması. Aynı gün dört kez görüldü: merkez kuralının PUT
/// kopyası, `dotnet ef` çağrısının üç ayrı ortamı, sır bekçisinin
/// taranmayan yüzeyi, parola uzunluğunun iki kopyası.
///
/// Hatırlamaya bırakılan kural unutulur. Buradaki koruma, üç adımı
/// AYIRMANIN mümkün olmaması: `ParolaYazici` çağrılmadan parola
/// değiştirilemez ve bunu bir bekçi test zorluyor
/// (`ParolaYaziciTekYerTests`).
/// </summary>
public interface IParolaYazici
{
    /// <summary>
    /// Parolayı yazar, damgayı basar ve oturum önbelleğini günceller.
    /// Çağıranın <c>SaveChangesAsync</c> yapması gerekir.
    /// </summary>
    /// <returns>
    /// Oturum sınırı: bu andan itibaren üretilecek jetonların taşıması
    /// gereken en erken `iat`.
    /// </returns>
    DateTime Uygula(AppUser user, string yeniParola, DateTime simdi);
}

public sealed class ParolaYazici(
    PasswordService passwordService,
    IOturumGecerliligi oturumGecerliligi) : IParolaYazici
{
    public DateTime Uygula(AppUser user, string yeniParola, DateTime simdi)
    {
        var karma = passwordService.Hash(yeniParola);

        user.PasswordHash = karma.Hash;
        user.PasswordSalt = karma.Salt;

        // DAMGA VE ÖNBELLEK, KARMA İLE AYNI İŞLEMDE.
        user.PasswordChangedAtUtc = simdi;
        oturumGecerliligi.Kaydet(user.Id, simdi);

        return IOturumGecerliligi.JetonSaniyesi(simdi);
    }
}
