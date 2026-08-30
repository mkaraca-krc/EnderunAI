using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Contracts.Core;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Common;
using EnderunAI.Api.Security.CurrentUser;
using Microsoft.EntityFrameworkCore;
using EnderunAI.Api.Search;

namespace EnderunAI.Api.Services.Accounting;

public sealed class AccountingAccountService(
    AppDbContext dbContext,
    ICurrentUserService currentUser)
    : IAccountingAccountService
{
    /// <summary>
    /// K9 — DENETİM KAYDI: kim, ne zaman, eski değer, yeni değer.
    ///
    /// ÜÇ EYLEM AYRI AYRI yazılıyor (`ekle` / `ad-degistir` /
    /// `pasife-al` / `geri-ac`). Tek bir "guncelle" eylemi
    /// yazılsaydı, hesap planında en çok merak edilen soru —
    /// "bu hesabın adını kim değiştirdi" — kayıttan çıkarılamazdı.
    ///
    /// ESKİ DEĞER OLMADAN KAYIT YARIM: "ad değişti" demek yetmez,
    /// neyden neye değiştiği gerekir. Bu paketin var olma sebebi
    /// zaten "Banka 1" gibi adların düzeltilmesi.
    ///
    /// DESEN `DepodanZimmetService.DenetimYaz` İLE AYNI — ikinci bir
    /// denetim biçimi açılmıyor.
    /// </summary>
    private void DenetimYaz(string eylem, Guid hesapId, object ayrinti) =>
        dbContext.SecurityAuditEvents.Add(new SecurityAuditEvent
        {
            ActorUserId = currentUser.UserId,
            ActorUsername = currentUser.Username,
            Action = eylem,
            EntityType = nameof(AccountingAccount),
            EntityId = hesapId,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(ayrinti)
        });

    public async Task<IReadOnlyCollection<AccountingAccountListItemResponse>>
        GetAllAsync(
            Guid? companyId,
            Guid? parentAccountId,
            bool? isActive,
            string? search,
            CancellationToken cancellationToken)
    {
        var query = dbContext.AccountingAccounts.AsNoTracking();

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        if (parentAccountId.HasValue)
            query = query.Where(x => x.ParentAccountId == parentAccountId.Value);

        if (isActive.HasValue)
            query = query.Where(x => x.IsActive == isActive.Value);

        query = ApplySearch(query, search);

        return await query
            .OrderBy(x => x.Code)
            .Select(x => new AccountingAccountListItemResponse(
                x.Id,
                x.CompanyId,
                x.ParentAccountId,
                x.Code,
                x.Name,
                (int)x.Nature,
                x.Level,
                x.IsPostingAllowed,
                x.RequiresProject,
                x.RequiresCostCenter,
                x.CurrencyCode,
                x.IsActive,
                x.ChildAccounts.Count))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// KATLANMIŞ ARAMA — kural `lib/search/fold.ts` ile aynı.
    ///
    /// Eskiden `x.Code.ToLower().Contains(...)` yazıyordu: küçültme
    /// doğruydu (veritabanı kültürü C.UTF-8) ama TÜRKÇE KATLAMA YOKTU —
    /// "sube" yazan "Şube"yi bulamıyordu. Ekranda bulunan bir kaydın
    /// sunucuda bulunamaması, sayfalı aramada kaydın hiç yokmuş gibi
    /// görünmesi demek.
    ///
    /// Katlama üretilmiş kolonda hazır duruyor; burada yalnızca ARANAN
    /// metin aynı kuralla katlanıyor.
    /// </summary>
    private static IQueryable<AccountingAccount> ApplySearch(
        IQueryable<AccountingAccount> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return query;

        var folded = TurkishSearch.Fold(search);

        return query.Where(x => x.SearchFold.Contains(folded));
    }

    /// <summary>
    /// Aranabilir seçicinin ucu: en fazla <paramref name="limit"/> satır
    /// ve TOPLAM eşleşme sayısı.
    /// </summary>
    public async Task<PagedResult<AccountingAccountListItemResponse>> SearchAsync(
        Guid? companyId,
        bool? isActive,
        string? search,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.AccountingAccounts.AsNoTracking();

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        if (isActive.HasValue)
            query = query.Where(x => x.IsActive == isActive.Value);

        query = ApplySearch(query, search);

        // Toplam sayı AYRI sorgulanıyor: satırlarla birlikte alınsaydı
        // limit toplamı da kırpardı ve "kaç kayıt daha var" cevabı
        // kendi kendini yanlışlardı.
        var total = await query.CountAsync(cancellationToken);

        var take = Math.Clamp(limit, 1, 200);

        var items = await query
            .OrderBy(x => x.Code)
            .Take(take)
            .Select(x => new AccountingAccountListItemResponse(
                x.Id,
                x.CompanyId,
                x.ParentAccountId,
                x.Code,
                x.Name,
                (int)x.Nature,
                x.Level,
                x.IsPostingAllowed,
                x.RequiresProject,
                x.RequiresCostCenter,
                x.CurrencyCode,
                x.IsActive,
                x.ChildAccounts.Count))
            .ToListAsync(cancellationToken);

        /*
         * PagedResult — KOD TABANININ MEVCUT SÖZLEŞMESİ.
         *
         * Önce buraya özel bir yanıt tipi yazılmıştı (Items +
         * TotalCount). Sözleşme testi yakaladı: çağıranın tavan
         * verebildiği her uç PagedResult döndürmek zorunda. Paralel bir
         * şekil, arayüzde ikinci bir "kırpıldı mı" okuma biçimi
         * demekti — poz ekranındaki hatanın tekrar doğmasına açık kapı.
         */
        return PagedResult<AccountingAccountListItemResponse>.From(items, total, take);
    }

    public async Task<AccountingAccountDetailResponse> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        /*
         * SÜRÜM DAMGASI SORGUDA OKUNUYOR.
         *
         * `AsNoTracking` ile gölge özellik izleyiciden okunamaz;
         * `EF.Property` onu SELECT'e koyuyor. Bu uç düzenleme
         * formunu besliyor, yani sürümün DOĞRU gelmesi zorunlu —
         * 0 gelirse kullanıcı kaydı hiç düzenleyemez.
         */
        var satir = await dbContext.AccountingAccounts
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                Hesap = x,
                Surum = x.UpdatedAtUtc ?? x.CreatedAtUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (satir is null)
            throw new KeyNotFoundException("Muhasebe hesabı bulunamadı.");

        return MapDetail(satir.Hesap, satir.Surum);
    }

    public async Task<AccountingAccountDetailResponse> CreateAsync(
        CreateAccountingAccountRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request.Code, request.Name, request.Nature);

        var companyExists = await dbContext.Companies
            .AnyAsync(x => x.Id == request.CompanyId, cancellationToken);

        if (!companyExists)
            throw new ArgumentException("Şirket bulunamadı.");

        var code = request.Code.Trim();
        var duplicateExists = await dbContext.AccountingAccounts
            .AnyAsync(
                x => x.CompanyId == request.CompanyId && x.Code == code,
                cancellationToken);

        if (duplicateExists)
            throw new InvalidOperationException(
                "Aynı şirkette bu hesap kodu zaten kullanılıyor.");

        // K5 — eksik üst hesap otomatik açılmaz.
        await UstHesapVarOlmaliAsync(request.CompanyId, code, cancellationToken);

        var level = 1;

        if (request.ParentAccountId.HasValue)
        {
            // K4(b) — hareketi olan hesap yapraktır, altına açılmaz.
            await UstHesabinHareketiOlmamaliAsync(
                request.ParentAccountId.Value, cancellationToken);

            var parent = await dbContext.AccountingAccounts
                .FirstOrDefaultAsync(
                    x =>
                        x.Id == request.ParentAccountId.Value &&
                        x.CompanyId == request.CompanyId,
                    cancellationToken);

            if (parent is null)
                throw new ArgumentException(
                    "Üst hesap bulunamadı veya farklı şirkete ait.");

            level = parent.Level + 1;
        }

        var account = new AccountingAccount
        {
            CompanyId = request.CompanyId,
            ParentAccountId = request.ParentAccountId,
            Code = code,
            Name = request.Name.Trim(),
            Description = NormalizeNullable(request.Description),
            Nature = (AccountingAccountNature)request.Nature,
            Level = level,
            IsPostingAllowed = request.IsPostingAllowed,
            RequiresProject = request.RequiresProject,
            RequiresCostCenter = request.RequiresCostCenter,
            CurrencyCode = NormalizeCurrency(request.CurrencyCode),
            IsActive = true
        };

        dbContext.AccountingAccounts.Add(account);

        DenetimYaz("hesap-plani.ekle", account.Id, new
        {
            kod = account.Code,
            ad = account.Name,
            ustHesapId = account.ParentAccountId
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        /*
         * SÜRÜM OLUŞTURMA YANITINDA DA DÖNÜYOR.
         *
         * Yanıt doğrudan düzenleme formuna besleniyor; sürüm
         * dönmezse kullanıcı yeni açtığı hesabı DÜZENLEYEMEZ —
         * ilk kaydetmede sürüm hatası alır.
         */
        return MapDetail(
            account,
            KayitSurumu.Oku(account));
    }

    public async Task<AccountingAccountDetailResponse> UpdateAsync(
        Guid id,
        UpdateAccountingAccountRequest request,
        CancellationToken cancellationToken)
    {
        /*
         * KOD DOĞRULAMASI YOK ÇÜNKÜ KOD GELMİYOR (K1).
         *
         * Eskiden `request.Code` alınıp `account.Code`a yazılıyordu ve
         * yanında bir tekillik kontrolü duruyordu. İkisi de kalktı:
         * kod değişmediği için çakışma da doğmaz.
         */
        ValidateName(request.Name, request.Nature);

        var account = await dbContext.AccountingAccounts
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (account is null)
            throw new KeyNotFoundException("Muhasebe hesabı bulunamadı.");

        /*
         * EŞZAMANLILIK — İSTEMCİNİN SÜRÜMÜYLE (K8).
         *
         * Karşılaştırma ORTAK KAYNAKTA (`KayitSurumu`): istemcinin
         * gönderdiği damga ile veritabanındaki güncel damga.
         *
         * TUZAK: kaydın kendi damgasını kendisiyle karşılaştırmak HER
         * ZAMAN eşit çıkar — koruma görüntüsü, sıfır koruma. Bu yüzden
         * karşılaştırılan değer `request.Surum`, `account`ınki değil.
         *
         * SIRA ÖNEMLİ: doğrulama yazmalardan ÖNCE. Sonra yapılsaydı
         * reddedilen bir istek yine de nesneyi değiştirmiş olurdu ve
         * aynı DbContext'te sonraki bir işlem onu kaydedebilirdi.
         */
        KayitSurumu.Dogrula(account, request.Surum);

        var level = 1;

        if (request.ParentAccountId.HasValue)
        {
            if (request.ParentAccountId.Value == id)
                throw new ArgumentException(
                    "Bir hesap kendisinin üst hesabı olamaz.");

            /*
             * K4(b) GÜNCELLEMEDE DE GEÇERLİ.
             *
             * Üst hesap bu uçtan DEĞİŞTİRİLEBİLİYOR; bir hesabı fiş
             * kesilmiş bir hesabın altına TAŞIMAK, oraya alt hesap
             * EKLEMEKLE aynı şeydir. Yalnız oluşturmaya konsaydı kural
             * bir satır ötede delinirdi.
             */
            await UstHesabinHareketiOlmamaliAsync(
                request.ParentAccountId.Value, cancellationToken);

            var parent = await dbContext.AccountingAccounts
                .FirstOrDefaultAsync(
                    x =>
                        x.Id == request.ParentAccountId.Value &&
                        x.CompanyId == account.CompanyId,
                    cancellationToken);

            if (parent is null)
                throw new ArgumentException(
                    "Üst hesap bulunamadı veya farklı şirkete ait.");

            var parentCreatesCycle = await HasAncestorAsync(
                parent,
                id,
                cancellationToken);

            if (parentCreatesCycle)
                throw new ArgumentException(
                    "Seçilen üst hesap döngü oluşturuyor.");

            level = parent.Level + 1;
        }

        var eskiAd = account.Name;
        var eskiUstHesapId = account.ParentAccountId;

        account.ParentAccountId = request.ParentAccountId;

        // KOD YAZILMIYOR (K1). Bu satır 2026-08-30'a kadar buradaydı.
        account.Name = request.Name.Trim();

        /*
         * AD DEĞİŞMEDİYSE DENETİM YAZILMAZ.
         *
         * Her kaydetmede kayıt yazılsaydı, "adı kim değiştirdi"
         * sorusu değişmemiş kayıtların gürültüsünde kaybolurdu.
         */
        if (!string.Equals(eskiAd, account.Name, StringComparison.Ordinal))
        {
            DenetimYaz("hesap-plani.ad-degistir", account.Id, new
            {
                kod = account.Code,
                eskiAd,
                yeniAd = account.Name
            });
        }

        if (eskiUstHesapId != account.ParentAccountId)
        {
            DenetimYaz("hesap-plani.ust-hesap-degistir", account.Id, new
            {
                kod = account.Code,
                eskiUstHesapId,
                yeniUstHesapId = account.ParentAccountId
            });
        }
        account.Description = NormalizeNullable(request.Description);
        account.Nature = (AccountingAccountNature)request.Nature;
        account.Level = level;
        account.IsPostingAllowed = request.IsPostingAllowed;
        account.RequiresProject = request.RequiresProject;
        account.RequiresCostCenter = request.RequiresCostCenter;
        account.CurrencyCode = NormalizeCurrency(request.CurrencyCode);

        // AKTİFLİK YAZILMIYOR (K3). Tek kapı: Deactivate / Activate.
        account.UpdatedAtUtc = DateTime.UtcNow;


        await dbContext.SaveChangesAsync(cancellationToken);

        /*
         * YENİ SÜRÜM GERİ VERİLİYOR.
         *
         * Kaydeden kullanıcı formda kalıyor; sürüm dönmezse ikinci
         * kaydetmesi bayat damgayla gider ve kendi yazdığı kayıtta
         * "başkası değiştirdi" hatası alır.
         */
        return MapDetail(
            account,
            KayitSurumu.Oku(account));
    }

    /// <summary>
    /// K3 — PASİFE ALMA GERİ ALINABİLİR.
    ///
    /// Silme yok; pasife alınan hesap geri açılabiliyor. Bu uç
    /// olmasaydı "geri alınabilir" sözü kâğıtta kalırdı — ve
    /// kullanıcı geri açmak için güncelleme formundaki `IsActive`
    /// alanına muhtaç olurdu, ki o alan K3 gereği kaldırıldı.
    /// </summary>
    public async Task ActivateAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var account = await dbContext.AccountingAccounts
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (account is null)
            throw new KeyNotFoundException("Muhasebe hesabı bulunamadı.");

        /*
         * ÜST HESAP PASİFSE ALT HESAP GERİ AÇILAMAZ: aksi hâlde
         * pasif bir ağacın altında aktif bir yaprak kalır ve hesap
         * seçicide "olmayan" bir dalın ucu görünür.
         */
        if (account.ParentAccountId.HasValue)
        {
            var ustAktif = await dbContext.AccountingAccounts
                .AnyAsync(
                    x => x.Id == account.ParentAccountId.Value && x.IsActive,
                    cancellationToken);

            if (!ustAktif)
                throw new InvalidOperationException(
                    "Üst hesap pasif; önce üst hesabı geri açın.");
        }

        account.IsActive = true;
        account.UpdatedAtUtc = DateTime.UtcNow;

        DenetimYaz("hesap-plani.geri-ac", account.Id, new
        {
            kod = account.Code,
            ad = account.Name,
            eskiDurum = false,
            yeniDurum = true
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeactivateAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var account = await dbContext.AccountingAccounts
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (account is null)
            throw new KeyNotFoundException("Muhasebe hesabı bulunamadı.");

        var hasActiveChildren = await dbContext.AccountingAccounts
            .AnyAsync(
                x => x.ParentAccountId == id && x.IsActive,
                cancellationToken);

        if (hasActiveChildren)
            throw new InvalidOperationException(
                "Aktif alt hesapları bulunan hesap pasife alınamaz.");

        account.IsActive = false;
        account.UpdatedAtUtc = DateTime.UtcNow;

        DenetimYaz("hesap-plani.pasife-al", account.Id, new
        {
            kod = account.Code,
            ad = account.Name,
            eskiDurum = true,
            yeniDurum = false
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> HasAncestorAsync(
        AccountingAccount parent,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var current = parent;

        while (true)
        {
            if (current.Id == accountId)
                return true;

            if (!current.ParentAccountId.HasValue)
                return false;

            var next = await dbContext.AccountingAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == current.ParentAccountId.Value,
                    cancellationToken);

            if (next is null)
                return false;

            current = next;
        }
    }

    private static void ValidateRequest(string code, string name, int nature)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Hesap kodu zorunludur.");

        ValidateName(name, nature);
    }

    /// <summary>
    /// KOD DIŞI DOĞRULAMA — güncellemede kod gelmiyor (K1), o yüzden
    /// ayrıldı. İki ayrı doğrulama yazılsaydı biri güncellenip diğeri
    /// kalırdı.
    /// </summary>
    private static void ValidateName(string name, int nature)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Hesap adı zorunludur.");

        if (!Enum.IsDefined(typeof(AccountingAccountNature), nature))
            throw new ArgumentException("Geçersiz hesap karakteri.");
    }

    /// <summary>
    /// K5 — EKSİK ÜST HESAP OTOMATİK AÇILMAZ.
    ///
    /// `102.01.03` açılırken `102.01` yoksa hata döner. Otomatik
    /// açılsaydı, üretilen ara hesabın adı ve karakteri UYDURMA
    /// olurdu; hesap planı kullanıcının bilmediği satırlarla dolardı.
    ///
    /// Aktarımdaki kararla aynı (`chart.import`: üst hesap
    /// OLUŞTURULMAZ) — iki yol aynı kuralı uygulamalı, yoksa dosyadan
    /// gelen ile elle açılan farklı davranırdı.
    ///
    /// NOKTASIZ KOD ÜST HESAP İSTEMEZ: `102` kökün kendisidir.
    /// </summary>
    private async Task UstHesapVarOlmaliAsync(
        Guid companyId,
        string code,
        CancellationToken cancellationToken)
    {
        var sonNokta = code.LastIndexOf('.');

        if (sonNokta <= 0) return;

        var ustKod = code[..sonNokta];

        var ustVar = await dbContext.AccountingAccounts
            .AnyAsync(
                x => x.CompanyId == companyId && x.Code == ustKod,
                cancellationToken);

        if (!ustVar)
            throw new ArgumentException(
                $"Üst hesap \"{ustKod}\" bulunamadı. Otomatik açılmaz — "
                + "önce üst hesabı oluşturun.");
    }

    /// <summary>
    /// K4(b) — HAREKETİ OLAN HESABA ALT HESAP EKLENEMEZ.
    ///
    /// Fiş kesilmiş bir hesap ARTIK YAPRAKTIR: altına hesap açmak,
    /// mevcut bakiyeyi hangi alt hesaba ait olduğu belirsiz bir
    /// yerde bırakır. Toplam doğru görünür ama kırılımı yalan söyler.
    ///
    /// K4(a) İLE KARIŞTIRILMAMALI: o "aktif alt hesabı olan hesap
    /// pasife alınamaz" der ve K3'ün yardımcısıdır. Bu, ekleme
    /// yönünde ve ayrı bir kural — ikisi ayrı ayrı sınanıyor.
    /// </summary>
    private async Task UstHesabinHareketiOlmamaliAsync(
        Guid parentId,
        CancellationToken cancellationToken)
    {
        var hareketVar = await dbContext.AccountingVoucherLines
            .AnyAsync(x => x.AccountingAccountId == parentId, cancellationToken);

        if (hareketVar)
            throw new InvalidOperationException(
                "Bu hesaba fiş kesilmiş; altına alt hesap açılamaz. "
                + "Hareketi olan hesap yaprak hesaptır.");
    }

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeCurrency(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim().ToUpperInvariant();

        if (normalized.Length != 3)
            throw new ArgumentException(
                "Para birimi kodu 3 karakter olmalıdır.");

        return normalized;
    }

    /// <summary>
    /// SÜRÜM AÇIK GİRDİ — izleyiciden OKUNMAZ.
    ///
    /// İlk sürümde `dbContext.Entry(account)` üzerinden okunuyordu ve
    /// izlenmeyen (`AsNoTracking`) kayıtlarda 0 dönüyordu. Okuma ucu
    /// tam da `AsNoTracking` kullanıyor: sonuç, düzenleme formunu
    /// besleyen yanıtın HER ZAMAN sürüm 0 taşıması ve hiç kimsenin
    /// hiçbir hesabı düzenleyememesiydi.
    ///
    /// "İzlenmiyorsa 0 dön" bir güvenlik değil, özelliğin sessizce
    /// kapanmasıydı. Artık sürüm parametre: veremeyen çağıran
    /// DERLENMİYOR, çalışma anında sessizce 0 taşımıyor.
    /// </summary>
    private static AccountingAccountDetailResponse MapDetail(
        AccountingAccount account,
        DateTime surum) =>
        new(
            account.Id,
            account.CompanyId,
            account.ParentAccountId,
            account.Code,
            account.Name,
            account.Description,
            (int)account.Nature,
            account.Level,
            account.IsPostingAllowed,
            account.RequiresProject,
            account.RequiresCostCenter,
            account.CurrencyCode,
            account.IsActive,
            account.CreatedAtUtc,
            account.UpdatedAtUtc,
            surum);
}
