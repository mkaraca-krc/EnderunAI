using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Messaging;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Messaging;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// YAYIN ANINDAKİ YETKİ — BAĞLANTI ANINDAKİ YETKİ DEĞİLDİR.
///
/// ═══ ÖLÇÜLEN KUSUR (2026-09-07) ═══
///
/// SignalR bağlantısı el sıkışmada kimlik doğrular ve sonra KENDİ
/// BAŞINA yaşar. Soket açıkken kullanıcının izni alınabilir,
/// hesabı kapatılabilir, kişisel bir Deny kaydı yazılabilir —
/// bağlantı bunların hiçbirini duymaz. Alıcılar üyelikten
/// çözülüyordu ve üyelik değişmediği için mesaj gitmeye devam
/// ediyordu.
///
/// Bu dosya YAYININ KENDİSİNİ sınamıyor; sınayamaz, testte bağlı
/// istemci yok. Sınadığı şey ondan önceki karar: **KİME
/// gönderilecek.** Kusur da orada yaşıyordu.
///
/// ═══ NEDEN İKİ AYRI "İZNİ AL" AYAĞI ═══
///
/// İzin iki yoldan gidebilir ve ikisi AYNI KODDAN geçmez:
///   · rol bağının silinmesi  -> `user_roles`
///   · kişisel yasak kaydı    -> `user_permission_overrides` (Deny)
/// İlk yazdığım süzgeç rolleri okuyan tek bir EF sorgusuydu ve
/// ikinci yolu HİÇ görmüyordu: rolünde izin duran ama kişisel
/// olarak yasaklanmış kullanıcı mesaj almaya devam edecekti. Kural
/// 79 gereği süzgeç kanonik çözücüye (`IUserAuthorizationService`)
/// bağlandı; bu iki ayak o bağın koptuğunu yakalar.
///
/// ═══ HER AYAKTA POZİTİF KONTROL AYNI TESTİN İÇİNDE ═══
///
/// "B listede yok" tek başına hiçbir şey kanıtlamaz: metot boş
/// liste döndürseydi de yeşil olurdu. Bu yüzden her ayak aynı anda
/// A'nın listede DURDUĞUNU da sınıyor (Kural 48).
/// </summary>
[Collection("Integration")]
public sealed class YayinAnindaYetkiTests(DatabaseFixture fixture)
{
    private const string RolAdi = "Şantiye Şefi";

    private static async Task<Guid> KullaniciAsync(AppDbContext db, string ad)
    {
        var rol = await db.Roles.SingleAsync(x => x.Name == RolAdi);

        var kullanici = new AppUser
        {
            Username = $"yay-{ad}-{Guid.NewGuid():N}",
            FullName = $"Yayın {ad}",
            PasswordHash = "x",
            PasswordSalt = "x",
            IsActive = true,
            WorkHoursExempt = true
        };

        db.Users.Add(kullanici);
        db.UserRoles.Add(new UserRole { UserId = kullanici.Id, RoleId = rol.Id });
        db.UserDataScopes.Add(new UserDataScope
        {
            UserId = kullanici.Id,
            ScopeType = DataScopeType.All
        });

        await db.SaveChangesAsync();
        return kullanici.Id;
    }

    private static async Task<(Guid Konusma, Guid A, Guid B)> IkiliKonusmaAsync(
        AppDbContext db)
    {
        var proje = await TestDataFactory.CreateProjectAsync(
            db, $"YAY{Guid.NewGuid().ToString("N")[..6]}");

        var a = await KullaniciAsync(db, "a");
        var b = await KullaniciAsync(db, "b");

        var konusma = new Conversation
        {
            CompanyId = proje.CompanyId,
            Type = ConversationType.Direct,
            LastMessageAtUtc = DateTime.UtcNow
        };
        db.Conversations.Add(konusma);
        db.ConversationMembers.Add(new ConversationMember
        {
            ConversationId = konusma.Id,
            UserId = a
        });
        db.ConversationMembers.Add(new ConversationMember
        {
            ConversationId = konusma.Id,
            UserId = b
        });

        await db.SaveChangesAsync();
        return (konusma.Id, a, b);
    }

    private static async Task<IReadOnlyList<Guid>> AlicilarAsync(
        DatabaseFixture fixture, Guid konusma, Guid gonderen)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var servis = scope.ServiceProvider
            .GetRequiredService<IMesajlasmaService>();

