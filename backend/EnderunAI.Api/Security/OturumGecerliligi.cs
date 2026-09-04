using EnderunAI.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace EnderunAI.Api.Security;

/// <summary>
/// PAROLA DEĞİŞİNCE DİĞER OTURUMLAR DÜŞER.
///
/// ── KARAR VE GEREKÇESİ (2026-09-03) ──
///
/// Parola değiştirmenin sebebi çoğu zaman "bu parolayı başkası
/// biliyor"dur. Eski oturumlar yaşamaya devam ederse değişiklik amacına
/// ULAŞMAZ: parolayı bilen kişinin açık oturumu 12 saat daha çalışır.
/// "Dar olan kazanır": değişimden ÖNCE üretilmiş her jeton geçersiz.
///
/// ── BELLEK KAYNAK DEĞİL, ÖNBELLEKTİR ──
///
/// İlk tasarım haritayı açılışta yükleyip bellekte KAYNAK olarak
/// tutuyordu. Mehmet'in sabotajı tasarımın zayıf yerine vurdu:
/// *"servisi yeniden başlat → değişimden önceki jeton HÂLÂ
/// reddedilmeli"*. Açılışta yükleme bunu sağlar ama bir TERCİHTİR —
/// yüklemeyi çağırmayı unutan ya da sırası kayan bir değişiklik,
/// korumayı sessizce kapatırdı. Bellekte tutulan tek gerçek kaynak,
/// süreç ömrü kadar yaşayan bir garantidir.
///
/// Artık kayıt yoksa VERİTABANINDAN OKUNUYOR ve önbelleğe alınıyor.
/// Kaynak `users.PasswordChangedAtUtc`; bellek yalnızca hızlandırıcı.
///
/// ── MALİYET ──
///
/// Ölçüldü: bu sistemde kimlik doğrulamada veritabanına HİÇ
/// bakılmıyor; yetki ara katmanı yalnız jeton iddialarını okuyor. Bu
/// tasarım o tercihi bozmuyor: veritabanına yalnız ÖNBELLEK IŞKALARSA
/// gidiliyor — süreç ömrü boyunca kullanıcı başına bir kez.
///
/// ── TEK SÜREÇ VARSAYIMI — ZAYIFLADI, KALKMADI ──
///
/// Ölçüldü (2026-09-03): `enderunai-backend.service` tek `dotnet`
/// süreci çalıştırıyor, nginx'te upstream havuzu yok.
///
/// Önbellek geri dönüşü sayesinde yeniden başlatma artık korumayı
/// bozmuyor. Ama İKİNCİ BİR SÜREÇ eklenirse varsayım hâlâ kırılır:
/// A sürecinde yapılan parola değişikliği, kullanıcıyı ÖNCEDEN
/// önbelleğe almış B sürecine ulaşmaz ve B'deki eski oturum yaşamaya
/// devam eder. Çözüm o gün: kısa ömürlü önbellek (TTL) ya da ortak
/// bir depo.
///
/// Varsayım burada YAZILI olduğu için o gün aranacak yer belli.
/// </summary>
public interface IOturumGecerliligi
{
    /// <summary>Parola değişimini kaydeder; o andan öncesi geçersiz olur.</summary>
    void Kaydet(Guid kullaniciId, DateTime degisimUtc);

    /// <summary>
    /// Değişimden sonra üretilecek jetonun taşıması gereken en erken
    /// `iat`. Uç, yeni jetonu bu saniyeyle üretiyor — aksi hâlde
    /// kullanıcının kendi jetonu da reddedilirdi.
    /// </summary>
    static DateTime JetonSaniyesi(DateTime degisimUtc) =>
        OturumGecerliligi.SonrakiSaniye(degisimUtc);

    /// <summary>
    /// Jeton hâlâ geçerli mi? <paramref name="jetonUretimUtc"/> jetonun
    /// `iat` iddiası.
    /// </summary>
    Task<bool> GecerliAsync(
        Guid kullaniciId,
        DateTime? jetonUretimUtc,
        AppDbContext db,
        CancellationToken cancellationToken = default);
}

public sealed class OturumGecerliligi : IOturumGecerliligi
{
    /// <summary>
    /// kullanıcı → (parola değişim zamanı, bu kaydın alındığı an).
    ///
    /// ── NEGATİF DEĞERİN KISA ÖMRÜ VAR ──
    ///
    /// `null` değer ("bu kullanıcının damgası yok") da önbellekleniyor,
    /// yoksa parolasını hiç değiştirmemiş kullanıcılar HER istekte
    /// veritabanına giderdi.
    ///
    /// Ama kalıcı bir negatif değer kırılgan: damgayı veritabanına
    /// yazan ama <see cref="Kaydet"/> çağırmayan HER yeni yol,
    /// korumayı sessizce kapatırdı. Bu varsayım değil ölçüm — yönetici
    /// sıfırlama yolu tam olarak böyleydi.
    ///
    /// İKİ KATMANLI ÇÖZÜM:
    ///   (c) Yazma tek noktada (`ParolaYazici`) ve bunu bekçi test
    ///       zorluyor — bugünü kapatır.
    ///   (b) Negatif değerin ömrü {NegatifOmurSaniye} saniye — yarını
    ///       kapatır: (c) bir gün delinirse, sessiz bozulma en fazla
    ///       bu kadar sürer.
    ///
    /// POZİTİF değerin ömrü YOK: bir damga yazıldıysa geri alınmaz,
    /// eskimez. Onu da süreli yapmak, hiçbir şey kazandırmadan her
    /// dakika veritabanına gitmek olurdu.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, (DateTime? Damga, DateTime Alindi)>
        _onbellek = new();

