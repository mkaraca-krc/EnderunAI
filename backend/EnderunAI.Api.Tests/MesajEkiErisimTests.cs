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
/// MESAJ EKİ ERİŞİMİ — İKİ KOL, İKİSİ DE ZORUNLU (MESAJ/3 C7).
///
/// ═══ KOL A ═══
/// Konuşmanın katılımcısı OLMAYAN bir kullanıcı ekin kimliğini
/// doğrudan isterse İÇERİK ALMAZ.
///
/// ═══ KOL B ═══
/// Katılımcı OLAN ama izni Deny ile geri alınmış bir kullanıcı da
/// alamaz.
///
/// ═══ NEDEN İKİ KOL AYRI ═══
///
/// İki kapı iki AYRI şey sınıyor ve biri diğerinin yerini tutmuyor:
///   üyelik -> BU konuşmanın tarafı mı
///   izin   -> mesajlaşma özelliğini kullanabilir mi
///
/// M3/2c-2'de ölçüldü: izin kapısı kanonik çözücüden geçmezse
/// `user_permission_overrides` üzerinden verilen Deny kaydı
/// GÖRÜLMÜYOR. Kol B tam olarak onu tutuyor.
///
/// ═══ POZİTİF KONTROL AYNI DOSYADA ═══
///
/// "Alamıyor" tek başına hiçbir şey kanıtlamaz: servis her zaman
/// `null` dönseydi de iki kol yeşil olurdu. Üçüncü test, YETKİLİ
/// katılımcının eki ALDIĞINI gösteriyor (Kural 48).
/// </summary>
[Collection("Integration")]
public sealed class MesajEkiErisimTests(DatabaseFixture fixture)
{
    private sealed record Kurulum(Guid EkId, Guid UyeId, Guid YabanciId);

