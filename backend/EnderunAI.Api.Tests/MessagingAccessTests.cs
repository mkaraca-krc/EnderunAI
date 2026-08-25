using EnderunAI.Api.Data;
using EnderunAI.Api.Models.Messaging;
using EnderunAI.Api.Security;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// MESAJLAŞMA ERİŞİM KAPISI — ÜYELİK, KAPSAM DEĞİL.
///
/// Sistemdeki diğer her süzgeç `HasGlobalAccess` için bir kısayol
/// taşıyor: global erişimli kullanıcıda sorgu olduğu gibi geçiyor.
/// Mesajlaşmada bu kısayol YOK ve OLMAMALI — karar açık: kimse
/// başkasının konuşmasını okuyamaz, Genel Müdür dahil.
///
/// BU TESTLERİN ASIL İŞİ: o kısayolun bir gün "tutarlılık olsun"
/// diye eklenmesini yakalamak. Kural yorumda değil burada duruyor.
/// </summary>
[Collection("Integration")]
public sealed class MessagingAccessTests(DatabaseFixture fixture)
{
    private static async Task<(Guid ConversationId, Guid UyeId, Guid YabanciId)>
        KonusmaAsync(AppDbContext db, string suffix, DateTime? uyeAyrilma = null)
    {
        /*
         * ŞİRKET KENDİ KURULUYOR — VARLIĞI VARSAYILMIYOR.
         *
         * Önce `db.Companies.First()` yazıyordu ve test "veritabanında
         * bir şirket vardır" diye varsayıyordu. Fixture koşu başına
         * veritabanını düşürüp yeniden kuruyor; o anda şirket
         * olmayabiliyor ve test "Sequence contains no elements" ile
         * düşüyordu — sınamak istediği şeye hiç gelemeden.
         *
         * Bugün §7b'ye yazılan kuralın aynısı: test kendi önkoşulunu
         * GARANTİ eder, varlığını varsaymaz.
         */
        var proje = await TestDataFactory.CreateProjectAsync(db, $"MSJ{suffix}");
        var sirket = proje.CompanyId;

        var uye = Guid.NewGuid();
        var yabanci = Guid.NewGuid();

        var konusma = new Conversation
        {
            CompanyId = sirket,
            Type = ConversationType.Direct,
            LastMessageAtUtc = DateTime.UtcNow
        };

        db.Conversations.Add(konusma);

        db.ConversationMembers.Add(new ConversationMember
        {
            ConversationId = konusma.Id,
            UserId = uye,
            LeftAtUtc = uyeAyrilma
        });

        db.Messages.Add(new Message
        {
            ConversationId = konusma.Id,
            CompanyId = sirket,
            SenderUserId = uye,
            Body = $"Gizli mesaj {suffix}"
        });

        await db.SaveChangesAsync();

        return (konusma.Id, uye, yabanci);
    }

    [Fact]
    public async Task Uye_KendiKonusmasiniGorur()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (konusmaId, uye, _) = await KonusmaAsync(db, suffix);

        var gorulen = await db.Conversations
            .AsNoTracking()
            .ApplyMembership(uye)
            .CountAsync(x => x.Id == konusmaId);

