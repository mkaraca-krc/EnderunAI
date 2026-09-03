using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EnderunAI.Api.Data.HumanResources;

/// <summary>
/// HrDbContext'in TASARIM-ZAMANI FABRİKASI.
///
/// ── NEDEN YAZILDI (2026-09-03, DEPARTMAN/1) ──
///
/// `dotnet ef database update`, fabrika YOKSA uygulamanın Host'unu
/// ayağa kaldırmaya çalışır. Host `JWT_SECRET` istiyor; değişken yoksa
/// kurulamıyor ve EF "servis sağlayıcısı olmadan devam ediyorum"
/// diyerek `DbContextOptions&lt;HrDbContext&gt;`i çözemiyor:
///
///     Unable to resolve service for type
///     'DbContextOptions`1[HrDbContext]'
///
/// <see cref="AppDbContextFactory"/> olduğu için AppDbContext bundan
/// etkilenmiyordu; HrDbContext'in fabrikası yoktu ve tek fark buydu.
///
/// ── ASIL KUSUR: GÖÇÜN UYGULAMA SIRRINA MUHTAÇ OLMASI ──
///
/// SAHA göçünde `goc-uygula.sh` göçü canlıya BAŞARIYLA uyguladı ve
/// yine de çıkış 1 verdi — çünkü ikinci bağlamı AÇAMADI. İlk refleksim
/// betiğe `JWT_SECRET` eklemekti; bu bir YAMAYDI. Bir göçün, hiç
/// kullanmadığı bir uygulama sırrının VARLIĞINA bağlı olması yapısal
/// bir kusurdur: göç şemayı taşır, kimlik doğrulamaz.
///
/// Bu fabrikayla göç yolu artık yalnız <c>DB_CONNECTION</c> istiyor —
/// gerçekten ihtiyaç duyduğu tek şey. `JWT_SECRET` göç betiklerinden
/// tamamen kaldırıldı; ne okunuyor, ne yazılıyor, ne gerekiyor.
///
/// ── AppDbContextFactory İLE AYNI DESEN, BİLEREK ──
///
/// Farklı yazılsaydı ikisi zamanla ayrışırdı — bu olayın kendisi tam
/// olarak iki kopyanın ayrışmasıydı (prova betiği `JWT_SECRET`'i
/// biliyordu, uygulama betiği bilmiyordu).
/// </summary>
public sealed class HrDbContextFactory
    : IDesignTimeDbContextFactory<HrDbContext>
{
    public HrDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("DB_CONNECTION");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // AppDbContextFactory ile AYNI mesaj: iki bağlam için iki
            // farklı hata metni, aynı sorunu iki ayrı sorun gibi
            // gösterirdi.
            throw new InvalidOperationException(
                "Migration işlemi için DB_CONNECTION tanımlı değil.");
        }

        var optionsBuilder =
            new DbContextOptionsBuilder<HrDbContext>();

        optionsBuilder.UseNpgsql(
            connectionString,
            npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(
                    typeof(HrDbContext).Assembly.FullName);
            });

        return new HrDbContext(optionsBuilder.Options);
    }
}
