using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Contracts.Core;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;
using EnderunAI.Api.Search;

namespace EnderunAI.Api.Services.Accounting;

public sealed class AccountingAccountService(
    AppDbContext dbContext)
    : IAccountingAccountService
{
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
        var account = await dbContext.AccountingAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (account is null)
            throw new KeyNotFoundException("Muhasebe hesabı bulunamadı.");

        return MapDetail(account);
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

        var level = 1;

        if (request.ParentAccountId.HasValue)
        {
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
        await dbContext.SaveChangesAsync(cancellationToken);

        return MapDetail(account);
    }

    public async Task<AccountingAccountDetailResponse> UpdateAsync(
        Guid id,
        UpdateAccountingAccountRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request.Code, request.Name, request.Nature);

        var account = await dbContext.AccountingAccounts
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (account is null)
            throw new KeyNotFoundException("Muhasebe hesabı bulunamadı.");

        var code = request.Code.Trim();
        var duplicateExists = await dbContext.AccountingAccounts
            .AnyAsync(
                x =>
                    x.Id != id &&
                    x.CompanyId == account.CompanyId &&
                    x.Code == code,
                cancellationToken);

        if (duplicateExists)
            throw new InvalidOperationException(
                "Aynı şirkette bu hesap kodu zaten kullanılıyor.");

        var level = 1;

        if (request.ParentAccountId.HasValue)
        {
            if (request.ParentAccountId.Value == id)
                throw new ArgumentException(
                    "Bir hesap kendisinin üst hesabı olamaz.");

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

        account.ParentAccountId = request.ParentAccountId;
        account.Code = code;
        account.Name = request.Name.Trim();
        account.Description = NormalizeNullable(request.Description);
        account.Nature = (AccountingAccountNature)request.Nature;
        account.Level = level;
        account.IsPostingAllowed = request.IsPostingAllowed;
        account.RequiresProject = request.RequiresProject;
        account.RequiresCostCenter = request.RequiresCostCenter;
        account.CurrencyCode = NormalizeCurrency(request.CurrencyCode);
        account.IsActive = request.IsActive;
        account.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return MapDetail(account);
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

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Hesap adı zorunludur.");

        if (!Enum.IsDefined(typeof(AccountingAccountNature), nature))
            throw new ArgumentException("Geçersiz hesap karakteri.");
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

    private static AccountingAccountDetailResponse MapDetail(
        AccountingAccount account) =>
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
            account.UpdatedAtUtc);
}