        Assert.Equal(1, gorulen);
    }

    /// <summary>
    /// ASIL KURAL: taraf olmayan göremez.
    ///
    /// Bu kullanıcı AYNI ŞİRKETTE — yani şirket kapsamı onu
    /// engellemezdi. Engelleyen tek şey üyelik kapısı.
    /// </summary>
    [Fact]
    public async Task Yabanci_BaskasininKonusmasiniGoremez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (konusmaId, _, yabanci) = await KonusmaAsync(db, suffix);

        var gorulen = await db.Conversations
            .AsNoTracking()
            .ApplyMembership(yabanci)
            .CountAsync(x => x.Id == konusmaId);

        Assert.Equal(0, gorulen);
    }

    [Fact]
    public async Task Yabanci_BaskasininMesajlariniGoremez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (konusmaId, _, yabanci) = await KonusmaAsync(db, suffix);

        var gorulen = await db.Messages
            .AsNoTracking()
            .ApplyMembership(yabanci)
            .CountAsync(x => x.ConversationId == konusmaId);

        Assert.Equal(0, gorulen);
    }

    /// <summary>
    /// AYRILAN ÜYE OKUYAMAZ.
    ///
    /// Bugünkü davranış DAR: `LeftAtUtc` dolu olan hiçbir şey görmez,
    /// ayrıldığı tarihe kadarki mesajları da göremez. "Ayrıldığı
    /// tarihe kadarki geçmişi görür" kuralı departman KANALLARI
    /// bağlamında konuşulmuştu; kanallar M3/3'te gelecek ve orada
    /// yeniden ele alınacak. Dar olan seçildi.
    /// </summary>
    [Fact]
    public async Task AyrilanUye_Goremez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (konusmaId, uye, _) = await KonusmaAsync(
            db, suffix, uyeAyrilma: DateTime.UtcNow.AddMinutes(-5));

        var gorulen = await db.Conversations
            .AsNoTracking()
            .ApplyMembership(uye)
            .CountAsync(x => x.Id == konusmaId);

        Assert.Equal(0, gorulen);
    }

    /// <summary>
    /// KAPI KAYNAĞINDA `HasGlobalAccess` GEÇMEZ.
    ///
    /// Kaynak taraması, çalışma zamanı testinin yakalayamayacağı bir
    /// şeyi yakalıyor: kısayolun EKLENMESİNİ. Çalışma zamanında
    /// yakalamak için global erişimli bir kullanıcıyla test kurmak
    /// gerekirdi ve o test, kısayol eklendiğinde bile yalnız o
    /// kullanıcı için kırmızıya dönerdi.
    /// </summary>
    [Fact]
    public void ErisimKapisi_GlobalKapsamKisayoluTasimaz()
    {
        var kok = AppContext.BaseDirectory;
        var dizin = new DirectoryInfo(kok);

        while (dizin is not null &&
               !Directory.Exists(Path.Combine(dizin.FullName, "EnderunAI.Api")))
        {
            dizin = dizin.Parent;
        }

        Assert.True(dizin is not null, "Çözüm kökü bulunamadı.");

        var yol = Path.Combine(
            dizin!.FullName, "EnderunAI.Api", "Security", "MessagingAccessExtensions.cs");

        Assert.True(File.Exists(yol), $"Erişim kapısı dosyası yok: {yol}");

        var kod = File.ReadAllText(yol);

        // Yorumlarda GEÇEBİLİR (neden olmadığını anlatıyor); KODDA geçemez.
        var kodSatirlari = kod.Split('\n')
            .Where(x => !x.TrimStart().StartsWith("///") && !x.TrimStart().StartsWith("*")
                        && !x.TrimStart().StartsWith("//") && !x.TrimStart().StartsWith("/*"))
            .ToList();

        Assert.DoesNotContain(
            kodSatirlari,
            satir => satir.Contains("HasGlobalAccess", StringComparison.Ordinal));
    }

    /// <summary>
    /// AYRILAN ÜYE YENİDEN EKLENEBİLMELİ.
    ///
    /// Üyelik satırı silinmiyor, LeftAtUtc ile tarihleniyor: "o
    /// tarihte kim görüyordu" sorusunun tek cevabı o satır. Bu
    /// tasarımın bedeli, benzersizlik indeksinin KISMİ olma
    /// zorunluluğudur.
    ///
    /// KOŞULSUZ bir UNIQUE(ConversationId, UserId) ikinci satırı
    /// reddeder ve kişi o konuşmaya BİR DAHA EKLENEMEZ. Bu hata
    /// canlıya çıktı ve ölçümle yakalandı: EF, filtre yazılmadığı
    /// için indeksi koşulsuz üretmişti.
    ///
    /// IsDeleted süzgeci burada YETMEZ — benzersizlik veritabanı
    /// düzeyinde uygulanıyor, EF sorgu süzgeci oraya işlemiyor.
    /// Bu yüzden test veritabanına gerçekten YAZIYOR; süzgeci
    /// gözlemleyen bir test bu kusuru göremezdi.
    /// </summary>
    [Fact]
    public async Task AyrilanUye_AyniKonusmayaYenidenEklenebilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (konusmaId, uye, _) = await KonusmaAsync(db, suffix, uyeAyrilma: DateTime.UtcNow.AddDays(-1));

        db.ConversationMembers.Add(new ConversationMember
        {
            ConversationId = konusmaId,
            UserId = uye,
            LeftAtUtc = null
        });

        await db.SaveChangesAsync();

        var aktif = await db.ConversationMembers
            .AsNoTracking()
            .CountAsync(x => x.ConversationId == konusmaId && x.UserId == uye && x.LeftAtUtc == null);

        var toplam = await db.ConversationMembers
            .AsNoTracking()
            .CountAsync(x => x.ConversationId == konusmaId && x.UserId == uye);

        Assert.Equal(1, aktif);
        Assert.Equal(2, toplam);
    }

    /// <summary>
    /// AYNI ANDA İKİ AKTİF ÜYELİK OLAMAZ.
    ///
    /// Yukarıdaki testin ters yönü. Kısmi indeks yalnız "yeniden
    /// eklenebilsin" demiyor; aktif üyeliğin TEK olduğunu da garanti
    /// ediyor. Filtre tümüyle kaldırılırsa üstteki test kırmızıya
    /// döner, filtre fazla genişletilirse bu test kırmızıya döner —
    /// ikisi birlikte indeksi iki taraftan sıkıştırıyor.
    /// </summary>
    [Fact]
    public async Task AyniKisi_IkiKezAktifUyeOlamaz()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (konusmaId, uye, _) = await KonusmaAsync(db, suffix);

        db.ConversationMembers.Add(new ConversationMember
        {
            ConversationId = konusmaId,
            UserId = uye,
            LeftAtUtc = null
        });

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
