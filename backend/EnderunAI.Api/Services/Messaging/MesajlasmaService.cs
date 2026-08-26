using EnderunAI.Api.Data;
using EnderunAI.Api.Hubs;
using EnderunAI.Api.Models.Messaging;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Messaging;

public sealed record KonusmaOzeti(
    Guid Id,
    Guid CompanyId,
    string Baslik,
    Guid? KarsiTarafUserId,
    DateTime? SonMesajZamani,
    string? SonMesajOnizleme,
    int OkunmamisSayisi);

public sealed record MesajOzeti(
    Guid Id,
    Guid KonusmaId,
    Guid GonderenUserId,
    string GonderenAd,
    string Govde,
    DateTime GonderimZamani,
    bool Duzenlendi);

public sealed record KisiOzeti(Guid UserId, string Ad, string? Unvan);

public sealed record SayfaSonucu<T>(IReadOnlyList<T> Kayitlar, bool SonrakiVar);

public interface IMesajlasmaService
{
    Task<SayfaSonucu<KonusmaOzeti>> KonusmalarimAsync(
        DateTime? imlecZaman, Guid? imlecId, int limit, CancellationToken ct);

    Task<KonusmaOzeti> BirebirKonusmaAcAsync(Guid karsiUserId, CancellationToken ct);

    Task<SayfaSonucu<MesajOzeti>> MesajlarAsync(
        Guid konusmaId, DateTime? imlecZaman, Guid? imlecId, int limit, CancellationToken ct);

    Task<MesajOzeti> MesajGonderAsync(Guid konusmaId, string govde, CancellationToken ct);

    Task OkunduIsaretleAsync(Guid konusmaId, CancellationToken ct);

    Task<int> ToplamOkunmamisAsync(CancellationToken ct);

    Task<SayfaSonucu<MesajOzeti>> AraAsync(
        string sorgu, DateTime? imlecZaman, Guid? imlecId, int limit, CancellationToken ct);

    Task<IReadOnlyList<KisiOzeti>> KisiAraAsync(string sorgu, CancellationToken ct);
}

