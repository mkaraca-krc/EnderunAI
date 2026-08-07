using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Data;
using EnderunAI.Api.Formatting;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Market;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Accounting;

/// <summary>
/// Değerleme önizlemesinin tek satırı.
/// </summary>
/// <param name="CurrentAccountId">Cari kart.</param>
/// <param name="CurrentAccountCode">Cari kodu.</param>
/// <param name="CurrentAccountTitle">Cari unvanı.</param>
/// <param name="CurrencyCode">Para birimi.</param>
/// <param name="Balance">Döviz bakiyesi (işaretli).</param>
/// <param name="BookValueLocal">Defterdeki TL karşılığı.</param>
/// <param name="RateAvailable">Değerleme kuru bulunabildi mi.</param>
/// <param name="ValuationRate">Değerleme kuru.</param>
/// <param name="RateSource">Kurun kaynağı.</param>
/// <param name="ValuedLocal">Değerleme kuruyla TL karşılığı.</param>
/// <param name="TotalDifference">Toplam fark.</param>
/// <param name="PreviouslyPosted">Önceki turlarda yazılmış düzeltme.</param>
/// <param name="PostableDifference">Bu turda yazılacak kısım.</param>
/// <param name="Message">Yazılamıyorsa nedeni.</param>
public sealed record CurrencyValuationPreviewLine(
    Guid CurrentAccountId,
    string CurrentAccountCode,
    string CurrentAccountTitle,
    string CurrencyCode,
    decimal Balance,
    decimal BookValueLocal,
    bool RateAvailable,
    decimal? ValuationRate,
    string? RateSource,
    decimal? ValuedLocal,
    decimal? TotalDifference,
    decimal PreviouslyPosted,
    decimal PostableDifference,
    string? Message);

/// <summary>
/// Değerleme önizlemesi.
/// </summary>
/// <param name="CompanyId">Şirket.</param>
/// <param name="ValuationDate">Değerleme tarihi.</param>
/// <param name="Lines">Satırlar.</param>
/// <param name="TotalGain">Kambiyo kârı toplamı (646).</param>
/// <param name="TotalLoss">Kambiyo zararı toplamı (656).</param>
/// <param name="NetDifference">Net fark.</param>
/// <param name="HasMissingRate">Kuru bulunamayan döviz var mı.</param>
/// <param name="AlreadyPostedRunId">Aynı tarihte iptal edilmemiş tur
/// varsa onun kimliği; varsa yeni tur kesilemez.</param>
public sealed record CurrencyValuationPreview(
    Guid CompanyId,
    DateTime ValuationDate,
    IReadOnlyList<CurrencyValuationPreviewLine> Lines,
    decimal TotalGain,
    decimal TotalLoss,
    decimal NetDifference,
    bool HasMissingRate,
    Guid? AlreadyPostedRunId);

