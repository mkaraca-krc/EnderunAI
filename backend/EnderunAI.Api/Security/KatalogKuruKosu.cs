using EnderunAI.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Security;

/// <summary>
/// KATALOG UZLAŞTIRMASI — KURU KOŞU (KATALOG/1 · KT2).
///
/// ═══ NE YAPAR ═══
///
/// Uzlaştırmanın NE EKLEYECEĞİNİ ve NE SİLECEĞİNİ satır satır basar.
/// HİÇBİR ŞEY YAZMAZ: ne ekler, ne siler, ne göç uygular, ne tohumlar.
///
/// ═══ NEDEN AYRI BİR GİRİŞ ═══
///
/// Uzlaştırmayı canlıda görmeden uygulamak, 605 satırlık bir tabloya
/// gözü kapalı dokunmak olurdu. Kuru koşu listesi onaylanmadan
/// uygulama yapılmıyor (KT2).
///
/// ═══ NEDEN NORMAL AÇILIŞ YOLUNU KULLANMIYOR ═══
///
/// `Program.cs`in normal akışı açılışta göç uygular ve tohumlar.
/// Kuru koşu o akışa girseydi, "bakacağım" derken YAZMIŞ olurdu.
/// Bu yüzden en üstte, `WebApplication.CreateBuilder` çağrılmadan
/// önce yakalanıyor ve kendi bağlamını kuruyor.
/// </summary>
public static class KatalogKuruKosu
{
    public const string Bayrak = "--katalog-kuru-kosu";

    public static async Task<int> CalistirAsync()
    {
        var baglanti = Environment.GetEnvironmentVariable("DB_CONNECTION");

        if (string.IsNullOrWhiteSpace(baglanti))
        {
            Console.Error.WriteLine(
                "[katalog-kuru] DB_CONNECTION tanımlı değil. " +
                "Veritabanı adı tahmin edilmez (Y3).");
            return 2;
        }

        var secenekler = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(baglanti)
            .Options;

        // Interceptor'sız, kullanıcı bağlamsız, YALNIZ OKUMA.
        await using var db = new AppDbContext(secenekler);

        var veritabani = db.Database.GetDbConnection().Database;

        var (eklenecek, silinecek, mevcutSayi, elleEklemeTablosuVar, katalogCiftSayisi) =
            await DatabaseSeeder.RolIzinFarkiniHesaplaAsync(db);

        Console.WriteLine($"[katalog-kuru] veritabanı = {veritabani}");
        // KATALOĞUN ÜRETTİĞİ ÇİFT SAYISI — KK1.
        // Dosya karşılaştırması "aynı kaynak" der; bu sayı "aynı SONUÇ"
        // der. Bayat bir daldan koşturulsaydı bu sayı ayrışırdı.
        Console.WriteLine($"[katalog-kuru] katalogun ürettiği (rol,izin) çifti = {katalogCiftSayisi}");
        Console.WriteLine($"[katalog-kuru] mevcut role_permissions satırı = {mevcutSayi}");
        Console.WriteLine($"[katalog-kuru] EKLENECEK = {eklenecek.Count}");
        Console.WriteLine($"[katalog-kuru] SİLİNECEK = {silinecek.Count}");

        if (!elleEklemeTablosuVar)
        {
            Console.WriteLine(
                "[katalog-kuru] UYARI: role_manual_permission_grants tablosu " +
                "HENÜZ YOK (göç uygulanmadı). Elle ekleme kümesi BOŞ kabul " +
                "edildi. Bugün hiç kayıt olamayacağı için sonuç değişmiyor, " +
                "ama bu bir varsayımdır ve gizlenmiyor.");
        }
        Console.WriteLine("[katalog-kuru] ────────────────────────────────");

        // TAM LİSTE BASILIYOR, KIRPILMIYOR (Y4).
        foreach (var satir in eklenecek.OrderBy(x => x.Rol).ThenBy(x => x.Izin))
            Console.WriteLine($"  + {satir.Rol} → {satir.Izin}");

        foreach (var satir in silinecek.OrderBy(x => x.Rol).ThenBy(x => x.Izin))
            Console.WriteLine($"  - {satir.Rol} → {satir.Izin}");

        Console.WriteLine("[katalog-kuru] HİÇBİR ŞEY YAZILMADI.");
        return 0;
    }
}