    /// <summary>Negatif (damga yok) kaydın ömrü.</summary>
    public const int NegatifOmurSaniye = 60;

    public void Kaydet(Guid kullaniciId, DateTime degisimUtc) =>
        _onbellek[kullaniciId] = (degisimUtc, DateTime.UtcNow);

    /// <summary>
    /// Verilen anın AŞILDIĞI ilk saniye sınırı.
    ///
    /// Tam saniyede bile bir sonrakine gidiyor: aksi hâlde
    /// 12:00:00.000'da yapılan bir değişiklikte, aynı saniyede
    /// üretilmiş ESKİ jeton (iat = 12:00:00) sınıra EŞİT olur ve
    /// geçerdi.
    /// </summary>
    public static DateTime SonrakiSaniye(DateTime an) =>
        new DateTime(
            an.Ticks - (an.Ticks % TimeSpan.TicksPerSecond),
            DateTimeKind.Utc)
        .AddSeconds(1);

    public async Task<bool> GecerliAsync(
        Guid kullaniciId,
        DateTime? jetonUretimUtc,
        AppDbContext db,
        CancellationToken cancellationToken = default)
    {
        var tazeMi =
            _onbellek.TryGetValue(kullaniciId, out var kayit) &&
            (kayit.Damga is not null ||
             kayit.Alindi.AddSeconds(NegatifOmurSaniye) > DateTime.UtcNow);

        if (!tazeMi)
        {
            // ÖNBELLEK IŞKASI (ya da bayat negatif kayıt): kaynak
            // veritabanı.
            var damga = await db.Users
                .AsNoTracking()
                .Where(x => x.Id == kullaniciId)
                .Select(x => x.PasswordChangedAtUtc)
                .SingleOrDefaultAsync(cancellationToken);

            kayit = (damga, DateTime.UtcNow);
            _onbellek[kullaniciId] = kayit;
        }

        if (kayit.Damga is not DateTime zaman)
            return true; // Parolası hiç değişmemiş: kısıt yok.

        /*
         * JETON ÜRETİM ZAMANI OKUNAMIYORSA REDDEDİLİR (fail-closed).
         *
         * `iat` iddiası olmayan bir jeton, bu mekanizmadan ÖNCE
         * üretilmiş demektir — yani parola değişiminden de önce.
         * "Okuyamadım, geçir" demek eski jetonların tamamına kapıyı
         * açık bırakırdı.
         *
         * DEPLOY SONUCU: bu sürüm yayınlandığı anda `iat` taşımayan
         * TÜM mevcut jetonlar reddedilir — herkes yeniden giriş yapar.
         * Bu bilinçli ve ilan edilmiştir.
         */
        if (jetonUretimUtc is not DateTime uretim)
            return false;

        /*
         * ═══ DAMGA YUKARI YUVARLANIYOR — SONDA GÖSTERDİ ═══
         *
         * `iat` SANİYE çözünürlüğünde ve bu iki yönlü bir sorun:
         *
         *   AŞAĞI yuvarlarsak: değişimle AYNI SANİYEDE üretilmiş
         *   ESKİ jetonlar hayatta kalır. Kendi yeni jetonunu kurtarmak
         *   için açılan pay, saldırganın jetonunu da kurtarır.
         *
         *   HAM karşılaştırırsak: kullanıcının KENDİ yeni jetonu da
         *   (aynı saniyede üretildiği için) reddedilir.
         *
         * İlk sürüm AŞAĞI yuvarlıyordu ve sonda bunu yakaladı:
         * `Degisiklikten_Sonra_ESKI_JETON_Reddedilir` üç sabotajda
         * düştü, birinde düşmedi — yani sabotaja değil ZAMANLAMAYA
         * bağlıydı. Testte her şey aynı saniyeye düşüyordu; canlıda
         * ara sıra düşerdi ve teşhisi çok daha zor olurdu.
         *
         * ÇÖZÜM: sınır bir SONRAKİ saniye. Değişim saniyesindeki ve
         * öncesindeki tüm jetonlar reddedilir; kullanıcının kendi yeni
         * jetonu ise bu sınırla üretiliyor (AuthController), yani
         * kesinlikle geçerli.
         *
         * Yönü çevirmek boşluğu KAPATIYOR; pay vermek açıyordu.
         */
        var sinir = SonrakiSaniye(zaman);

        return uretim >= sinir;
    }
}