        return await ((MesajlasmaService)servis)
            .AlicilariCozAsync(konusma, gonderen, CancellationToken.None);
    }

    /// <summary>
    /// SONDA — ROL BAĞI KOPARILDI, BAĞLANTIYA DOKUNULMADI.
    ///
    /// A ve B aynı konuşmada. B'nin rolü alınıyor; üyeliği duruyor,
    /// varsayımsal soketi de duruyor. B artık alıcı OLMAMALI.
    /// </summary>
    [Fact]
    public async Task RolAlinanUye_AliciListesindenDuser()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (konusma, a, b) = await IkiliKonusmaAsync(db);

        // POZİTİF KONTROL: izin alınmadan ÖNCE B gerçekten alıcı.
        // Bu satır olmasaydı test, B hiç eklenmemiş olsaydı da yeşil
        // kalırdı ve hiçbir şey kanıtlamazdı.
        var once = await AlicilarAsync(fixture, konusma, a);
        Assert.Contains(b, once);
        Assert.Contains(a, once);

        var baglar = await db.UserRoles.Where(x => x.UserId == b).ToListAsync();
        db.UserRoles.RemoveRange(baglar);
        await db.SaveChangesAsync();

        var sonra = await AlicilarAsync(fixture, konusma, a);

        Assert.DoesNotContain(b, sonra);
        // Aynı testte: A hâlâ alıcı. "Kimse alamıyor" ile
        // "B alamıyor" birbirinden burada ayrılıyor.
        Assert.Contains(a, sonra);
    }

    /// <summary>
    /// SONDA — KİŞİSEL YASAK (Deny), ROL YERİNDE DURUYOR.
    ///
    /// B'nin rolü hâlâ `mesajlar.view` taşıyor; kişisel bir Deny
    /// kaydı yazılıyor. Rolleri okuyan bir süzgeç bunu GÖRMEZ ve
    /// bu test kırmızı verir.
    /// </summary>
    [Fact]
    public async Task KisiselYasakliUye_AliciListesindenDuser()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (konusma, a, b) = await IkiliKonusmaAsync(db);

        var once = await AlicilarAsync(fixture, konusma, a);
        Assert.Contains(b, once);
        Assert.Contains(a, once);

        var anahtar = await db.Permissions
            .SingleAsync(x => x.Key == PermissionCatalog.Keys.MesajlarView);

        db.UserPermissionOverrides.Add(new UserPermissionOverride
        {
            UserId = b,
            PermissionId = anahtar.Id,
            Effect = PermissionOverrideEffect.Deny
        });
        await db.SaveChangesAsync();

        // B'nin rolü YERİNDE — ayak gerçekten ikinci yolu sınıyor.
        Assert.True(await db.UserRoles.AnyAsync(x => x.UserId == b));

        var sonra = await AlicilarAsync(fixture, konusma, a);

        Assert.DoesNotContain(b, sonra);
        Assert.Contains(a, sonra);
    }

    /// <summary>
    /// SONDA — HESABI KAPATILAN ÜYE.
    ///
    /// `IsActive = false` giriş yapmayı engeller ama AÇIK bağlantıyı
    /// kapatmaz. İşten çıkan personelin hesabı kapatıldıktan sonra
    /// açık sekmesine mesaj düşmeye devam etmemeli.
    /// </summary>
    [Fact]
    public async Task HesabiKapatilanUye_AliciListesindenDuser()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (konusma, a, b) = await IkiliKonusmaAsync(db);

        var once = await AlicilarAsync(fixture, konusma, a);
        Assert.Contains(b, once);

        var kullanici = await db.Users.SingleAsync(x => x.Id == b);
        kullanici.IsActive = false;
        await db.SaveChangesAsync();

        var sonra = await AlicilarAsync(fixture, konusma, a);

        Assert.DoesNotContain(b, sonra);
        Assert.Contains(a, sonra);
    }

    /// <summary>
    /// ESKİ KAPI HÂLÂ YERİNDE — ÜYELİK.
    ///
    /// İzin süzgeci eklenirken üyelik süzgecinin yerini almadığını
    /// gösterir. İkisi ayrı şey sınıyor ve ikisi de gerekli.
    /// </summary>
    [Fact]
    public async Task KonusmadanCikanUye_AliciListesindenDuser()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (konusma, a, b) = await IkiliKonusmaAsync(db);

        var once = await AlicilarAsync(fixture, konusma, a);
        Assert.Contains(b, once);

        var uyelik = await db.ConversationMembers
            .SingleAsync(x => x.ConversationId == konusma && x.UserId == b);
        uyelik.LeftAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var sonra = await AlicilarAsync(fixture, konusma, a);

        Assert.DoesNotContain(b, sonra);
        Assert.Contains(a, sonra);
    }
}