/// <summary>
/// MESAJLAŞMA — ERİŞİM ÜYELİKTEN, KAPSAMDAN DEĞİL.
///
/// İKİ KAPI, İKİSİ DE GEÇİLİR: kapsam yanlış ŞİRKETİN verisini
/// engeller, üyelik doğru şirketteki BAŞKASININ konuşmasını engeller.
/// `HasGlobalAccess` kısayolu bu servisin hiçbir yerinde YOK ve
/// olmamalı — Admin ve Genel Müdür dahil kimse başkasının
/// konuşmasını okuyamaz. `MessagingAccessTests` kaynak taraması bunu
/// koruyor.
///
/// YETKİ ANAHTARI AÇILMADI — BİLİNÇLİ.
/// Mesajlaşma "yetkisi olan görür" işi değil; giriş yapmış herkes
/// kendi konuşmasını görür, kimse başkasınınkini göremez. Yeni bir
/// `messaging.use` anahtarı açsaydım `RoleCatalog` yansıması onu
/// yalnız Admin ve Genel Müdür'e verirdi (`K`/`KWithSensitive`) ve
/// diğer on rolün her birine elle eklemek gerekirdi; biri unutulsa
/// o rol sessizce mesajlaşamazdı. Kapı `[Authorize]` + üyelik.
///
/// KİMİNLE KONUŞULABİLİR: yalnız kapsamdaki şirketlerde çalışan
/// kullanıcılar. Kapsam burada "kimi okuyabilirim" değil "kime
/// yazabilirim" sorusunu cevaplıyor.
/// </summary>
public sealed class MesajlasmaService(
    AppDbContext db,
    ICurrentUserService currentUser,
    ICurrentDataScopeService dataScope,
    IHubContext<MesajHub> hub) : IMesajlasmaService
{
    /// <summary>Liste önizlemesinde gösterilen en fazla karakter.</summary>
    private const int OnizlemeUzunlugu = 120;

    /// <summary>Bir mesajın en fazla uzunluğu.</summary>
    public const int EnFazlaGovdeUzunlugu = 4000;

    private Guid KullaniciId() =>
        currentUser.UserId
        ?? throw new InvalidOperationException("Oturum bulunamadı.");

    /// <summary>
    /// Kapsamdaki şirket kimlikleri. Global erişimli kullanıcıda
    /// KISAYOL YOK: burada da şirket listesi çıkarılıyor, çünkü bu
    /// küme "kime yazabilirim" sorusunun cevabı ve global erişim onu
    /// sınırsız yapmamalı — yalnız görebildiği şirketlerle sınırlı.
    /// </summary>
    private async Task<IReadOnlyCollection<Guid>> SirketlerAsync(CancellationToken ct)
    {
        var kapsam = await dataScope.GetAsync(ct);

        if (kapsam is null) return [];

        if (kapsam.HasGlobalAccess)
        {
            return await db.Companies.Select(x => x.Id).ToListAsync(ct);
        }

        return kapsam.VisibleCompanyIds.Concat(kapsam.CompanyIds).Distinct().ToList();
    }

    public async Task<SayfaSonucu<KonusmaOzeti>> KonusmalarimAsync(
        DateTime? imlecZaman, Guid? imlecId, int limit, CancellationToken ct)
    {
        var userId = KullaniciId();
        var sirketler = await SirketlerAsync(ct);

        // KEYSET — COUNT(*) YOK. Konuşma listesi "en son konuşulan
        // üstte" sıralı; imleç (SonMesajZamani, Id) ikilisi.
        var sorgu = db.Conversations
            .AsNoTracking()
            .ApplyMembership(userId)
            .Where(x => sirketler.Contains(x.CompanyId) && !x.IsArchived);

        if (imlecZaman is not null && imlecId is not null)
        {
            sorgu = sorgu.Where(x =>
                x.LastMessageAtUtc < imlecZaman
                || (x.LastMessageAtUtc == imlecZaman && x.Id.CompareTo(imlecId.Value) < 0));
        }

        var sayfa = await sorgu
            .OrderByDescending(x => x.LastMessageAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(limit + 1)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                x.Type,
                x.Title,
                x.LastMessageAtUtc,
                BenimUyeligim = x.Members
                    .Where(m => m.UserId == userId && m.LeftAtUtc == null)
                    .Select(m => m.LastReadAtUtc)
                    .FirstOrDefault(),
                KarsiTaraf = x.Members
                    .Where(m => m.UserId != userId && m.LeftAtUtc == null)
                    .Select(m => m.UserId)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var sonrakiVar = sayfa.Count > limit;
        if (sonrakiVar) sayfa.RemoveAt(sayfa.Count - 1);

        var konusmaIdleri = sayfa.Select(x => x.Id).ToList();
        var karsiIdler = sayfa.Select(x => x.KarsiTaraf).Where(x => x != Guid.Empty).ToList();

        var adlar = await db.Users
            .AsNoTracking()
            .Where(x => karsiIdler.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName, ct);

        // SON MESAJ ÖNİZLEMESİ TEK SORGUDA. Konuşma başına sorgu
        // atmak, liste açılışında N+1 demekti.
        // ÜYELİK BURADA DA YAZILI — kimlik listesi zaten üyelikle
        // süzülmüş bir sorgudan geliyor, yani teknik olarak gereksiz.
        // Ama "önceki sorgu süzmüştü" güvencesi okuma yerinde
        // GÖRÜNMÜYOR ve zamanla çürür: biri kimlik listesini başka
        // yerden doldurduğunda kapı sessizce açılırdı.
        //
        // YORUM ZİNCİRİN İÇİNDE DEĞİL ÜSTÜNDE: kapsam tarayıcısı
        // yorumları UZUNLUĞU KORUYARAK boşluğa çeviriyor ve kapıyı
        // okumadan sonraki 400 karakterlik pencerede arıyor. Zincirin
        // içine yazılan uzun yorum, kapı yerinde dururken tarayıcıyı
        // kör ediyordu.
        var sonMesajlar = await db.Messages
            .AsNoTracking()
            .ApplyMembership(userId)
            .Where(x => konusmaIdleri.Contains(x.ConversationId) && x.HiddenAtUtc == null)
            .GroupBy(x => x.ConversationId)
            .Select(g => new
            {
                KonusmaId = g.Key,
                Govde = g.OrderByDescending(m => m.CreatedAtUtc).ThenByDescending(m => m.Id)
                    .Select(m => m.Body).FirstOrDefault()
            })
            .ToDictionaryAsync(x => x.KonusmaId, x => x.Govde, ct);

        var okunmamislar = await OkunmamisSayilariAsync(userId, konusmaIdleri, ct);

        var kayitlar = sayfa.Select(x => new KonusmaOzeti(
            x.Id,
            x.CompanyId,
            BaslikUret(x.Type, x.Title, x.KarsiTaraf, adlar),
            x.KarsiTaraf == Guid.Empty ? null : x.KarsiTaraf,
            x.LastMessageAtUtc,
            Onizleme(sonMesajlar.GetValueOrDefault(x.Id)),
            okunmamislar.GetValueOrDefault(x.Id))).ToList();

        return new SayfaSonucu<KonusmaOzeti>(kayitlar, sonrakiVar);
    }

    /// <summary>
    /// BİREBİR KONUŞMA: VARSA GETİRİR, YOKSA AÇAR.
    ///
    /// "Yoksa aç" olmadan kullanıcı kiminle konuşacağını seçemez;
    /// her seferinde yeni konuşma açsaydı aynı iki kişi arasında
    /// onlarca kopya birikir ve geçmiş parçalanırdı.
    /// </summary>
    public async Task<KonusmaOzeti> BirebirKonusmaAcAsync(
        Guid karsiUserId, CancellationToken ct)
    {
        var userId = KullaniciId();

        // KENDİNE MESAJ YOK. Dar olan seçildi: "kendine not" ayrı bir
        // ihtiyaç ve `yapilacaklar` orada duruyor.
        if (karsiUserId == userId)
        {
            throw new InvalidOperationException("Kendinizle konuşma açamazsınız.");
        }

        var sirketler = await SirketlerAsync(ct);

        var karsi = await db.Users
            .AsNoTracking()
            .Where(x => x.Id == karsiUserId && x.IsActive)
            .Select(x => new { x.Id, x.FullName, x.PersonnelId })
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Kişi bulunamadı.");

        var sirketId = await KisininSirketiAsync(karsiUserId, sirketler, ct)
            ?? throw new InvalidOperationException(
                "Bu kişiye mesaj gönderemezsiniz: yetki alanınızdaki bir "
                + "şirkette çalışmıyor.");

        // MEVCUT KONUŞMA ARANIYOR: iki tarafın da AKTİF üye olduğu,
        // birebir tipinde konuşma.
        var mevcut = await db.Conversations
            .AsNoTracking()
            .ApplyMembership(userId)
            .Where(x => x.Type == ConversationType.Direct
                        && x.CompanyId == sirketId
                        && x.Members.Any(m => m.UserId == karsiUserId && m.LeftAtUtc == null))
            .Select(x => new { x.Id, x.CompanyId, x.LastMessageAtUtc })
            .FirstOrDefaultAsync(ct);

        if (mevcut is not null)
        {
            return new KonusmaOzeti(
                mevcut.Id, mevcut.CompanyId, karsi.FullName, karsiUserId,
                mevcut.LastMessageAtUtc, null, 0);
        }

        var konusma = new Conversation
        {
            CompanyId = sirketId,
            Type = ConversationType.Direct,
            Title = null
        };

        db.Conversations.Add(konusma);

        db.ConversationMembers.Add(new ConversationMember
        {
            Conversation = konusma,
            UserId = userId
        });

        db.ConversationMembers.Add(new ConversationMember
        {
            Conversation = konusma,
            UserId = karsiUserId
        });

        await db.SaveChangesAsync(ct);

        return new KonusmaOzeti(
            konusma.Id, sirketId, karsi.FullName, karsiUserId, null, null, 0);
    }

    public async Task<SayfaSonucu<MesajOzeti>> MesajlarAsync(
        Guid konusmaId, DateTime? imlecZaman, Guid? imlecId, int limit, CancellationToken ct)
    {
        var userId = KullaniciId();

        await UyeMiyimAsync(konusmaId, userId, ct);

        var sorgu = db.Messages
            .AsNoTracking()
            .ApplyMembership(userId)
            .Where(x => x.ConversationId == konusmaId && x.HiddenAtUtc == null);

        if (imlecZaman is not null && imlecId is not null)
        {
            sorgu = sorgu.Where(x =>
                x.CreatedAtUtc < imlecZaman
                || (x.CreatedAtUtc == imlecZaman && x.Id.CompareTo(imlecId.Value) < 0));
        }

        return await SayfalaAsync(sorgu, limit, ct);
    }

    public async Task<MesajOzeti> MesajGonderAsync(
        Guid konusmaId, string govde, CancellationToken ct)
    {
        var userId = KullaniciId();

        var temiz = (govde ?? string.Empty).Trim();

        if (temiz.Length == 0)
        {
            throw new InvalidOperationException("Boş mesaj gönderilemez.");
        }

        if (temiz.Length > EnFazlaGovdeUzunlugu)
        {
            throw new InvalidOperationException(
                $"Mesaj en fazla {EnFazlaGovdeUzunlugu} karakter olabilir.");
        }

        var konusma = await UyeMiyimAsync(konusmaId, userId, ct);

        var mesaj = new Message
        {
            ConversationId = konusmaId,
            CompanyId = konusma.CompanyId,
            SenderUserId = userId,
            Body = temiz
        };

        db.Messages.Add(mesaj);

        // SON MESAJ ZAMANI KONUŞMADA TUTULUYOR: liste sıralaması için
        // mesaj tablosuna MAX() atmak konuşma başına bir sorgu demekti.
        konusma.LastMessageAtUtc = mesaj.CreatedAtUtc;

        // GÖNDEREN KENDİ MESAJINI OKUMUŞ SAYILIR. Sayılmasaydı kişi
        // kendi yazdığı mesajdan ötürü okunmamış rozeti görürdü.
        var kendiUyeligim = await db.ConversationMembers
            .FirstAsync(x => x.ConversationId == konusmaId
                             && x.UserId == userId
                             && x.LeftAtUtc == null, ct);

        kendiUyeligim.LastReadAtUtc = mesaj.CreatedAtUtc;

        await db.SaveChangesAsync(ct);

        var ozet = new MesajOzeti(
            mesaj.Id, konusmaId, userId, currentUser.FullName ?? "", temiz,
            mesaj.CreatedAtUtc, false);

        await YayinlaAsync(konusmaId, ozet, ct);

        return ozet;
    }

    public async Task OkunduIsaretleAsync(Guid konusmaId, CancellationToken ct)
    {
        var userId = KullaniciId();
        await UyeMiyimAsync(konusmaId, userId, ct);

        var uyelik = await db.ConversationMembers
            .FirstAsync(x => x.ConversationId == konusmaId
                             && x.UserId == userId
                             && x.LeftAtUtc == null, ct);

        uyelik.LastReadAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> ToplamOkunmamisAsync(CancellationToken ct)
    {
        var userId = KullaniciId();

        var sayilar = await OkunmamisSayilariAsync(userId, konusmaIdleri: null, ct);

        return sayilar.Values.Sum();
    }

    public async Task<SayfaSonucu<MesajOzeti>> AraAsync(
        string sorgu, DateTime? imlecZaman, Guid? imlecId, int limit, CancellationToken ct)
    {
        var userId = KullaniciId();

        // ÜÇ HARF KURALI SUNUCUDA — ekran kuralı yalnız kolaylık.
        // Ekran atlanabilir (doğrudan uç çağrılabilir); sunucu
        // atlanamaz. Karar tek yerde: MesajAramaKurali.
        if (!MesajAramaKurali.Gecerli(sorgu))
        {
            throw new InvalidOperationException(MesajAramaKurali.Uyari);
        }

        var katlanmis = MesajAramaKurali.Normalize(sorgu);

        var arama = db.Messages
            .AsNoTracking()
            .ApplyMembership(userId)
            .Where(x => x.HiddenAtUtc == null
                        && x.SearchFold != null
                        && EF.Functions.Like(x.SearchFold, $"%{katlanmis}%"));

        if (imlecZaman is not null && imlecId is not null)
        {
            arama = arama.Where(x =>
                x.CreatedAtUtc < imlecZaman
                || (x.CreatedAtUtc == imlecZaman && x.Id.CompareTo(imlecId.Value) < 0));
        }

        return await SayfalaAsync(arama, limit, ct);
    }

    public async Task<IReadOnlyList<KisiOzeti>> KisiAraAsync(
        string sorgu, CancellationToken ct)
    {
        var userId = KullaniciId();

        if (!MesajAramaKurali.Gecerli(sorgu))
        {
            throw new InvalidOperationException(MesajAramaKurali.Uyari);
        }

        var katlanmis = MesajAramaKurali.Normalize(sorgu);
        var sirketler = await SirketlerAsync(ct);

        // KİŞİ LİSTESİ KAPSAMLA SINIRLI: yalnız kapsamdaki
        // şirketlerde personel kaydı olan aktif kullanıcılar.
        // Personel kaydı olmayan (yalnız sistem) kullanıcılar
        // listede çıkmıyor — dar olan seçildi.
        return await db.Users
            .AsNoTracking()
            .Where(x => x.IsActive
                        && x.Id != userId
                        && x.PersonnelId != null
                        && db.Personnel.Any(p =>
                            p.Id == x.PersonnelId && sirketler.Contains(p.CompanyId)))
            // KATLAMA VERİTABANINDA: `AppDbContext.Fold` zaten
            // `enderun_fold`a bağlı ve mesaj arama da aynı fonksiyonu
            // kullanıyor. İkinci bir katlama yazsaydım aynı arama
            // kişide bulur, mesajda bulmazdı.
            //
            // Kullanıcı adında ÜRETİLMİŞ kolon yok; katlama sorgu
            // anında yapılıyor. Kişi listesi küçük (şirket başına
            // yüzler mertebesi) olduğundan kabul edilebilir — mesajda
            // kabul edilemezdi, orada üretilmiş kolon + GIN var.
            .Where(x => EF.Functions.Like(
                AppDbContext.Fold(x.FullName), $"%{katlanmis}%"))
            .OrderBy(x => x.FullName)
            .Take(20)
            .Select(x => new KisiOzeti(x.Id, x.FullName, x.Honorific))
            .ToListAsync(ct);
    }

    private static string BaslikUret(
        ConversationType tur, string? baslik, Guid karsiTaraf,
        IReadOnlyDictionary<Guid, string> adlar)
    {
        if (tur == ConversationType.Channel) return baslik ?? "Kanal";

        return karsiTaraf != Guid.Empty && adlar.TryGetValue(karsiTaraf, out var ad)
            ? ad
            : "Konuşma";
    }

    private static string? Onizleme(string? govde)
    {
        if (string.IsNullOrWhiteSpace(govde)) return null;

        var tekSatir = govde.ReplaceLineEndings(" ").Trim();

        return tekSatir.Length <= OnizlemeUzunlugu
            ? tekSatir
            : tekSatir[..OnizlemeUzunlugu] + "…";
    }

    /// <summary>
    /// ÜYELİK KAPISI — HER UÇTA. Üye değilse konuşma "yok" sayılır:
    /// "yetkiniz yok" demek, konuşmanın VAR OLDUĞUNU söylerdi.
    /// </summary>
    private async Task<Conversation> UyeMiyimAsync(
        Guid konusmaId, Guid userId, CancellationToken ct) =>
        await db.Conversations
            .ApplyMembership(userId)
            .FirstOrDefaultAsync(x => x.Id == konusmaId, ct)
        ?? throw new InvalidOperationException("Konuşma bulunamadı.");

    private async Task<Guid?> KisininSirketiAsync(
        Guid karsiUserId, IReadOnlyCollection<Guid> sirketler, CancellationToken ct) =>
        await db.Users
            .AsNoTracking()
            .Where(x => x.Id == karsiUserId && x.PersonnelId != null)
            .Join(db.Personnel, u => u.PersonnelId, p => p.Id, (u, p) => p.CompanyId)
            .Where(x => sirketler.Contains(x))
            .Cast<Guid?>()
            .FirstOrDefaultAsync(ct);

    private async Task<Dictionary<Guid, int>> OkunmamisSayilariAsync(
        Guid userId, IReadOnlyCollection<Guid>? konusmaIdleri, CancellationToken ct)
    {
        // ÜYELİK SÜZGECİYLE OKUNUYOR, `ConversationMembers` DOĞRUDAN
        // DEĞİL. İkisi de aynı satırlara varıyor ama bu biçimde kapı
        // okuma yerinde görünüyor; `x.UserId == userId` yazmak da bir
        // kapıydı, fakat okuyanın onu kapı olarak TANIMASI gerekiyordu.
        var konusmalar = db.Conversations
            .AsNoTracking()
            .ApplyMembership(userId);

        if (konusmaIdleri is not null)
        {
            konusmalar = konusmalar.Where(x => konusmaIdleri.Contains(x.Id));
        }

        // OKUNMAMIŞ = benim GÖNDERMEDİĞİM ve son okuma zamanımdan
        // SONRA yazılmış mesaj. Hiç okumadıysam hepsi okunmamış.
        return await konusmalar
            .Select(c => new
            {
                KonusmaId = c.Id,
                SonOkuma = c.Members
                    .Where(m => m.UserId == userId && m.LeftAtUtc == null)
                    .Select(m => m.LastReadAtUtc)
                    .FirstOrDefault()
            })
            .Select(x => new
            {
                x.KonusmaId,
                Sayi = db.Messages.Count(m =>
                    m.ConversationId == x.KonusmaId
                    && m.HiddenAtUtc == null
                    && m.SenderUserId != userId
                    && (x.SonOkuma == null || m.CreatedAtUtc > x.SonOkuma))
            })
            .ToDictionaryAsync(x => x.KonusmaId, x => x.Sayi, ct);
    }

    private async Task<SayfaSonucu<MesajOzeti>> SayfalaAsync(
        IQueryable<Message> sorgu, int limit, CancellationToken ct)
    {
        var sayfa = await sorgu
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(limit + 1)
            .Select(x => new
            {
                x.Id,
                x.ConversationId,
                x.SenderUserId,
                x.Body,
                x.CreatedAtUtc,
                x.EditedAtUtc
            })
            .ToListAsync(ct);

        var sonrakiVar = sayfa.Count > limit;
        if (sonrakiVar) sayfa.RemoveAt(sayfa.Count - 1);

        var gonderenler = sayfa.Select(x => x.SenderUserId).Distinct().ToList();

        var adlar = await db.Users
            .AsNoTracking()
            .Where(x => gonderenler.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName, ct);

        var kayitlar = sayfa.Select(x => new MesajOzeti(
            x.Id,
            x.ConversationId,
            x.SenderUserId,
            adlar.GetValueOrDefault(x.SenderUserId, ""),
            x.Body,
            x.CreatedAtUtc,
            x.EditedAtUtc is not null)).ToList();

        return new SayfaSonucu<MesajOzeti>(kayitlar, sonrakiVar);
    }

    /// <summary>
    /// GERÇEK ZAMANLI YAYIN — YALNIZ ÜYELERE.
    ///
    /// Hub'da konuşma başına grup YOK; kullanıcı başına grup var
    /// (M3/0). Mesaj, o anki AKTİF üyelerin kişisel gruplarına tek
    /// tek gönderiliyor. Konuşma grubuna yayın yapsaydık, ayrılan
    /// üyenin bağlantısı grupta kaldığı sürece mesaj almaya devam
    /// ederdi — erişim kapısı REST'te kapalı, yayında açık kalırdı.
    /// </summary>
    private async Task YayinlaAsync(Guid konusmaId, MesajOzeti ozet, CancellationToken ct)
    {
        // GÖNDERENİN ÜYELİĞİ ÜZERİNDEN OKUNUYOR. Doğrudan
        // `ConversationMembers` okusaydık kapı yalnız "çağıran daha
        // önce kontrol etmişti" varsayımına dayanırdı.
        var uyeler = await db.Conversations
            .AsNoTracking()
            .ApplyMembership(ozet.GonderenUserId)
            .Where(x => x.Id == konusmaId)
            .SelectMany(x => x.Members
                .Where(m => m.LeftAtUtc == null)
                .Select(m => m.UserId))
            .ToListAsync(ct);

        foreach (var uye in uyeler)
        {
            await hub.Clients
                .Group(MesajHub.KullaniciGrubu(uye))
                .SendAsync("MesajGeldi", ozet, ct);
        }
    }
}