    private static async Task<Kurulum> HazirlaAsync(AppDbContext db)
    {
        var ek = Guid.NewGuid().ToString("N")[..6];
        var proje = await TestDataFactory.CreateProjectAsync(db, $"EK{ek}");
        var rol = await db.Roles.SingleAsync(x => x.Name == "Şantiye Şefi");

        async Task<Guid> KullaniciAsync(string ad)
        {
            var k = new AppUser
            {
                Username = $"ek-{ad}-{Guid.NewGuid():N}",
                FullName = $"Ek {ad}",
                PasswordHash = "x",
                PasswordSalt = "x",
                IsActive = true,
                WorkHoursExempt = true
            };
            db.Users.Add(k);
            db.UserRoles.Add(new UserRole { UserId = k.Id, RoleId = rol.Id });
            db.UserDataScopes.Add(new UserDataScope
            {
                UserId = k.Id,
                ScopeType = DataScopeType.All
            });
            await db.SaveChangesAsync();
            return k.Id;
        }

        var uye = await KullaniciAsync("uye");
        var yabanci = await KullaniciAsync("yabanci");

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
            UserId = uye
        });

        var mesaj = new Message
        {
            ConversationId = konusma.Id,
            CompanyId = proje.CompanyId,
            SenderUserId = uye,
            Body = "Ekli mesaj"
        };
        db.Messages.Add(mesaj);

        var attachment = new Attachment
        {
            CompanyId = proje.CompanyId,
            EntityType = MesajEkiKurallari.VarlikTuru,
            EntityId = mesaj.Id,
            Category = MesajEkiKurallari.Kategori,
            StoredName = $"20260908000000_{Guid.NewGuid():N}.pdf",
            OriginalName = "gizli-rapor.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1234,
            UploadedByUserId = uye
        };
        db.Attachments.Add(attachment);

        await db.SaveChangesAsync();
        return new Kurulum(attachment.Id, uye, yabanci);
    }

    private static IMesajEkiServisi Servis(
        DatabaseFixture fixture, IServiceScope scope, Guid userId)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var uploads = scope.ServiceProvider
            .GetRequiredService<Services.Upload.IUploadService>();
        var yetki = scope.ServiceProvider
            .GetRequiredService<IUserAuthorizationService>();

        return new MesajEkiServisi(db, uploads, new SabitKullanici(userId), yetki);
    }

    /// <summary>Testin oturumu — gerçek `ICurrentUserService` yerine.</summary>
    private sealed class SabitKullanici(Guid id)
        : Security.CurrentUser.ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => id;
        public string? Username => "test";
        public string? FullName => "Test";
        public IReadOnlyCollection<string> Roles => [];

        /*
         * İZİNLER BURADAN GELMİYOR — VE BU TESTİN ÖZÜ.
         *
         * `MesajEkiServisi` izni bu arayüzden DEĞİL, kanonik
         * `IUserAuthorizationService`ten okuyor. Buradan boş liste
         * dönmesi hiçbir kapıyı gevşetmiyor; tersine, servisin
         * gerçekten kanonik çözücüye gittiğini gösteriyor —
         * buradan okusaydı Kol B'de Deny kaydını hiç görmezdi.
         */
        public IReadOnlyCollection<string> Permissions => [];
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => false;
    }

    /// <summary>
    /// POZİTİF KONTROL — YETKİLİ KATILIMCI EKİ ALIYOR.
    ///
    /// Bu olmadan aşağıdaki iki kol, servis her zaman `null`
    /// dönseydi de yeşil kalırdı.
    /// </summary>
    [Fact]
    public async Task YetkiliKatilimci_EkiAlabiliyor()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var k = await HazirlaAsync(db);

        var sonuc = await Servis(fixture, scope, k.UyeId)
            .IndirAsync(k.EkId, CancellationToken.None);

        // Dosya diskte YOK (test kaydı), o yüzden `null` dönebilir —
        // ama sebebi YETKİ olmamalı. Ayrımı ListeleAsync ile yapıyoruz:
        // yetkili katılımcı eki GÖRÜYOR.
        var liste = await Servis(fixture, scope, k.UyeId)
            .ListeleAsync([await db.Attachments.Where(x => x.Id == k.EkId)
                .Select(x => x.EntityId).SingleAsync()], CancellationToken.None);

        Assert.Contains(liste, x => x.Id == k.EkId);
        _ = sonuc;
    }

    /// <summary>KOL A — KATILIMCI OLMAYAN ALAMAZ.</summary>
    [Fact]
    public async Task KolA_KatilimciOlmayan_EkiAlamiyor()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var k = await HazirlaAsync(db);

        var sonuc = await Servis(fixture, scope, k.YabanciId)
            .IndirAsync(k.EkId, CancellationToken.None);

        Assert.Null(sonuc);

        var mesajId = await db.Attachments.Where(x => x.Id == k.EkId)
            .Select(x => x.EntityId).SingleAsync();

        var liste = await Servis(fixture, scope, k.YabanciId)
            .ListeleAsync([mesajId], CancellationToken.None);

        Assert.DoesNotContain(liste, x => x.Id == k.EkId);
    }

    /// <summary>
    /// KOL B — KATILIMCI AMA İZNİ Deny İLE ALINMIŞ, ALAMAZ.
    ///
    /// Bağlantıya, üyeliğe, mesaja DOKUNULMUYOR. Yalnız kişisel bir
    /// Deny kaydı yazılıyor — rolde izin duruyor. Kendi EF sorgumu
    /// yazsaydım bu kaydı görmezdim.
    /// </summary>
    [Fact]
    public async Task KolB_IzniAlinmisKatilimci_EkiAlamiyor()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var k = await HazirlaAsync(db);

        // POZİTİF KONTROL: Deny'dan ÖNCE görebiliyor.
        var mesajId = await db.Attachments.Where(x => x.Id == k.EkId)
            .Select(x => x.EntityId).SingleAsync();

        var once = await Servis(fixture, scope, k.UyeId)
            .ListeleAsync([mesajId], CancellationToken.None);
        Assert.Contains(once, x => x.Id == k.EkId);

        var anahtar = await db.Permissions
            .SingleAsync(x => x.Key == PermissionCatalog.Keys.MesajlarView);

        db.UserPermissionOverrides.Add(new UserPermissionOverride
        {
            UserId = k.UyeId,
            PermissionId = anahtar.Id,
            Effect = PermissionOverrideEffect.Deny
        });
        await db.SaveChangesAsync();

        // ÜYELİĞİ YERİNDE — kolun gerçekten izni sınadığının kanıtı.
        Assert.True(await db.ConversationMembers
            .AnyAsync(x => x.UserId == k.UyeId && x.LeftAtUtc == null));

        /*
         * ÖLÇÜM `ListeleAsync` ÜZERİNDEN — VE SEBEBİ ÖLÇÜLDÜ.
         *
         * İlk yazımda `IndirAsync` kullanıyordum ve test TOTOLOJİYDİ:
         * `IndirAsync` diskte gerçek dosya olmadığı için zaten `null`
         * dönüyordu. Sonda bunu yakaladı — izin kapısını kaldırdım,
         * Kol B yine YEŞİL kaldı.
         *
         * `ListeleAsync` diske hiç dokunmuyor; dönen boş liste ancak
         * izin kapısının eseri olabilir. Diske gerçek bir dosya
         * yazmak ise canlı yükleme dizinine yazmak demekti — test
         * üretim deposunu kirletemez.
         */
        using var scope2 = fixture.Factory.Services.CreateScope();

        var sonra = await Servis(fixture, scope2, k.UyeId)
            .ListeleAsync([mesajId], CancellationToken.None);

        Assert.DoesNotContain(sonra, x => x.Id == k.EkId);
    }
}