/// <summary>
/// Dönem sonu kur değerlemesi (VUK): dövizli cari bakiyelerinin defter
/// değeri ile değerleme günündeki kur karşılığı arasındaki farkı
/// 646/656'ya yazar.
///
/// TASARIM — neden kümülatif:
/// Değerleme satırları TL olarak kesilir; dövizin kendi bakiyesini
/// değiştirmezler (hâlâ aynı doları borçluyuz). Bu yüzden bir sonraki
/// değerleme, defter değerini yine ORİJİNAL hareketlerden hesaplar ve
/// aynı farkı yeniden bulur. Çift kayıt olmasın diye her turda
/// yalnızca "toplam fark − daha önce yazılmış düzeltmeler" kadarı
/// defterlenir. İptal edilen turlar bu toplama girmez.
///
/// Kuru bulunamayan döviz için tutar UYDURULMAZ: o satır fişe girmez
/// ve önizlemede nedeniyle birlikte gösterilir.
/// </summary>
public sealed class CurrencyValuationService(
    AppDbContext db,
    CurrentAccountCurrencyService currencyService,
    IInvoiceExchangeRateResolver rateResolver,
    IAccountingVoucherService voucherService)
{
    private const string LocalCurrency = "TRY";

    /// <summary>
    /// Değerleme önizlemesi — hiçbir kayıt yazmaz.
    /// </summary>
    public async Task<CurrencyValuationPreview> PreviewAsync(
        Guid companyId,
        DateTime valuationDate,
        CancellationToken cancellationToken)
    {
        var date = DateTime.SpecifyKind(valuationDate.Date, DateTimeKind.Utc);

        var balances = await currencyService.GetBalancesAsync(
            companyId, currentAccountId: null, cancellationToken);

        var accountIds = balances.Select(x => x.CurrentAccountId).ToList();

        var accounts = await db.CurrentAccounts
            .AsNoTracking()
            .Where(x => accountIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Code, x.Title })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        // Daha önce yazılmış düzeltmeler (iptal edilenler hariç).
        var posted = await db.CurrencyValuationRunLines
            .AsNoTracking()
            .Where(x =>
                x.CurrencyValuationRun.CompanyId == companyId &&
                x.CurrencyValuationRun.ReversedAtUtc == null &&
                x.CurrencyValuationRun.ValuationDate <= date)
            .GroupBy(x => new { x.CurrentAccountId, x.CurrencyCode })
            .Select(g => new
            {
                g.Key.CurrentAccountId,
                g.Key.CurrencyCode,
                Total = g.Sum(x => x.PostedDifference)
            })
            .ToListAsync(cancellationToken);

        var postedMap = posted.ToDictionary(
            x => (x.CurrentAccountId, x.CurrencyCode.ToUpperInvariant()),
            x => x.Total);

        // Kur çözümü döviz başına bir kez yapılır: aynı dövizden onlarca
        // cari olabilir, her biri için arşivi tekrar sorgulamak gereksiz.
        var rateCache = new Dictionary<string, DocumentRateResolution>(
            StringComparer.OrdinalIgnoreCase);

        var lines = new List<CurrencyValuationPreviewLine>();

        foreach (var account in balances)
        {
            foreach (var currency in account.CurrencyBalances)
            {
                if (currency.CurrencyCode == LocalCurrency || currency.Balance == 0m)
                    continue;

                if (!rateCache.TryGetValue(currency.CurrencyCode, out var resolution))
                {
                    resolution = await rateResolver.ResolveAsync(
                        currency.CurrencyCode, date, explicitRate: null, cancellationToken);
                    rateCache[currency.CurrencyCode] = resolution;
                }

                var info = accounts.GetValueOrDefault(account.CurrentAccountId);

                postedMap.TryGetValue(
                    (account.CurrentAccountId, currency.CurrencyCode),
                    out var previouslyPosted);

                if (!resolution.Success)
                {
                    lines.Add(new CurrencyValuationPreviewLine(
                        account.CurrentAccountId,
                        info?.Code ?? "—",
                        info?.Title ?? "—",
                        currency.CurrencyCode,
                        currency.Balance,
                        currency.BalanceLocal,
                        RateAvailable: false,
                        ValuationRate: null,
                        RateSource: null,
                        ValuedLocal: null,
                        TotalDifference: null,
                        PreviouslyPosted: previouslyPosted,
                        PostableDifference: 0m,
                        Message: resolution.Error));

                    continue;
                }

                var difference = ExchangeDifferenceCalculator.Calculate(
                    currency.Balance, currency.BalanceLocal, resolution.Rate);

                var valued = decimal.Round(currency.Balance * resolution.Rate, 2);
                var total = decimal.Round(valued - currency.BalanceLocal, 2);
                var postable = decimal.Round(total - previouslyPosted, 2);

                lines.Add(new CurrencyValuationPreviewLine(
                    account.CurrentAccountId,
                    info?.Code ?? "—",
                    info?.Title ?? "—",
                    currency.CurrencyCode,
                    currency.Balance,
                    currency.BalanceLocal,
                    RateAvailable: true,
                    ValuationRate: resolution.Rate,
                    RateSource: resolution.Source,
                    ValuedLocal: valued,
                    TotalDifference: total,
                    PreviouslyPosted: previouslyPosted,
                    PostableDifference: postable,
                    Message: difference is null && postable == 0m
                        ? "Fark yok"
                        : null));
            }
        }

        var existingRun = await db.CurrencyValuationRuns
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.ValuationDate == date &&
                x.ReversedAtUtc == null)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return new CurrencyValuationPreview(
            CompanyId: companyId,
            ValuationDate: date,
            Lines: lines,
            TotalGain: decimal.Round(
                lines.Where(x => x.PostableDifference > 0m)
                     .Sum(x => x.PostableDifference), 2),
            TotalLoss: decimal.Round(
                lines.Where(x => x.PostableDifference < 0m)
                     .Sum(x => -x.PostableDifference), 2),
            NetDifference: decimal.Round(
                lines.Sum(x => x.PostableDifference), 2),
            HasMissingRate: lines.Any(x => !x.RateAvailable),
            AlreadyPostedRunId: existingRun);
    }

    /// <summary>
    /// Değerleme fişini keser ve turu kaydeder.
    /// </summary>
    /// <exception cref="InvalidOperationException">Aynı tarihte iptal
    /// edilmemiş tur varsa ya da yazılacak fark yoksa.</exception>
    public async Task<CurrencyValuationRun> PostAsync(
        Guid companyId,
        DateTime valuationDate,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var preview = await PreviewAsync(companyId, valuationDate, cancellationToken);

        if (preview.AlreadyPostedRunId is not null)
        {
            throw new InvalidOperationException(
                $"{preview.ValuationDate:dd.MM.yyyy} tarihi için zaten bir " +
                "değerleme fişi var. Yeniden kesmek için önce mevcut turu " +
                "iptal edin.");
        }

        var postable = preview.Lines
            .Where(x => x.RateAvailable && x.PostableDifference != 0m)
            .ToList();

        if (postable.Count == 0)
        {
            throw new InvalidOperationException(
                "Deftere yazılacak kur farkı yok. Dövizli bakiye " +
                "bulunmuyor ya da fark daha önce yazılmış olabilir.");
        }

        var gainAccountId = await FindAccountIdAsync(
            companyId, cancellationToken, "646.01", "646");
        var lossAccountId = await FindAccountIdAsync(
            companyId, cancellationToken, "656.01", "656");

        var needsGain = postable.Any(x => x.PostableDifference > 0m);
        var needsLoss = postable.Any(x => x.PostableDifference < 0m);

        if (needsGain && gainAccountId is null)
        {
            throw new InvalidOperationException(
                "Kambiyo kârı hesabı bulunamadı (646.01 / 646). " +
                "Hesap planında ilgili hesabı tanımlayın.");
        }

        if (needsLoss && lossAccountId is null)
        {
            throw new InvalidOperationException(
                "Kambiyo zararı hesabı bulunamadı (656.01 / 656). " +
                "Hesap planında ilgili hesabı tanımlayın.");
        }

        var settings = await db.CompanyFinanceSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);

        var accounts = await db.CurrentAccounts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new
            {
                x.Id,
                x.ReceivableAccountingAccountId,
                x.PayableAccountingAccountId
            })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var voucherLines = new List<AccountingVoucherLineRequest>();

        foreach (var line in postable)
        {
            var account = accounts.GetValueOrDefault(line.CurrentAccountId);

            // Bakiye yönü hangi hesabın değerleneceğini belirler:
            // alacaksak 120, borçluysak 320.
            var isReceivable = line.Balance > 0m;

            var currentAccountAccountId = isReceivable
                ? account?.ReceivableAccountingAccountId ?? settings?.ReceivablesAccountId
                : account?.PayableAccountingAccountId ?? settings?.PayablesAccountId;

            if (currentAccountAccountId is null)
            {
                throw new InvalidOperationException(
                    $"{line.CurrentAccountTitle} carisi için " +
                    $"{(isReceivable ? "alacak (120)" : "borç (320)")} hesabı " +
                    "belirlenemedi. Cari kartında ya da Finans Ayarları'nda " +
                    "ilgili hesabı seçin.");
            }

            var magnitude = Math.Abs(line.PostableDifference);
            var isGain = line.PostableDifference > 0m;

            var description =
                $"Kur değerlemesi — {line.CurrencyCode} " +
                $"{TurkishFormat.Amount(line.Balance)} @ " +
                $"{TurkishFormat.Rate(line.ValuationRate ?? 0m)}";

            // Değerleme satırları TL'dir: dövizin kendi bakiyesi
            // değişmiyor, yalnızca TL karşılığı düzeltiliyor.
            voucherLines.Add(new AccountingVoucherLineRequest(
                AccountingAccountId: currentAccountAccountId.Value,
                Description: description,
                DebitAmount: isGain ? magnitude : 0m,
                CreditAmount: isGain ? 0m : magnitude,
                CurrencyCode: LocalCurrency,
                ExchangeRate: 1m,
                CurrentAccountId: line.CurrentAccountId,
                ProjectId: null,
                CostCenterCode: null,
                DocumentNumber: null,
                DocumentDate: null,
                DueDate: null));

            // 646/656 satırına CARİ BOYUTU KONULMAZ. Cari bakiyesi bu
            // sistemde "o boyutu taşıyan satırların borç − alacak"ı
            // olarak hesaplanıyor; kâr/zarar satırını da cariye
            // etiketlemek düzeltmeyi kendi içinde netler ve cari
            // bakiyesi hiç değişmemiş gibi görünür.
            voucherLines.Add(new AccountingVoucherLineRequest(
                AccountingAccountId: isGain
                    ? gainAccountId!.Value
                    : lossAccountId!.Value,
                Description: description,
                // Kâr alacağa, zarar borca.
                DebitAmount: isGain ? 0m : magnitude,
                CreditAmount: isGain ? magnitude : 0m,
                CurrencyCode: LocalCurrency,
                ExchangeRate: 1m,
                CurrentAccountId: null,
                ProjectId: null,
                CostCenterCode: null,
                DocumentNumber: null,
                DocumentDate: null,
                DueDate: null));
        }

        var run = new CurrencyValuationRun
        {
            CompanyId = companyId,
            ValuationDate = preview.ValuationDate,
            PostedDifference = decimal.Round(
                postable.Sum(x => x.PostableDifference), 2),
            CreatedByUserId = actorUserId
        };

        foreach (var line in postable)
        {
            run.Lines.Add(new CurrencyValuationRunLine
            {
                CurrentAccountId = line.CurrentAccountId,
                CurrencyCode = line.CurrencyCode,
                Balance = line.Balance,
                BookValueLocal = line.BookValueLocal,
                ValuationRate = line.ValuationRate ?? 0m,
                ValuedLocal = line.ValuedLocal ?? 0m,
                TotalDifference = line.TotalDifference ?? 0m,
                PostedDifference = line.PostableDifference
            });
        }

        var created = await voucherService.CreateAsync(
            new CreateAccountingVoucherRequest(
                CompanyId: companyId,
                VoucherType: (int)AccountingVoucherType.Journal,
                VoucherDate: preview.ValuationDate,
                CurrencyCode: LocalCurrency,
                ExchangeRate: 1m,
                Description:
                    $"Kur değerlemesi — {preview.ValuationDate:dd.MM.yyyy}",
                ReferenceNumber: null,
                SourceModule: "CurrencyValuation",
                SourceEntityId: run.Id,
                Lines: voucherLines),
            cancellationToken);

        await voucherService.PostAsync(created.Id, cancellationToken);

        run.AccountingVoucherId = created.Id;

        db.CurrencyValuationRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        return run;
    }

    /// <summary>
    /// Değerleme turunu ters kayıtla iptal eder. İptal edilen tur
    /// kümülatif toplama girmez; sonraki değerleme farkı yeniden
    /// baştan hesaplar.
    /// </summary>
    public async Task<CurrencyValuationRun> ReverseAsync(
        Guid runId,
        string reason,
        IAccountingIntegrationService integration,
        CancellationToken cancellationToken)
    {
        var run = await db.CurrencyValuationRuns
            .SingleOrDefaultAsync(x => x.Id == runId, cancellationToken)
            ?? throw new KeyNotFoundException("Değerleme turu bulunamadı.");

        if (run.ReversedAtUtc is not null)
            throw new InvalidOperationException("Bu tur zaten iptal edilmiş.");

        if (run.AccountingVoucherId is null)
            throw new InvalidOperationException("Turun muhasebe fişi yok.");

        var reversalId = await integration.CreateReversalVoucherAsync(
            run.AccountingVoucherId.Value,
            string.IsNullOrWhiteSpace(reason) ? "Kur değerlemesi iptali" : reason,
            DateTime.UtcNow.Date,
            cancellationToken);

        run.ReversalVoucherId = reversalId;
        run.ReversedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return run;
    }

    private async Task<Guid?> FindAccountIdAsync(
        Guid companyId,
        CancellationToken cancellationToken,
        params string[] codeCandidates)
    {
        foreach (var code in codeCandidates)
        {
            var id = await db.AccountingAccounts
                .Where(x =>
                    x.CompanyId == companyId &&
                    x.Code == code &&
                    x.IsActive &&
                    x.IsPostingAllowed)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (id is not null)
                return id;
        }

        return null;
    }
}
