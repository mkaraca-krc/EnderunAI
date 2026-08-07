using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;
using EnderunAI.Api.Formatting;

namespace EnderunAI.Api.Services.Accounting;

/// <summary>
/// Tedarikçi faturası fişleştirme sonucu. ExpenseLineId, proje maliyet
/// kaydını muhasebedeki maliyet satırına bağlamak için kullanılır.
/// </summary>
public sealed record SupplierInvoicePostingResult(Guid VoucherId, Guid ExpenseLineId);

public interface IAccountingIntegrationService
{
    /// <summary>
    /// Şirketin finans ayarlarını getirir; yoksa hesap planından kod
    /// eşleştirmesiyle (191/391/600/740/320/120/780) varsayılanları
    /// oluşturur.
    /// </summary>
    Task<CompanyFinanceSettings> GetOrCreateFinanceSettingsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Onaylanan tedarikçi faturası için dengeli ve doğrudan Posted bir
    /// mahsup fişi üretir: maliyet hesabı + 191 İndirilecek KDV (borç),
    /// 320 Satıcılar (alacak). Fiş Id'si ile birlikte maliyet satırının
    /// Id'sini de döndürür (proje maliyeti ↔ muhasebe köprüsü için).
    /// </summary>
    /// <param name="reverse">
    /// İADE faturasında true: aynı hesaplar ve masraf merkezleriyle
    /// borç/alacak yer değiştirir. Ayrı bir "iade fişi" metodu
    /// yazılsaydı iki kural zamanla ayrışır ve iade, alışın tam aynası
    /// olmaktan çıkardı.
    /// </param>
    Task<SupplierInvoicePostingResult> CreateSupplierInvoiceVoucherAsync(
        SupplierInvoice invoice,
        CancellationToken cancellationToken = default,
        bool reverse = false);

    /// <summary>
    /// Kesinleşmiş bir fişin birebir ters kaydı. Orijinal fiş SİLİNMEZ;
    /// iptal, defterde iki fişle görünür.
    /// </summary>
    Task<Guid> CreateReversalVoucherAsync(
        Guid voucherId,
        string reason,
        DateTime voucherDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kesinleştirilen hakediş için dengeli ve doğrudan Posted bir gelir
    /// fişi üretir: 120 Alıcılar + kesinti hesapları (borç),
    /// 600 Yurtiçi Satışlar + 391 Hesaplanan KDV (alacak).
    /// </summary>
    Task<Guid> CreateProgressPaymentVoucherAsync(
        ProgressPayment progressPayment,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hakediş dışı satış faturası için dengeli, doğrudan Posted gelir
    /// fişi: 120 Alıcılar (borç) / 600 Yurtiçi Satışlar + 391 Hesaplanan
    /// KDV (alacak). Tevkifat varsa beyan edilen KDV o kadar azalır ve
    /// alacak da düşer.
    ///
    /// Hakediş fişinden AYRI bir metot: ikisinin kuralları zamanla
    /// ayrışır (hakedişte kesinti, ihzarat, minha var), tek metotta
    /// birleştirmek ikisini de kırılgan yapardı.
    /// </summary>
    /// <param name="reverse">
    /// İADE faturasında true: 600 yerine 610 Satıştan İadeler
    /// borçlandırılır, KDV ve alacak tersine döner. Gelir hesabı
    /// borçlandırılsaydı brüt satış rakamı olduğundan düşük görünürdü.
    /// </param>
    Task<Guid> CreateSalesInvoiceVoucherAsync(
        SalesInvoice invoice,
        CancellationToken cancellationToken = default,
        bool reverse = false);

    /// <summary>
    /// Kasa/banka hareketi için dengeli, doğrudan Posted fiş üretir.
    /// Para girişinde kasa/banka hesabı borçlanır, karşı hesap alacaklanır;
    /// çıkışta tersi. Karşı hesap işlem tipine göre belirlenir
    /// (tahsilat→120, ödeme→320, çek tahsili→101, çek ödemesi→103).
    /// </summary>
    Task<Guid> CreateCashTransactionVoucherAsync(
        CashTransaction transaction,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Çekin bir durum geçişi için dengeli, doğrudan Posted fiş üretir.
    /// Muhasebe etkisi olmayan geçişlerde (ör. faktoringdeki çekin
    /// tahsil bildirimi) null döner.
    /// </summary>
    Task<Guid?> CreateChequeVoucherAsync(
        Cheque cheque,
        ChequeStatus? fromStatus,
        ChequeStatus toStatus,
        DateTime voucherDate,
        CashAccount? cashAccount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Çek kırdırma fişi: 102 Bankalar (net) + 780 Finansman Giderleri
    /// (komisyon + BSMV + masraf) borç / 101 Alınan Çekler (nominal)
    /// alacak.
    /// </summary>
    Task<Guid> CreateFactoringVoucherAsync(
        FactoringTransaction transaction,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Aylık bordro tahakkuk fişi: 770 Personel Giderleri (brüt + işveren
    /// payı primler) borç / 335 Personele Borçlar + 360 Ödenecek Vergi +
    /// 361 Ödenecek SGK + 195 İş Avansları alacak.
    /// </summary>
    Task<Guid> CreatePayrollAccrualVoucherAsync(
        Guid companyId,
        int year,
        int month,
        PayrollAccrualTotals totals,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bordro ödeme fişi: 335 Personele Borçlar (borç) / kasa veya banka
    /// (alacak). Kasa/banka hareketi de aynı fişe bağlanır.
    /// </summary>
    Task<PayrollPaymentPostingResult> CreatePayrollPaymentVoucherAsync(
        Guid companyId,
        int year,
        int month,
        Guid cashAccountId,
        decimal amount,
        DateTime paymentDate,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Bir dönemin bordro toplamları. Tahakkuk fişi bunlardan üretilir;
/// borç ve alacak tarafının eşitliği bu toplamların iç tutarlılığına
/// dayanır (brüt + işveren payı = net + vergi + SGK + avans).
/// </summary>
public sealed record PayrollAccrualTotals(
    decimal TotalEarnings,
    decimal NetPayable,
    decimal IncomeTax,
    decimal StampTax,
    decimal SgkEmployee,
    decimal UnemploymentEmployee,
    decimal SgkEmployer,
    decimal UnemploymentEmployer,
    decimal AdvanceAndOtherDeductions,
    int PersonnelCount,
    /// <summary>
    /// Gider satırının masraf merkezi kırılımı. Boşsa tek satır kesilir
    /// ve şirket kodu kullanılır (eski davranış).
    /// </summary>
    IReadOnlyList<PayrollCostCenterShare>? CostCenters = null);

/// <summary>
/// Bir masraf merkezine düşen bordro gideri (brüt + işveren payı).
///
/// Merkez personelinin gideri merkez ofise, şantiye personelininki
/// çalıştığı projeye yazılır. Tutarların toplamı fişin gider satırına
/// eşit olmak zorundadır; yuvarlama artığı en büyük paya eklenir.
/// </summary>
/// <param name="Code">Fiş satırına yazılacak masraf merkezi kodu.</param>
/// <param name="Label">Satır açıklamasında görünen ad.</param>
/// <param name="ExpenseAmount">Brüt kazanç + işveren SGK/işsizlik payı.</param>
/// <param name="PersonnelCount">Bu merkeze düşen personel sayısı.</param>
public sealed record PayrollCostCenterShare(
    string Code,
    string Label,
    decimal ExpenseAmount,
    int PersonnelCount);

public sealed record PayrollPaymentPostingResult(
    Guid VoucherId,
    Guid CashTransactionId);

public sealed class AccountingIntegrationService(
    AppDbContext db,
    IAccountingVoucherService voucherService,
    Market.IInvoiceExchangeRateResolver exchangeRateResolver)
    : IAccountingIntegrationService
{
    public async Task<CompanyFinanceSettings> GetOrCreateFinanceSettingsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var settings = await db.CompanyFinanceSettings
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);

        if (settings is not null)
            return settings;

        settings = new CompanyFinanceSettings
        {
            CompanyId = companyId,
            VatInAccountId = await FindAccountIdAsync(companyId, cancellationToken, "191.01.03", "191"),
            VatOutAccountId = await FindAccountIdAsync(companyId, cancellationToken, "391.09", "391"),
            SalesAccountId = await FindAccountIdAsync(companyId, cancellationToken, "600.03", "600"),
            SalesReturnAccountId = await FindAccountIdAsync(companyId, cancellationToken, "610.01", "610"),
            VatCarryForwardAccountId = await FindAccountIdAsync(companyId, cancellationToken, "190.01", "190"),
            VatPayableAccountId = await FindAccountIdAsync(companyId, cancellationToken, "360.99", "360"),
            ReverseChargeVatInputAccountId = await FindAccountIdAsync(companyId, cancellationToken, "191.05"),
            ReverseChargeVatPayableAccountId = await FindAccountIdAsync(companyId, cancellationToken, "360.002"),
            ExpenseAccountId = await FindAccountIdAsync(companyId, cancellationToken, "740"),
            // Stok alışı 153'e yazılır; boş bırakılsaydı yeni şirkette
            // malzeme alışı doğrudan 740 maliyete düşer ve depodaki mal
            // hiç bilançoya girmezdi.
            InventoryAccountId = await FindAccountIdAsync(companyId, cancellationToken, "153", "150"),
            PayablesAccountId = await FindAccountIdAsync(companyId, cancellationToken, "320"),
            ReceivablesAccountId = await FindAccountIdAsync(companyId, cancellationToken, "120"),
            FactoringExpenseAccountId = await FindAccountIdAsync(companyId, cancellationToken, "780.01.01", "780"),
            DeductionAccountId = await FindAccountIdAsync(companyId, cancellationToken, "126"),
            PayrollExpenseAccountId = await FindAccountIdAsync(companyId, cancellationToken, "770", "720"),
            PayrollPayableAccountId = await FindAccountIdAsync(companyId, cancellationToken, "335"),
            TaxPayableAccountId = await FindAccountIdAsync(companyId, cancellationToken, "360"),
            SocialSecurityPayableAccountId = await FindAccountIdAsync(companyId, cancellationToken, "361"),
            EmployeeAdvanceAccountId = await FindAccountIdAsync(companyId, cancellationToken, "196.01.01", "196", "195")
        };

        db.CompanyFinanceSettings.Add(settings);
        await db.SaveChangesAsync(cancellationToken);

        return settings;
    }

    /// <summary>
    /// Faturanın borç (maliyet/gider) satırları.
    ///
    /// ALIŞ: kalemler stok hesabına yazılır (ayarlardaki stok hesabı,
    /// yoksa maliyet hesabı). Masraf merkezi kalemin deposunun projesi,
    /// yoksa faturanınki.
    ///
    /// GİDER: her kalem KENDİ seçilen hesabına yazılır. Aynı hesap +
    /// aynı masraf merkezine düşen kalemler tek satırda birleşir — 40
    /// kalemlik bir kırtasiye faturası deftere 40 satır yazmasın.
    /// </summary>
    private async Task<List<AccountingVoucherLineRequest>> BuildSupplierInvoiceCostLinesAsync(
        SupplierInvoice invoice,
        CompanyFinanceSettings settings,
        CurrentAccount supplier,
        string fallbackCostCenter,
        CancellationToken cancellationToken)
    {
        var items = invoice.Items.Count > 0
            ? invoice.Items.ToList()
            : await db.SupplierInvoiceItems
                .AsNoTracking()
                .Where(x => x.SupplierInvoiceId == invoice.Id)
                .ToListAsync(cancellationToken);

        // Kalem masraf merkezi doldurulmamışsa deponun projesinden
        // türetilir: şantiye deposuna giren malzeme o şantiyenin
        // maliyetidir.
        var warehouseIds = items
            .Select(x => x.WarehouseId ?? invoice.WarehouseId)
            .OfType<Guid>()
            .Distinct()
            .ToList();

        var warehouseCostCenters = warehouseIds.Count == 0
            ? []
            : await db.Warehouses
                .AsNoTracking()
                .Where(x => warehouseIds.Contains(x.Id))
                .Select(x => new
                {
                    x.Id,
                    Code = x.Project != null ? x.Project.Code : (x.Branch.CostCenterCode ?? x.Branch.Code),
                    ProjectId = x.ProjectId
                })
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        string ResolveCostCenter(SupplierInvoiceItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.CostCenterCode))
                return item.CostCenterCode!;

            var warehouseId = item.WarehouseId ?? invoice.WarehouseId;

            if (warehouseId is Guid id &&
                warehouseCostCenters.TryGetValue(id, out var warehouse))
            {
                return warehouse.Code;
            }

            return fallbackCostCenter;
        }

        Guid? ResolveProjectId(SupplierInvoiceItem item)
        {
            var warehouseId = item.WarehouseId ?? invoice.WarehouseId;

            if (warehouseId is Guid id &&
                warehouseCostCenters.TryGetValue(id, out var warehouse) &&
                warehouse.ProjectId is Guid warehouseProject)
            {
                return warehouseProject;
            }

            return invoice.ProjectId;
        }

        Guid ResolveAccount(SupplierInvoiceItem item)
        {
            if (invoice.InvoiceType == SupplierInvoiceType.Expense)
            {
                return item.ExpenseAccountId
                    ?? settings.ExpenseAccountId
                    ?? throw new InvalidOperationException(
                        $"Kalem {item.LineNumber}: gider hesabı seçilmemiş.");
            }

            // Stok hesabı ayarlanmamışsa maliyet hesabına düşülür ki
            // ayar yapılmamış şirkette fatura onayı kilitlenmesin.
            return settings.InventoryAccountId
                ?? settings.ExpenseAccountId
                ?? throw new InvalidOperationException(
                    "Stok veya maliyet hesabı yapılandırılmamış. " +
                    "Şirket Ayarları → Finans Ayarları'ndan seçin.");
        }

        var grouped = items
            .Select(item => new
            {
                Item = item,
                AccountId = ResolveAccount(item),
                CostCenter = ResolveCostCenter(item),
                ProjectId = ResolveProjectId(item)
            })
            .GroupBy(x => new { x.AccountId, x.CostCenter, x.ProjectId })
            .Select(group => new AccountingVoucherLineRequest(
                AccountingAccountId: group.Key.AccountId,
                Description: invoice.InvoiceType == SupplierInvoiceType.Expense
                    ? $"Gider — {supplier.Title}"
                    : $"Stok girişi — {supplier.Title}",
                DebitAmount: decimal.Round(group.Sum(x => x.Item.LineSubtotal), 2),
                CreditAmount: 0m,
                CurrencyCode: invoice.CurrencyCode,
                ExchangeRate: invoice.ExchangeRate,
                CurrentAccountId: null,
                ProjectId: group.Key.ProjectId,
                CostCenterCode: group.Key.CostCenter,
                DocumentNumber: invoice.InvoiceNumber,
                DocumentDate: invoice.InvoiceDate,
                DueDate: null))
            .ToList();

        // Yuvarlama artığı: kalem toplamları fatura ara toplamından
        // sapmışsa fark en büyük satıra eklenir; aksi halde fiş
        // dengelenmez ve onay tamamen bloke olurdu.
        var lineSum = grouped.Sum(x => x.DebitAmount);
        var difference = decimal.Round(invoice.Subtotal - lineSum, 2);

        if (difference != 0m && grouped.Count > 0)
        {
            var largest = grouped.OrderByDescending(x => x.DebitAmount).First();
            var index = grouped.IndexOf(largest);

            grouped[index] = largest with
            {
                DebitAmount = largest.DebitAmount + difference
            };
        }

        return grouped;
    }

    public async Task<SupplierInvoicePostingResult> CreateSupplierInvoiceVoucherAsync(
        SupplierInvoice invoice,
        CancellationToken cancellationToken = default,
        bool reverse = false)
    {
        var settings = await GetOrCreateFinanceSettingsAsync(
            invoice.CompanyId, cancellationToken);

        if (invoice.VatTotal > 0 && settings.VatInAccountId is null)
            throw new InvalidOperationException(
                "İndirilecek KDV hesabı yapılandırılmamış. Şirket Ayarları → Finans Ayarları'ndan seçin.");

        var supplier = await db.CurrentAccounts
            .SingleAsync(x => x.Id == invoice.SupplierCurrentAccountId, cancellationToken);

        var project = invoice.ProjectId is Guid projectId
            ? await db.Projects.SingleAsync(x => x.Id == projectId, cancellationToken)
            : null;

        var payableAccountId = await ResolvePayableAccountAsync(
            supplier, settings, cancellationToken);

        // Faturanın varsayılan masraf merkezi: açıkça girilen kod, yoksa
        // proje kodu, o da yoksa şirket kodu. Projesiz merkez giderinde
        // kod boş kalmasın diye üç kademeli.
        var fallbackCostCenter = invoice.CostCenterCode
            ?? project?.Code
            ?? await db.Companies
                .Where(x => x.Id == invoice.CompanyId)
                .Select(x => x.Code)
                .SingleAsync(cancellationToken);

        var lines = await BuildSupplierInvoiceCostLinesAsync(
            invoice, settings, supplier, fallbackCostCenter, cancellationToken);

        // KDV TEVKİFATI (alış tarafı): tevkifatlı faturada KDV'nin bir
        // kısmını tedarikçiye değil, "sorumlu sıfatıyla" doğrudan vergi
        // dairesine biz öderiz.
        //
        // Bu ayrım yapılmazsa iki şey birden bozulur: tedarikçiye
        // borcumuz tevkifat kadar fazla görünür (oysa o tutarı ona
        // ödemeyeceğiz) ve vergi dairesine olan yükümlülük hiç doğmaz.
        var withholding = decimal.Round(invoice.WithholdingAmount, 2);

        if (withholding > 0m && withholding > decimal.Round(invoice.VatTotal, 2))
        {
            throw new InvalidOperationException(
                $"Tevkifat ({TurkishFormat.Amount(withholding)}) fatura KDV'sinden " +
                $"({TurkishFormat.Amount(invoice.VatTotal)}) büyük olamaz.");
        }

        // İndirilecek KDV toplamda değişmez; tevkifatlı kısım yalnızca
        // ayrı hesapta izlenir (191.05 sorumlu sıfatıyla beyan edilen).
        var deductibleVat = decimal.Round(invoice.VatTotal - withholding, 2);

        if (deductibleVat > 0m)
        {
            lines.Add(new AccountingVoucherLineRequest(
                AccountingAccountId: settings.VatInAccountId!.Value,
                Description: "İndirilecek KDV",
                DebitAmount: deductibleVat,
                CreditAmount: 0m,
                CurrencyCode: invoice.CurrencyCode,
                ExchangeRate: invoice.ExchangeRate,
                CurrentAccountId: null,
                ProjectId: invoice.ProjectId,
                CostCenterCode: fallbackCostCenter,
                DocumentNumber: invoice.InvoiceNumber,
                DocumentDate: invoice.InvoiceDate,
                DueDate: null));
        }

        if (withholding > 0m)
        {
            var reverseChargeInputId = settings.ReverseChargeVatInputAccountId
                ?? settings.VatInAccountId
                ?? throw new InvalidOperationException(
                    "Sorumlu sıfatıyla beyan edilen KDV hesabı (191.05) " +
                    "yapılandırılmamış. Şirket Ayarları → Finans Ayarları'ndan seçin.");

            var reverseChargePayableId = settings.ReverseChargeVatPayableAccountId
                ?? throw new InvalidOperationException(
                    "Sorumlu sıfatıyla ödenecek KDV hesabı (360.002) " +
                    "yapılandırılmamış. Şirket Ayarları → Finans Ayarları'ndan seçin.");

            lines.Add(new AccountingVoucherLineRequest(
                AccountingAccountId: reverseChargeInputId,
                Description: "Sorumlu sıfatıyla beyan edilen KDV (tevkifat)",
                DebitAmount: withholding,
                CreditAmount: 0m,
                CurrencyCode: invoice.CurrencyCode,
                ExchangeRate: invoice.ExchangeRate,
                CurrentAccountId: null,
                ProjectId: invoice.ProjectId,
                CostCenterCode: fallbackCostCenter,
                DocumentNumber: invoice.InvoiceNumber,
                DocumentDate: invoice.InvoiceDate,
                DueDate: null));

            lines.Add(new AccountingVoucherLineRequest(
                AccountingAccountId: reverseChargePayableId,
                Description: "Sorumlu sıfatıyla ödenecek KDV (tevkifat)",
                DebitAmount: 0m,
                CreditAmount: withholding,
                CurrencyCode: invoice.CurrencyCode,
                ExchangeRate: invoice.ExchangeRate,
                CurrentAccountId: null,
                ProjectId: invoice.ProjectId,
                CostCenterCode: fallbackCostCenter,
                DocumentNumber: invoice.InvoiceNumber,
                DocumentDate: invoice.InvoiceDate,
                DueDate: invoice.DueDate));
        }

        // Tedarikçiye kalan borç: fatura toplamı eksi tevkifat.
        var supplierPayable = decimal.Round(invoice.GrandTotal - withholding, 2);

        lines.Add(new AccountingVoucherLineRequest(
            AccountingAccountId: payableAccountId,
            Description: withholding > 0m
                ? $"Satıcı borcu (tevkifat sonrası) — {supplier.Title}"
                : $"Satıcı borcu — {supplier.Title}",
            DebitAmount: 0m,
            CreditAmount: supplierPayable,
            CurrencyCode: invoice.CurrencyCode,
            ExchangeRate: invoice.ExchangeRate,
            CurrentAccountId: supplier.Id,
            ProjectId: invoice.ProjectId,
            CostCenterCode: fallbackCostCenter,
            DocumentNumber: invoice.InvoiceNumber,
            DocumentDate: invoice.InvoiceDate,
            DueDate: invoice.DueDate));

        // Denge ön kontrolü: fatura toplamları tutarlı olmalı; asıl
        // borç=alacak doğrulaması PostAsync içinde bir kez daha yapılır.
        var debitTotal = decimal.Round(invoice.Subtotal + invoice.VatTotal, 2);
        if (debitTotal != decimal.Round(invoice.GrandTotal, 2))
        {
            throw new InvalidOperationException(
                $"Fatura toplamları tutarsız: ara toplam + KDV ({TurkishFormat.Amount(debitTotal)}) ≠ genel toplam ({TurkishFormat.Amount(invoice.GrandTotal)}).");
        }

        // İade faturasında aynı satırlar borç/alacak yer değiştirerek
        // yazılır: hesaplar, projeler ve masraf merkezleri birebir aynı
        // kalır, yalnızca yön döner.
        if (reverse)
        {
            lines = lines
                .Select(line => line with
                {
                    DebitAmount = line.CreditAmount,
                    CreditAmount = line.DebitAmount
                })
                .ToList();
        }

        var created = await voucherService.CreateAsync(
            new CreateAccountingVoucherRequest(
                CompanyId: invoice.CompanyId,
                VoucherType: (int)AccountingVoucherType.Journal,
                VoucherDate: invoice.InvoiceDate,
                CurrencyCode: invoice.CurrencyCode,
                ExchangeRate: invoice.ExchangeRate,
                Description: reverse
                    ? $"Alış iadesi {invoice.InternalNumber} — {supplier.Title}"
                    : $"Tedarikçi faturası {invoice.InternalNumber} — {supplier.Title}",
                ReferenceNumber: invoice.InvoiceNumber,
                SourceModule: "SupplierInvoice",
                SourceEntityId: invoice.Id,
                Lines: lines),
            cancellationToken);

        await voucherService.PostAsync(created.Id, cancellationToken);

        // Maliyet satırları fişin başında, KDV ve satıcı satırından önce
        // üretiliyor. Sabit bir hesap koduna göre aranmıyor: gider
        // faturasında her kalem kendi hesabına, alışta stok hesabına
        // yazılıyor; hesaba göre arayan eski sorgu artık hiçbir satır
        // bulamazdı.
        // İade fişinde maliyet satırları ALACAK tarafındadır; borca göre
        // arayan sorgu orada hiçbir satır bulamazdı.
        var expenseLineId = await db.AccountingVoucherLines
            .Where(x => x.AccountingVoucherId == created.Id &&
                        (reverse ? x.CreditAmount > 0m : x.DebitAmount > 0m) &&
                        (settings.VatInAccountId == null ||
                         x.AccountingAccountId != settings.VatInAccountId))
            .OrderBy(x => x.LineNumber)
            .Select(x => x.Id)
            .FirstAsync(cancellationToken);

        return new SupplierInvoicePostingResult(created.Id, expenseLineId);
    }

    public async Task<Guid> CreateSalesInvoiceVoucherAsync(
        SalesInvoice invoice,
        CancellationToken cancellationToken = default,
        bool reverse = false)
    {
        var settings = await GetOrCreateFinanceSettingsAsync(
            invoice.CompanyId, cancellationToken);

        if (settings.SalesAccountId is null)
            throw new InvalidOperationException(
                "Yurtiçi satışlar hesabı yapılandırılmamış. Şirket Ayarları → " +
                "Finans Ayarları'ndan seçin.");

        // İadede gelir hesabı yerine 610 Satıştan İadeler kullanılır;
        // 600 borçlandırılsaydı brüt satış rakamı olduğundan düşük
        // görünür ve dönem kıyasları bozulurdu.
        var revenueAccountId = reverse
            ? settings.SalesReturnAccountId
                ?? await FindAccountIdAsync(
                    invoice.CompanyId, cancellationToken, "610.01", "610")
                ?? throw new InvalidOperationException(
                    "Satıştan iadeler (610) hesabı bulunamadı. Hesap planında " +
                    "tanımlayın ya da Finans Ayarları'ndan seçin.")
            : settings.SalesAccountId.Value;

        var customer = await db.CurrentAccounts
            .SingleAsync(x => x.Id == invoice.CustomerCurrentAccountId, cancellationToken);

        var project = invoice.ProjectId is Guid projectId
            ? await db.Projects.SingleAsync(x => x.Id == projectId, cancellationToken)
            : null;

        var subtotal = decimal.Round(invoice.Subtotal, 2);

        if (subtotal <= 0m)
            throw new InvalidOperationException(
                "Fatura tutarı sıfır; muhasebe fişi oluşturulamaz.");

        // Tevkifatta KDV'nin bir kısmını alıcı beyan eder; biz yalnızca
        // kalanı beyan eder ve tahsil ederiz.
        var declaredVat = decimal.Round(invoice.VatTotal - invoice.WithholdingAmount, 2);
        var receivable = decimal.Round(invoice.NetReceivableAmount, 2);

        if (decimal.Round(subtotal + declaredVat, 2) != receivable)
        {
            throw new InvalidOperationException(
                $"Fatura tutarları tutarsız: matrah ({TurkishFormat.Amount(subtotal)}) + beyan edilen " +
                $"KDV ({TurkishFormat.Amount(declaredVat)}) ≠ tahsil edilecek ({TurkishFormat.Amount(receivable)}).");
        }

        var receivableAccountId = customer.ReceivableAccountingAccountId
            ?? settings.ReceivablesAccountId
            ?? throw new InvalidOperationException(
                $"'{customer.Title}' carisi için 120 Alıcılar hesabı bulunamadı. " +
                "Cari kartında hesap eşleyin veya Finans Ayarları'ndan varsayılanı seçin.");

        var reference = invoice.OfficialInvoiceNumber ?? invoice.InternalNumber;

        var lines = new List<AccountingVoucherLineRequest>
        {
            new(
                AccountingAccountId: receivableAccountId,
                Description: $"Satış faturası alacağı — {customer.Title}",
                DebitAmount: receivable,
                CreditAmount: 0m,
                CurrencyCode: invoice.CurrencyCode,
                ExchangeRate: invoice.ExchangeRate,
                CurrentAccountId: customer.Id,
                ProjectId: invoice.ProjectId,
                CostCenterCode: project?.Code,
                DocumentNumber: reference,
                DocumentDate: invoice.InvoiceDate,
                DueDate: invoice.DueDate),

            new(
                AccountingAccountId: revenueAccountId,
                Description: reverse
                    ? $"Satıştan iade — {reference}"
                    : $"Satış geliri — {reference}",
                DebitAmount: 0m,
                CreditAmount: subtotal,
                CurrencyCode: invoice.CurrencyCode,
                ExchangeRate: invoice.ExchangeRate,
                CurrentAccountId: customer.Id,
                ProjectId: invoice.ProjectId,
                CostCenterCode: project?.Code,
                DocumentNumber: reference,
                DocumentDate: invoice.InvoiceDate,
                DueDate: null)
        };

        if (declaredVat > 0m)
        {
            if (settings.VatOutAccountId is null)
                throw new InvalidOperationException(
                    "Hesaplanan KDV hesabı yapılandırılmamış. Şirket Ayarları → " +
                    "Finans Ayarları'ndan seçin.");

            lines.Add(new AccountingVoucherLineRequest(
                AccountingAccountId: settings.VatOutAccountId.Value,
                Description: invoice.WithholdingAmount > 0m
                    ? "Hesaplanan KDV (tevkifat sonrası)"
                    : "Hesaplanan KDV",
                DebitAmount: 0m,
                CreditAmount: declaredVat,
                CurrencyCode: invoice.CurrencyCode,
                ExchangeRate: invoice.ExchangeRate,
                CurrentAccountId: customer.Id,
                ProjectId: invoice.ProjectId,
                CostCenterCode: project?.Code,
                DocumentNumber: reference,
                DocumentDate: invoice.InvoiceDate,
                DueDate: null));
        }

        if (reverse)
        {
            lines = lines
                .Select(line => line with
                {
                    DebitAmount = line.CreditAmount,
                    CreditAmount = line.DebitAmount
                })
                .ToList();
        }

        var created = await voucherService.CreateAsync(
            new CreateAccountingVoucherRequest(
                CompanyId: invoice.CompanyId,
                VoucherType: (int)AccountingVoucherType.Journal,
                VoucherDate: invoice.InvoiceDate,
                CurrencyCode: invoice.CurrencyCode,
                ExchangeRate: invoice.ExchangeRate,
                Description: reverse
                    ? $"Satış iadesi {reference} — {customer.Title}"
                    : $"Satış faturası {reference} — {customer.Title}",
                ReferenceNumber: reference,
                SourceModule: "SalesInvoice",
                SourceEntityId: invoice.Id,
                Lines: lines),
            cancellationToken);

        await voucherService.PostAsync(created.Id, cancellationToken);

        return created.Id;
    }

    public async Task<Guid> CreateReversalVoucherAsync(
        Guid voucherId,
        string reason,
        DateTime voucherDate,
        CancellationToken cancellationToken = default)
    {
        var original = await db.AccountingVouchers
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == voucherId, cancellationToken)
            ?? throw new KeyNotFoundException("Ters kaydı alınacak fiş bulunamadı.");

        if (original.Status != AccountingVoucherStatus.Posted)
        {
            throw new InvalidOperationException(
                "Yalnızca kesinleşmiş fişin ters kaydı alınabilir.");
        }

        if (original.Lines.Count == 0)
            throw new InvalidOperationException("Fişin satırı yok; ters kayıt üretilemez.");

        var lines = original.Lines
            .OrderBy(x => x.LineNumber)
            .Select(line => new AccountingVoucherLineRequest(
                AccountingAccountId: line.AccountingAccountId,
                Description: $"İptal — {line.Description}",
                // Borç ve alacak yer değiştirir; hesap, proje ve masraf
                // merkezi aynı kalır ki iptal orijinali tam kapatsın.
                DebitAmount: line.CreditAmount,
                CreditAmount: line.DebitAmount,
                CurrencyCode: line.CurrencyCode,
                ExchangeRate: line.ExchangeRate,
                CurrentAccountId: line.CurrentAccountId,
                ProjectId: line.ProjectId,
                CostCenterCode: line.CostCenterCode,
                DocumentNumber: line.DocumentNumber,
                DocumentDate: line.DocumentDate,
                DueDate: line.DueDate))
            .ToList();

        var created = await voucherService.CreateAsync(
            new CreateAccountingVoucherRequest(
                CompanyId: original.CompanyId,
                VoucherType: (int)original.VoucherType,
                VoucherDate: DateTime.SpecifyKind(voucherDate.Date, DateTimeKind.Utc),
                CurrencyCode: original.CurrencyCode,
                ExchangeRate: original.ExchangeRate,
                Description: $"TERS KAYIT — {original.VoucherNumber}: {reason}",
                ReferenceNumber: original.ReferenceNumber,
                SourceModule: original.SourceModule,
                SourceEntityId: original.SourceEntityId,
                Lines: lines),
            cancellationToken);

        await voucherService.PostAsync(created.Id, cancellationToken);

        return created.Id;
    }

    public async Task<Guid> CreateProgressPaymentVoucherAsync(
        ProgressPayment progressPayment,
        CancellationToken cancellationToken = default)
    {
        var settings = await GetOrCreateFinanceSettingsAsync(
            progressPayment.CompanyId, cancellationToken);

        if (settings.SalesAccountId is null)
            throw new InvalidOperationException(
                "Yurtiçi satışlar hesabı yapılandırılmamış. Şirket Ayarları → Finans Ayarları'ndan seçin.");

        var project = await db.Projects
            .SingleAsync(x => x.Id == progressPayment.ProjectId, cancellationToken);

        if (project.EmployerCurrentAccountId is null)
            throw new InvalidOperationException(
                "Projede işveren cari kartı tanımlı değil; hakediş muhasebeleştirilemez. " +
                "Proje kartından işvereni seçin.");

        var employer = await db.CurrentAccounts
            .SingleAsync(x => x.Id == project.EmployerCurrentAccountId.Value, cancellationToken);

        var deductions = await db.ProgressPaymentDeductions
            .Where(x => x.ProgressPaymentId == progressPayment.Id && x.Amount != 0m)
            .OrderBy(x => x.LineNumber)
            .ToListAsync(cancellationToken);

        // Tevkifatlı KDV'de satıcının beyan ettiği kısım yalnızca
        // tevkifat dışında kalan tutardır; kesilen kısmı alıcı beyan eder.
        //
        // İhzarat ayrı bir gelir satırı üretmez: fatura edilen tutarın
        // içindedir ve KDV'ye tabidir. Kümülatif toplama girdiği için
        // CurrentAmount'a zaten yansımıştır.
        var taxableAmount = decimal.Round(
            progressPayment.CurrentAmount + progressPayment.PriceDifferenceAmount, 2);
        var declaredVat = decimal.Round(
            progressPayment.VatAmount - progressPayment.WithholdingAmount, 2);
        var totalDeduction = decimal.Round(
            deductions.Sum(x => x.Amount), 2);
        var incomeTaxWithholding = decimal.Round(
            progressPayment.IncomeTaxWithholdingAmount, 2);
        var receivable = decimal.Round(
            progressPayment.NetPayableAmount, 2);

        if (taxableAmount <= 0m)
            throw new InvalidOperationException(
                "Hakediş tutarı sıfır; muhasebe fişi oluşturulamaz.");

        // Kesintiler hakedişi aşarsa alacak negatife düşer. Negatif borç
        // satırı fişi bozar; kullanıcı sebebi anlamadan 500 alırdı.
        if (receivable < 0m)
        {
            throw new InvalidOperationException(
                $"Kesintiler ve stopaj toplamı ({TurkishFormat.Amount(totalDeduction + incomeTaxWithholding)}) " +
                $"hakediş tutarını ({TurkishFormat.Amount(taxableAmount + progressPayment.VatAmount)}) aşıyor; " +
                $"tahsil edilecek tutar negatif ({TurkishFormat.Amount(receivable)}). Bu hakediş " +
                "kesinleştirilemez — kesintileri gözden geçirin veya fazlasını " +
                "sonraki hakedişe bırakın.");
        }

        // Stopaj da alacaktan düşen bir kalemdir; denge kontrolüne
        // girmezse fiş tutarsız görünürdü.
        if (decimal.Round(receivable + totalDeduction + incomeTaxWithholding, 2) !=
            decimal.Round(taxableAmount + declaredVat, 2))
        {
            throw new InvalidOperationException(
                $"Hakediş tutarları tutarsız: net ödenecek ({TurkishFormat.Amount(receivable)}) + kesintiler ({TurkishFormat.Amount(totalDeduction)}) " +
                $"+ stopaj ({TurkishFormat.Amount(incomeTaxWithholding)}) ≠ hakediş ({TurkishFormat.Amount(taxableAmount)}) + beyan edilen KDV ({TurkishFormat.Amount(declaredVat)}).");
        }

        // Kesinti türü → hesap eşlemesi. Çözüm sırası: kesinti
        // satırındaki hesap → şirketin tür eşlemesi → genel kesinti
        // hesabı.
        var deductionAccountByType = await db.HakedisDeductionAccountMappings
            .AsNoTracking()
            .Where(x => x.CompanyId == progressPayment.CompanyId)
            .ToDictionaryAsync(
                x => x.DeductionType, x => x.AccountingAccountId, cancellationToken);

        var receivableAccountId = employer.ReceivableAccountingAccountId
            ?? settings.ReceivablesAccountId
            ?? throw new InvalidOperationException(
                $"'{employer.Title}' carisi için 120 Alıcılar hesabı bulunamadı. " +
                "Cari kartında hesap eşleyin veya Şirket Ayarları → Finans Ayarları'ndan varsayılan hesabı seçin.");

        var lines = new List<AccountingVoucherLineRequest>
        {
            new(
                AccountingAccountId: receivableAccountId,
                Description: $"Hakediş alacağı — {employer.Title}",
                DebitAmount: receivable,
                CreditAmount: 0m,
                CurrencyCode: progressPayment.CurrencyCode,
                ExchangeRate: 1m,
                CurrentAccountId: employer.Id,
                ProjectId: progressPayment.ProjectId,
                CostCenterCode: project.Code,
                DocumentNumber: progressPayment.ProgressPaymentNumber,
                DocumentDate: progressPayment.ProgressPaymentDate,
                DueDate: null)
        };

        foreach (var deduction in deductions)
        {
            Guid? mapped =
                deductionAccountByType.TryGetValue(deduction.DeductionType, out var value)
                    ? value
                    : null;

            var deductionAccountId = deduction.AccountingAccountId
                ?? mapped
                ?? settings.DeductionAccountId
                ?? throw new InvalidOperationException(
                    $"'{deduction.Description}' kesintisi için muhasebe hesabı belirlenmemiş. " +
                    "Kesinti satırında hesap seçin, Şirket Ayarları → Hakediş Kesinti Hesapları'ndan " +
                    "türe hesap eşleyin veya Finans Ayarları'ndan varsayılan kesinti hesabını tanımlayın.");

            lines.Add(new AccountingVoucherLineRequest(
                AccountingAccountId: deductionAccountId,
                Description: $"Hakediş kesintisi — {deduction.Description}",
                DebitAmount: decimal.Round(deduction.Amount, 2),
                CreditAmount: 0m,
                CurrencyCode: progressPayment.CurrencyCode,
                ExchangeRate: 1m,
                CurrentAccountId: employer.Id,
                ProjectId: progressPayment.ProjectId,
                CostCenterCode: project.Code,
                DocumentNumber: progressPayment.ProgressPaymentNumber,
                DocumentDate: progressPayment.ProgressPaymentDate,
                DueDate: null));
        }

        // Stopaj: işveren kaynağında kesip vergi dairesine yatırır, biz
        // peşin ödenmiş vergi olarak izleriz. Alacaktan düştüğü için
        // borç tarafında yer alır.
        if (incomeTaxWithholding > 0m)
        {
            var withholdingAccountId = settings.TaxPayableAccountId
                ?? settings.DeductionAccountId
                ?? throw new InvalidOperationException(
                    "Stopaj için muhasebe hesabı belirlenmemiş. Şirket Ayarları → " +
                    "Finans Ayarları'ndan ödenecek vergiler hesabını seçin.");

            lines.Add(new AccountingVoucherLineRequest(
                AccountingAccountId: withholdingAccountId,
                Description:
                    $"Hakediş stopajı (%{TurkishFormat.Rate(progressPayment.IncomeTaxWithholdingRate)})",
                DebitAmount: incomeTaxWithholding,
                CreditAmount: 0m,
                CurrencyCode: progressPayment.CurrencyCode,
                ExchangeRate: 1m,
                CurrentAccountId: employer.Id,
                ProjectId: progressPayment.ProjectId,
                CostCenterCode: project.Code,
                DocumentNumber: progressPayment.ProgressPaymentNumber,
                DocumentDate: progressPayment.ProgressPaymentDate,
                DueDate: null));
        }

        lines.Add(new AccountingVoucherLineRequest(
            AccountingAccountId: settings.SalesAccountId.Value,
            Description: $"Hakediş geliri — {project.Code}",
            DebitAmount: 0m,
            CreditAmount: taxableAmount,
            CurrencyCode: progressPayment.CurrencyCode,
            ExchangeRate: 1m,
            CurrentAccountId: employer.Id,
            ProjectId: progressPayment.ProjectId,
            CostCenterCode: project.Code,
            DocumentNumber: progressPayment.ProgressPaymentNumber,
            DocumentDate: progressPayment.ProgressPaymentDate,
            DueDate: null));

        if (declaredVat > 0m)
        {
            if (settings.VatOutAccountId is null)
                throw new InvalidOperationException(
                    "Hesaplanan KDV hesabı yapılandırılmamış. Şirket Ayarları → Finans Ayarları'ndan seçin.");

            lines.Add(new AccountingVoucherLineRequest(
                AccountingAccountId: settings.VatOutAccountId.Value,
                Description: progressPayment.WithholdingAmount > 0m
                    ? $"Hesaplanan KDV (tevkifat sonrası {progressPayment.WithholdingNumerator}/{progressPayment.WithholdingDenominator})"
                    : "Hesaplanan KDV",
                DebitAmount: 0m,
                CreditAmount: declaredVat,
                CurrencyCode: progressPayment.CurrencyCode,
                ExchangeRate: 1m,
                CurrentAccountId: employer.Id,
                ProjectId: progressPayment.ProjectId,
                CostCenterCode: project.Code,
                DocumentNumber: progressPayment.ProgressPaymentNumber,
                DocumentDate: progressPayment.ProgressPaymentDate,
                DueDate: null));
        }

        var created = await voucherService.CreateAsync(
            new CreateAccountingVoucherRequest(
                CompanyId: progressPayment.CompanyId,
                VoucherType: (int)AccountingVoucherType.Journal,
                VoucherDate: progressPayment.ProgressPaymentDate,
                CurrencyCode: progressPayment.CurrencyCode,
                ExchangeRate: 1m,
                Description:
                    $"Hakediş {progressPayment.ProgressPaymentNumber} " +
                    $"({progressPayment.PeriodNumber}. dönem) — {project.Code} {project.Name}",
                ReferenceNumber: progressPayment.ProgressPaymentNumber,
                SourceModule: "ProgressPayment",
                SourceEntityId: progressPayment.Id,
                Lines: lines),
            cancellationToken);

        await voucherService.PostAsync(created.Id, cancellationToken);

        return created.Id;
    }

    public async Task<Guid> CreateCashTransactionVoucherAsync(
        CashTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        var cashAccount = await db.CashAccounts
            .Include(x => x.AccountingAccount)
            .SingleAsync(x => x.Id == transaction.CashAccountId, cancellationToken);

        var settings = await GetOrCreateFinanceSettingsAsync(
            cashAccount.CompanyId, cancellationToken);

        if (transaction.Amount <= 0m)
            throw new InvalidOperationException("Hareket tutarı sıfırdan büyük olmalıdır.");

        CurrentAccount? counterparty = null;
        if (transaction.CurrentAccountId is not null)
        {
            counterparty = await db.CurrentAccounts
                .SingleAsync(x => x.Id == transaction.CurrentAccountId.Value, cancellationToken);
        }

        // Karşı hesap: paranın nereden geldiği / nereye gittiği.
        var (counterAccountId, counterDescription) = transaction.TransactionType switch
        {
            CashTransactionType.Collection =>
                (counterparty?.ReceivableAccountingAccountId ?? settings.ReceivablesAccountId,
                 $"Tahsilat — {counterparty?.Title ?? "cari"}"),

            CashTransactionType.Payment =>
                (counterparty?.PayableAccountingAccountId ?? settings.PayablesAccountId,
                 $"Ödeme — {counterparty?.Title ?? "cari"}"),

            CashTransactionType.ChequeCollection =>
                (await FindAccountIdAsync(cashAccount.CompanyId, cancellationToken, "101.01.01", "101"),
                 "Alınan çek tahsili"),

            CashTransactionType.ChequePayment =>
                (await FindAccountIdAsync(cashAccount.CompanyId, cancellationToken, "103.01", "103"),
                 "Verilen çek ödemesi"),

            _ => (null, transaction.Description)
        };

        if (counterAccountId is null)
        {
            throw new InvalidOperationException(
                "Bu hareket için karşı muhasebe hesabı belirlenemedi. " +
                "Şirket Ayarları → Finans Ayarları'ndan ilgili hesabı seçin.");
        }

        var isInflow = transaction.Direction == CashTransactionDirection.In;
        var amount = decimal.Round(transaction.Amount, 2);

        var lines = new List<AccountingVoucherLineRequest>
        {
            new(
                AccountingAccountId: cashAccount.AccountingAccountId,
                Description: $"{cashAccount.Name} — {(isInflow ? "giriş" : "çıkış")}",
                DebitAmount: isInflow ? amount : 0m,
                CreditAmount: isInflow ? 0m : amount,
                CurrencyCode: transaction.CurrencyCode,
                ExchangeRate: 1m,
                CurrentAccountId: transaction.CurrentAccountId,
                ProjectId: transaction.ProjectId,
                CostCenterCode: null,
                DocumentNumber: transaction.DocumentNumber,
                DocumentDate: transaction.TransactionDate,
                DueDate: null),
            new(
                AccountingAccountId: counterAccountId.Value,
                Description: counterDescription,
                DebitAmount: isInflow ? 0m : amount,
                CreditAmount: isInflow ? amount : 0m,
                CurrencyCode: transaction.CurrencyCode,
                ExchangeRate: 1m,
                CurrentAccountId: transaction.CurrentAccountId,
                ProjectId: transaction.ProjectId,
                CostCenterCode: null,
                DocumentNumber: transaction.DocumentNumber,
                DocumentDate: transaction.TransactionDate,
                DueDate: null)
        };

        var voucherType = isInflow
            ? AccountingVoucherType.Collection
            : AccountingVoucherType.Payment;

        var created = await voucherService.CreateAsync(
            new CreateAccountingVoucherRequest(
                CompanyId: cashAccount.CompanyId,
                VoucherType: (int)voucherType,
                VoucherDate: transaction.TransactionDate,
                CurrencyCode: transaction.CurrencyCode,
                ExchangeRate: 1m,
                Description: transaction.Description,
                ReferenceNumber: transaction.DocumentNumber,
                SourceModule: transaction.SourceModule ?? "CashTransaction",
                SourceEntityId: transaction.Id,
                Lines: lines),
            cancellationToken);

        await voucherService.PostAsync(created.Id, cancellationToken);

        return created.Id;
    }

    /// <summary>
    /// Tedarikçinin 320 alt hesabını çözer: cari kartındaki eşleme →
    /// 320 altında isim eşleşmesi (bulunursa kalıcı eşlenir) → şirket
    /// varsayılanı (320 grup hesabı, CurrentAccountId boyutuyla).
    /// </summary>
    private async Task<Guid> ResolvePayableAccountAsync(
        CurrentAccount supplier,
        CompanyFinanceSettings settings,
        CancellationToken cancellationToken)
    {
        if (supplier.PayableAccountingAccountId is not null)
            return supplier.PayableAccountingAccountId.Value;

        if (settings.PayablesAccountId is not null)
        {
            var normalizedTitle = supplier.Title.Trim().ToLowerInvariant();
            var matched = await db.AccountingAccounts
                .Where(x =>
                    x.CompanyId == supplier.CompanyId &&
                    x.ParentAccountId == settings.PayablesAccountId &&
                    x.IsActive &&
                    x.IsPostingAllowed &&
                    x.Name.ToLower() == normalizedTitle)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (matched is not null)
            {
                supplier.PayableAccountingAccountId = matched;
                await db.SaveChangesAsync(cancellationToken);
                return matched.Value;
            }

            return settings.PayablesAccountId.Value;
        }

        throw new InvalidOperationException(
            $"'{supplier.Title}' carisi için 320 Satıcılar hesabı bulunamadı. " +
            "Cari kartında hesap eşleyin veya Şirket Ayarları → Finans Ayarları'ndan varsayılan hesabı seçin.");
    }

    /// <summary>
    /// Kod adaylarını sırayla dener; hesap aktif ve kayıt yapılabilir
    /// (IsPostingAllowed) olmalıdır. Bulunamazsa null (admin UI'dan seçer).
    /// </summary>
    /// <summary>
    /// Çekin masraf merkezi: seçilen kod → proje kodu → şirket kodu.
    /// Fatura tarafındaki üç kademeli çözümlemenin aynısı; iki modül
    /// farklı kural işletirse aynı şantiyenin gideri iki ayrı kod
    /// altında toplanırdı.
    /// </summary>
    private async Task<string?> ResolveChequeCostCenterAsync(
        Cheque cheque, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(cheque.CostCenterCode))
            return cheque.CostCenterCode.Trim();

        if (cheque.ProjectId is Guid projectId)
        {
            var projectCode = await db.Projects
                .Where(x => x.Id == projectId)
                .Select(x => x.Code)
                .SingleOrDefaultAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(projectCode))
                return projectCode;
        }

        return await db.Companies
            .Where(x => x.Id == cheque.CompanyId)
            .Select(x => x.Code)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Guid?> CreateChequeVoucherAsync(
        Cheque cheque,
        ChequeStatus? fromStatus,
        ChequeStatus toStatus,
        DateTime voucherDate,
        CashAccount? cashAccount,
        CancellationToken cancellationToken = default)
    {
        var settings = await GetOrCreateFinanceSettingsAsync(
            cheque.CompanyId, cancellationToken);

        CurrentAccount? counterparty = null;
        if (cheque.CurrentAccountId is not null)
        {
            counterparty = await db.CurrentAccounts
                .SingleOrDefaultAsync(
                    x => x.Id == cheque.CurrentAccountId.Value, cancellationToken);
        }

        var amount = decimal.Round(cheque.Amount, 2);
        if (amount <= 0m)
            throw new InvalidOperationException("Çek tutarı sıfırdan büyük olmalıdır.");

        // Çekin DEFTER kuru: keşide anında sabitlendi ve ömrü boyunca
        // değişmez. Geçmiş kaydı sonradan yeniden çözmek, TCMB arşivi
        // güncellendiğinde eski fişle tutmayan bir fark üretirdi.
        //
        // Eski kayıtlarda alan 0 kalmış olabilir; o durumda 1 kabul
        // ediliyor — hepsi TRY olduğu için sonuç değişmiyor.
        var bookRate = cheque.ExchangeRate > 0m ? cheque.ExchangeRate : 1m;

        // Çekin durumuna karşılık gelen muhasebe hesabı.
        async Task<Guid> ChequeAccountAsync(ChequeStatus status)
        {
            var codes = status switch
            {
                ChequeStatus.Portfolio => new[] { "101.01", "101" },
                ChequeStatus.AtBank => new[] { "101.02", "101" },
                ChequeStatus.AtFactoring => new[] { "101.03", "101" },
                ChequeStatus.Issued => new[] { "103.01", "103" },
                _ => new[] { "101" }
            };

            var id = await FindAccountIdAsync(cheque.CompanyId, cancellationToken, codes);
            if (id is null)
            {
                throw new InvalidOperationException(
                    $"Çek hesabı bulunamadı ({string.Join(" / ", codes)}). " +
                    "Hesap planında ilgili hesabı tanımlayın.");
            }

            return id.Value;
        }

        // Dağılım YALNIZCA cari tarafına uygulanır; hangi satırın cari
        // olduğunu bilmek için çözümlenen hesap burada tutulur.
        Guid? counterpartyAccountId = null;

        Guid CounterpartyAccount(bool receivable)
        {
            var id = receivable
                ? counterparty?.ReceivableAccountingAccountId ?? settings.ReceivablesAccountId
                : counterparty?.PayableAccountingAccountId ?? settings.PayablesAccountId;

            if (id is null)
            {
                throw new InvalidOperationException(
                    receivable
                        ? "Alıcılar (120) hesabı belirlenemedi. Şirket Ayarları → Finans Ayarları'ndan seçin."
                        : "Satıcılar (320) hesabı belirlenemedi. Şirket Ayarları → Finans Ayarları'ndan seçin.");
            }

            counterpartyAccountId = id.Value;
            return id.Value;
        }

        Guid CashAccountOrThrow()
        {
            if (cashAccount is null)
            {
                throw new InvalidOperationException(
                    "Bu geçiş için kasa/banka hesabı seçilmelidir.");
            }

            return cashAccount.AccountingAccountId;
        }

        // (borç hesabı, alacak hesabı, açıklama) — geçişin muhasebe karşılığı.
        (Guid Debit, Guid Credit, string Description)? entry = (fromStatus, toStatus) switch
        {
            // Alınan çek girişi: portföye alındı, cari alacağı kapanır.
            (null, ChequeStatus.Portfolio) =>
                (await ChequeAccountAsync(ChequeStatus.Portfolio),
                 CounterpartyAccount(receivable: true),
                 $"Alınan çek — {counterparty?.Title ?? "cari"}"),

            // Verilen çek girişi: satıcı borcu çek borcuna dönüşür.
            (null, ChequeStatus.Issued) =>
                (CounterpartyAccount(receivable: false),
                 await ChequeAccountAsync(ChequeStatus.Issued),
                 $"Verilen çek — {counterparty?.Title ?? "cari"}"),

            // Portföyden bankaya tahsile/teminata verildi ve geri alınması.
            (ChequeStatus.Portfolio, ChequeStatus.AtBank) =>
                (await ChequeAccountAsync(ChequeStatus.AtBank),
                 await ChequeAccountAsync(ChequeStatus.Portfolio),
                 "Çek bankaya tahsile verildi"),

            (ChequeStatus.AtBank, ChequeStatus.Portfolio) =>
                (await ChequeAccountAsync(ChequeStatus.Portfolio),
                 await ChequeAccountAsync(ChequeStatus.AtBank),
                 "Çek bankadan geri alındı"),

            // Tahsil: para kasaya/bankaya girer, çek hesabı kapanır.
            (ChequeStatus.Portfolio, ChequeStatus.Collected) =>
                (CashAccountOrThrow(),
                 await ChequeAccountAsync(ChequeStatus.Portfolio),
                 "Çek tahsil edildi"),

            (ChequeStatus.AtBank, ChequeStatus.Collected) =>
                (CashAccountOrThrow(),
                 await ChequeAccountAsync(ChequeStatus.AtBank),
                 "Çek tahsil edildi"),

            // Faktoringdeki çekin tahsil bildirimi: para zaten kırdırma
            // anında alındığı için muhasebe etkisi yok.
            (ChequeStatus.AtFactoring, ChequeStatus.Collected) => null,

            // Karşılıksız: alacak cariye geri döner.
            (ChequeStatus.Portfolio, ChequeStatus.Bounced) =>
                (CounterpartyAccount(receivable: true),
                 await ChequeAccountAsync(ChequeStatus.Portfolio),
                 "Karşılıksız çek — alacak cariye döndü"),

            (ChequeStatus.AtBank, ChequeStatus.Bounced) =>
                (CounterpartyAccount(receivable: true),
                 await ChequeAccountAsync(ChequeStatus.AtBank),
                 "Karşılıksız çek — alacak cariye döndü"),

            // Faktoringdeki çek karşılıksız çıkarsa rücu: parayı faktoring
            // şirketine iade ederiz, alacak cariye geri döner.
            (ChequeStatus.AtFactoring, ChequeStatus.Bounced) =>
                (CounterpartyAccount(receivable: true),
                 CashAccountOrThrow(),
                 "Karşılıksız çek — faktoring rücu iadesi"),

            // Verilen çek vadesinde ödendi.
            (ChequeStatus.Issued, ChequeStatus.Paid) =>
                (await ChequeAccountAsync(ChequeStatus.Issued),
                 CashAccountOrThrow(),
                 "Verilen çek ödendi"),

            // ERTELEME: eski çekin girişi ters kayıtla kapanır, yerine
            // geçen yeni çek kendi fişini üretir. Net etki yalnızca
            // vadenin değişmesidir; borç/alacak yeniden doğmaz.
            (ChequeStatus.Portfolio, ChequeStatus.Replaced) =>
                (CounterpartyAccount(receivable: true),
                 await ChequeAccountAsync(ChequeStatus.Portfolio),
                 "Çek ertelendi — yenisiyle değiştirildi"),

            (ChequeStatus.AtBank, ChequeStatus.Replaced) =>
                (CounterpartyAccount(receivable: true),
                 await ChequeAccountAsync(ChequeStatus.AtBank),
                 "Çek ertelendi — yenisiyle değiştirildi"),

            (ChequeStatus.Issued, ChequeStatus.Replaced) =>
                (await ChequeAccountAsync(ChequeStatus.Issued),
                 CounterpartyAccount(receivable: false),
                 "Verilen çek ertelendi — yenisiyle değiştirildi"),

            // Verilen çek iade alındı: borç yeniden satıcıda.
            (ChequeStatus.Issued, ChequeStatus.Returned) =>
                (await ChequeAccountAsync(ChequeStatus.Issued),
                 CounterpartyAccount(receivable: false),
                 "Verilen çek iade alındı"),

            _ => throw new InvalidOperationException(
                $"'{fromStatus}' → '{toStatus}' geçişi için muhasebe kaydı tanımlı değil.")
        };

        if (entry is null)
            return null;

        // Aynı hesaba borç ve alacak yazan geçiş (hesap planında 101 alt
        // kırılımı yoksa) muhasebede anlamsız — fiş üretilmez.
        if (entry.Value.Debit == entry.Value.Credit)
            return null;

        var voucherType = toStatus switch
        {
            ChequeStatus.Collected => AccountingVoucherType.Collection,
            ChequeStatus.Paid => AccountingVoucherType.Payment,
            _ => AccountingVoucherType.Journal
        };

        // Masraf merkezi: çekte açıkça seçilen kod, yoksa proje kodu, o da
        // yoksa şirket kodu. Ofis kirası çekinin projesi yoktur ama
        // Merkez'e yazılabilmelidir; kod boş kalırsa çek muhasebede
        // hangi birime ait olduğu belirsiz durur.
        var costCenterCode = await ResolveChequeCostCenterAsync(cheque, cancellationToken);

        var allocations = await db.ChequeAllocations
            .AsNoTracking()
            .Where(x => x.ChequeId == cheque.Id)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Amount,
                x.ProjectId,
                x.CostCenterCode,
                SupplierInvoiceNumber = x.SupplierInvoice != null
                    ? x.SupplierInvoice.InvoiceNumber
                    : null,
                SalesInvoiceNumber = x.SalesInvoice != null
                    ? x.SalesInvoice.InternalNumber
                    : null
            })
            .ToListAsync(cancellationToken);

        // Dağılım toplamı çek tutarını tutmuyorsa fiş dengesiz çıkardı;
        // yanlış fiş kesmektense işlemi durdurmak doğru.
        if (allocations.Count > 0 && allocations.Sum(x => x.Amount) != amount)
        {
            throw new InvalidOperationException(
                $"Çek dağılımı toplamı ({TurkishFormat.Amount(allocations.Sum(x => x.Amount))}) " +
                $"çek tutarına ({TurkishFormat.Amount(amount)}) eşit değil; fiş kesilemez.");
        }

        List<AccountingVoucherLineRequest> BuildSide(Guid accountId, bool isDebit)
        {
            // Yalnızca cari tarafı bölünür: 101/103 bir enstrüman hesabıdır,
            // kasa/banka gibi projesi yoktur.
            var splittable = allocations.Count > 0 &&
                             counterpartyAccountId is Guid id &&
                             id == accountId;

            if (!splittable)
            {
                return
                [
                    new AccountingVoucherLineRequest(
                        AccountingAccountId: accountId,
                        Description: entry.Value.Description,
                        DebitAmount: isDebit ? amount : 0m,
                        CreditAmount: isDebit ? 0m : amount,
                        CurrencyCode: cheque.CurrencyCode,
                        // Çekin DEFTER kuru. Sabit 1 bırakıldığı sürece
                        // dövizli çek TL tutarıyla aynı deftere giriyordu.
                        ExchangeRate: bookRate,
                        CurrentAccountId: cheque.CurrentAccountId,
                        ProjectId: cheque.ProjectId,
                        CostCenterCode: costCenterCode,
                        DocumentNumber: cheque.ChequeNumber,
                        DocumentDate: cheque.IssueDate,
                        DueDate: cheque.DueDate)
                ];
            }

            return allocations.Select(allocation =>
            {
                var invoiceNumber = allocation.SupplierInvoiceNumber
                    ?? allocation.SalesInvoiceNumber;

                var description = invoiceNumber is null
                    ? entry.Value.Description
                    : $"{entry.Value.Description} — fatura {invoiceNumber}";

                return new AccountingVoucherLineRequest(
                    AccountingAccountId: accountId,
                    Description: description,
                    DebitAmount: isDebit ? allocation.Amount : 0m,
                    CreditAmount: isDebit ? 0m : allocation.Amount,
                    CurrencyCode: cheque.CurrencyCode,
                    ExchangeRate: bookRate,
                    CurrentAccountId: cheque.CurrentAccountId,
                    ProjectId: allocation.ProjectId ?? cheque.ProjectId,
                    CostCenterCode: allocation.CostCenterCode ?? costCenterCode,
                    DocumentNumber: cheque.ChequeNumber,
                    DocumentDate: cheque.IssueDate,
                    DueDate: cheque.DueDate);
            }).ToList();
        }

        var lines = new List<AccountingVoucherLineRequest>();
        lines.AddRange(BuildSide(entry.Value.Debit, isDebit: true));
        lines.AddRange(BuildSide(entry.Value.Credit, isDebit: false));

        // --- Kur farkı: yalnızca PARA HAREKET ETTİĞİNDE ---
        //
        // Dövizli bir çek keşide kuruyla deftere girer; tahsil ya da
        // ödeme günü kur farklıysa kasaya giren/çıkan TL ile çekin
        // defter değeri arasında GERÇEKLEŞMİŞ bir fark doğar ve bu fark
        // 646/656'ya yazılmalıdır.
        //
        // Portföy → Bankada gibi geçişlerde para hareket etmez: aynı
        // enstrüman iki hesap arasında taşınır, defter değeri korunur ve
        // fark YAZILMAZ. Değerleme farkı (henüz gerçekleşmemiş) dönem
        // sonu işidir, bu fişin konusu değil.
        var settlesInCash =
            toStatus is ChequeStatus.Collected or ChequeStatus.Paid;

        if (!cheque.IsLocalCurrency && settlesInCash)
        {
            var settlement = await exchangeRateResolver.ResolveAsync(
                cheque.CurrencyCode, voucherDate, null, cancellationToken);

            if (!settlement.Success)
            {
                throw new InvalidOperationException(
                    settlement.Error ??
                    $"{cheque.CurrencyCode} için tahsilat/ödeme tarihine " +
                    "kur bulunamadı; kur olmadan fiş kesilemez.");
            }

            var bookValue = decimal.Round(amount * bookRate, 2);
            var settlementValue = decimal.Round(amount * settlement.Rate, 2);
            var difference = decimal.Round(settlementValue - bookValue, 2);

            // Kasa/banka satırı GERÇEKTEN hareket eden TL'yi taşımalı:
            // tahsilat günü 38 ise kasaya 38 × tutar girer, çekin 35'lik
            // defter değeri değil. Satır aynı para biriminde kalıyor,
            // yalnızca kuru tahsilat kuruna çekiliyor.
            var cashAccountId = cashAccount?.AccountingAccountId;

            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].AccountingAccountId == cashAccountId)
                    lines[i] = lines[i] with { ExchangeRate = settlement.Rate };
            }

            if (difference != 0m)
            {
                // Fark İŞARETİ yön belirler:
                // Alınan çekte kur yükseldiyse elimize daha çok TL geçti
                // (kâr); verilen çekte kur yükseldiyse daha çok TL ödedik
                // (zarar). Bu yüzden yön çekin yönüne göre ters çevriliyor.
                var isGain = cheque.Direction == ChequeDirection.Received
                    ? difference > 0m
                    : difference < 0m;

                var codes = isGain
                    ? new[] { "646.01", "646" }
                    : new[] { "656.01", "656" };

                var accountId = await FindAccountIdAsync(
                    cheque.CompanyId, cancellationToken, codes);

                if (accountId is null)
                {
                    throw new InvalidOperationException(
                        $"Kambiyo {(isGain ? "kârı" : "zararı")} hesabı " +
                        $"bulunamadı ({string.Join(" / ", codes)}). " +
                        "Hesap planında ilgili hesabı tanımlayın.");
                }

                var magnitude = Math.Abs(difference);

                lines.Add(new AccountingVoucherLineRequest(
                    AccountingAccountId: accountId.Value,
                    Description:
                        $"Kur farkı — {cheque.CurrencyCode} " +
                        $"{TurkishFormat.Rate(bookRate)} → " +
                        $"{TurkishFormat.Rate(settlement.Rate)}",
                    // Kâr alacağa, zarar borca yazılır.
                    DebitAmount: isGain ? 0m : magnitude,
                    CreditAmount: isGain ? magnitude : 0m,
                    CurrencyCode: "TRY",
                    ExchangeRate: 1m,
                    CurrentAccountId: cheque.CurrentAccountId,
                    ProjectId: cheque.ProjectId,
                    CostCenterCode: costCenterCode,
                    DocumentNumber: cheque.ChequeNumber,
                    DocumentDate: cheque.IssueDate,
                    DueDate: cheque.DueDate));
            }
        }

        var created = await voucherService.CreateAsync(
            new CreateAccountingVoucherRequest(
                CompanyId: cheque.CompanyId,
                VoucherType: (int)voucherType,
                VoucherDate: voucherDate,
                CurrencyCode: cheque.CurrencyCode,
                ExchangeRate: bookRate,
                Description: $"{cheque.InternalNumber} — {entry.Value.Description}",
                ReferenceNumber: cheque.ChequeNumber,
                SourceModule: "Cheque",
                SourceEntityId: cheque.Id,
                Lines: lines),
            cancellationToken);

        await voucherService.PostAsync(created.Id, cancellationToken);

        return created.Id;
    }

    public async Task<Guid> CreateFactoringVoucherAsync(
        FactoringTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        var settings = await GetOrCreateFinanceSettingsAsync(
            transaction.CompanyId, cancellationToken);

        if (settings.FactoringExpenseAccountId is null)
        {
            throw new InvalidOperationException(
                "Finansman gideri hesabı (780) yapılandırılmamış. " +
                "Şirket Ayarları → Finans Ayarları'ndan seçin.");
        }

        var cheque = await db.Cheques
            .SingleAsync(x => x.Id == transaction.ChequeId, cancellationToken);

        var cashAccount = await db.CashAccounts
            .SingleAsync(x => x.Id == transaction.CashAccountId, cancellationToken);

        var chequeAccountId = await FindAccountIdAsync(
            transaction.CompanyId, cancellationToken, "101.01", "101");

        if (chequeAccountId is null)
        {
            throw new InvalidOperationException(
                "Alınan çekler hesabı (101) bulunamadı. Hesap planını kontrol edin.");
        }

        var nominal = decimal.Round(transaction.ChequeAmount, 2);
        var net = decimal.Round(transaction.NetAmount, 2);
        var deduction = decimal.Round(transaction.TotalDeductionAmount, 2);

        if (net + deduction != nominal)
        {
            throw new InvalidOperationException(
                $"Faktoring tutarları tutarsız: net ({TurkishFormat.Amount(net)}) + kesinti ({TurkishFormat.Amount(deduction)}) " +
                $"≠ çek tutarı ({TurkishFormat.Amount(nominal)}).");
        }

        var project = transaction.ProjectId is null
            ? null
            : await db.Projects
                .SingleOrDefaultAsync(x => x.Id == transaction.ProjectId.Value, cancellationToken);

        var lines = new List<AccountingVoucherLineRequest>
        {
            new(
                AccountingAccountId: cashAccount.AccountingAccountId,
                Description: $"Faktoring net tahsilat — {cashAccount.Name}",
                DebitAmount: net,
                CreditAmount: 0m,
                CurrencyCode: transaction.CurrencyCode,
                ExchangeRate: 1m,
                CurrentAccountId: transaction.FactoringCurrentAccountId,
                ProjectId: transaction.ProjectId,
                CostCenterCode: project?.Code,
                DocumentNumber: transaction.InternalNumber,
                DocumentDate: transaction.TransactionDate,
                DueDate: null)
        };

        // Kesintiler ayrı satırlarda: komisyon, BSMV ve masraf tek tek
        // izlenebilsin (hepsi 780 Finansman Giderleri altında).
        void AddDeductionLine(decimal value, string description)
        {
            if (value <= 0m)
                return;

            lines.Add(new AccountingVoucherLineRequest(
                AccountingAccountId: settings.FactoringExpenseAccountId!.Value,
                Description: description,
                DebitAmount: decimal.Round(value, 2),
                CreditAmount: 0m,
                CurrencyCode: transaction.CurrencyCode,
                ExchangeRate: 1m,
                CurrentAccountId: transaction.FactoringCurrentAccountId,
                ProjectId: transaction.ProjectId,
                CostCenterCode: project?.Code,
                DocumentNumber: transaction.InternalNumber,
                DocumentDate: transaction.TransactionDate,
                DueDate: null));
        }

        AddDeductionLine(transaction.CommissionAmount, "Faktoring komisyonu");
        AddDeductionLine(transaction.BsmvAmount, "Faktoring BSMV");
        AddDeductionLine(transaction.ExpenseAmount, "Faktoring masrafı");

        lines.Add(new AccountingVoucherLineRequest(
            AccountingAccountId: chequeAccountId.Value,
            Description: $"Kırdırılan çek — {cheque.ChequeNumber}",
            DebitAmount: 0m,
            CreditAmount: nominal,
            CurrencyCode: transaction.CurrencyCode,
            ExchangeRate: 1m,
            CurrentAccountId: cheque.CurrentAccountId,
            ProjectId: transaction.ProjectId,
            CostCenterCode: project?.Code,
            DocumentNumber: cheque.ChequeNumber,
            DocumentDate: cheque.IssueDate,
            DueDate: cheque.DueDate));

        var created = await voucherService.CreateAsync(
            new CreateAccountingVoucherRequest(
                CompanyId: transaction.CompanyId,
                VoucherType: (int)AccountingVoucherType.Collection,
                VoucherDate: transaction.TransactionDate,
                CurrencyCode: transaction.CurrencyCode,
                ExchangeRate: 1m,
                Description: $"Çek kırdırma {transaction.InternalNumber} — {cheque.ChequeNumber}",
                ReferenceNumber: cheque.ChequeNumber,
                SourceModule: "Factoring",
                SourceEntityId: transaction.Id,
                Lines: lines),
            cancellationToken);

        await voucherService.PostAsync(created.Id, cancellationToken);

        return created.Id;
    }

    /// <summary>
    /// Bordro gider satırlarını masraf merkezine göre böler.
    ///
    /// Kırılım yoksa, tek merkez varsa ya da tutarların toplamı gider
    /// toplamına eşit değilse tek satır döner. Toplam tutmuyorsa
    /// bölmemek bilinçli: yarım bir kırılım uğruna fişi dengesizleştirmek
    /// veya sessizce düzeltmek, muhasebede izi sürülemeyen bir fark
    /// yaratırdı.
    /// </summary>
    private static List<AccountingVoucherLineRequest> BuildPayrollExpenseLines(
        Guid expenseAccountId,
        decimal expense,
        string period,
        PayrollAccrualTotals totals,
        string fallbackCostCenterCode,
        DateTime voucherDate)
    {
        AccountingVoucherLineRequest SingleLine() => new(
            AccountingAccountId: expenseAccountId,
            Description: $"{period} dönemi personel gideri ({totals.PersonnelCount} kişi)",
            DebitAmount: expense,
            CreditAmount: 0m,
            CurrencyCode: "TRY",
            ExchangeRate: 1m,
            CurrentAccountId: null,
            ProjectId: null,
            CostCenterCode: fallbackCostCenterCode,
            DocumentNumber: null,
            DocumentDate: voucherDate,
            DueDate: null);

        var shares = totals.CostCenters?
            .Where(x => x.ExpenseAmount > 0m)
            .ToList();

        if (shares is null || shares.Count == 0)
            return [SingleLine()];

        var shareTotal = decimal.Round(shares.Sum(x => x.ExpenseAmount), 2);

        if (shareTotal != expense)
            return [SingleLine()];

        // Tek merkez kalsa bile o merkezin kodu yazılır; şirket koduna
        // düşmek merkez giderini defterde adsız bırakırdı.
        return shares
            .OrderByDescending(x => x.ExpenseAmount)
            .Select(share => new AccountingVoucherLineRequest(
                AccountingAccountId: expenseAccountId,
                Description:
                    $"{period} dönemi personel gideri — {share.Label} " +
                    $"({share.PersonnelCount} kişi)",
                DebitAmount: decimal.Round(share.ExpenseAmount, 2),
                CreditAmount: 0m,
                CurrencyCode: "TRY",
                ExchangeRate: 1m,
                CurrentAccountId: null,
                ProjectId: null,
                CostCenterCode: share.Code,
                DocumentNumber: null,
                DocumentDate: voucherDate,
                DueDate: null))
            .ToList();
    }

    public async Task<Guid> CreatePayrollAccrualVoucherAsync(
        Guid companyId,
        int year,
        int month,
        PayrollAccrualTotals totals,
        CancellationToken cancellationToken = default)
    {
        var settings = await GetOrCreateFinanceSettingsAsync(companyId, cancellationToken);

        Guid Required(Guid? accountId, string label)
        {
            if (accountId is null)
            {
                throw new InvalidOperationException(
                    $"{label} hesabı yapılandırılmamış. " +
                    "Şirket Ayarları → Finans Ayarları'ndan seçin.");
            }

            return accountId.Value;
        }

        var expenseAccountId = Required(
            settings.PayrollExpenseAccountId, "Bordro gideri (770)");
        var payableAccountId = Required(
            settings.PayrollPayableAccountId, "Personele borçlar (335)");
        var taxAccountId = Required(
            settings.TaxPayableAccountId, "Ödenecek vergi ve fonlar (360)");
        var sgkAccountId = Required(
            settings.SocialSecurityPayableAccountId, "Ödenecek SGK kesintileri (361)");

        var employerBurden = Round(totals.SgkEmployer + totals.UnemploymentEmployer);
        var expense = Round(totals.TotalEarnings + employerBurden);

        var netPayable = Round(totals.NetPayable);
        var taxTotal = Round(totals.IncomeTax + totals.StampTax);
        var sgkTotal = Round(
            totals.SgkEmployee + totals.UnemploymentEmployee + employerBurden);
        var advances = Round(totals.AdvanceAndOtherDeductions);

        if (expense <= 0m)
            throw new InvalidOperationException("Dönemde tahakkuk edecek bordro tutarı yok.");

        // Denge ön kontrolü: brüt + işveren payı, net + vergi + SGK +
        // avans toplamına eşit olmalı. Tutmuyorsa bordro toplamları
        // kendi içinde tutarsızdır; asıl doğrulama PostAsync'te tekrar
        // yapılır ama hatayı burada anlaşılır biçimde veriyoruz.
        var creditTotal = Round(netPayable + taxTotal + sgkTotal + advances);
        if (creditTotal != expense)
        {
            throw new InvalidOperationException(
                $"Bordro toplamları tutarsız: gider ({TurkishFormat.Amount(expense)}) ≠ " +
                $"net + vergi + SGK + avans ({TurkishFormat.Amount(creditTotal)}).");
        }

        var voucherDate = new DateTime(
            year, month, DateTime.DaysInMonth(year, month), 0, 0, 0, DateTimeKind.Utc);

        var period = $"{month:00}/{year}";

        // Bordro şirket geneli bir giderdir; hesap planında gider hesabı
        // masraf merkezi zorunlu tutuyorsa şirket kodu kullanılır. Proje
        // ve şantiye bazlı işçilik dağılımı ayrıca HrProjectLaborCost
        // üzerinden izlenir.
        var costCenterCode = await db.Companies
            .Where(x => x.Id == companyId)
            .Select(x => x.Code)
            .SingleAsync(cancellationToken);

        // Gider satırı masraf merkezine göre bölünür: merkez personelinin
        // gideri merkez ofise, şantiye personelininki projesine yazılır.
        // Kırılım gelmezse (veya toplamı tutmazsa) tek satır kesilir —
        // eksik bir kırılım yüzünden fiş dengesizleşmemeli.
        var expenseLines = BuildPayrollExpenseLines(
            expenseAccountId, expense, period, totals, costCenterCode, voucherDate);

        var lines = new List<AccountingVoucherLineRequest>(expenseLines)
        {
            new(
                AccountingAccountId: payableAccountId,
                Description: $"{period} dönemi net ücret tahakkuku",
                DebitAmount: 0m,
                CreditAmount: netPayable,
                CurrencyCode: "TRY",
                ExchangeRate: 1m,
                CurrentAccountId: null,
                ProjectId: null,
                CostCenterCode: costCenterCode,
                DocumentNumber: null,
                DocumentDate: voucherDate,
                DueDate: null)
        };

        void AddCreditLine(Guid accountId, decimal amount, string description)
        {
            if (amount <= 0m)
                return;

            lines.Add(new AccountingVoucherLineRequest(
                AccountingAccountId: accountId,
                Description: description,
                DebitAmount: 0m,
                CreditAmount: amount,
                CurrencyCode: "TRY",
                ExchangeRate: 1m,
                CurrentAccountId: null,
                ProjectId: null,
                CostCenterCode: costCenterCode,
                DocumentNumber: null,
                DocumentDate: voucherDate,
                DueDate: null));
        }

        AddCreditLine(taxAccountId, taxTotal, $"{period} gelir ve damga vergisi");
        AddCreditLine(sgkAccountId, sgkTotal, $"{period} SGK kesintileri (işçi + işveren)");

        if (advances > 0m)
        {
            var advanceAccountId = Required(
                settings.EmployeeAdvanceAccountId, "İş avansları (195)");
            AddCreditLine(advanceAccountId, advances, $"{period} avans ve diğer kesintiler");
        }

        var created = await voucherService.CreateAsync(
            new CreateAccountingVoucherRequest(
                CompanyId: companyId,
                VoucherType: (int)AccountingVoucherType.Journal,
                VoucherDate: voucherDate,
                CurrencyCode: "TRY",
                ExchangeRate: 1m,
                Description: $"{period} dönemi bordro tahakkuku",
                ReferenceNumber: $"BORDRO-{year}-{month:00}",
                SourceModule: "PayrollAccrual",
                SourceEntityId: null,
                Lines: lines),
            cancellationToken);

        await voucherService.PostAsync(created.Id, cancellationToken);

        return created.Id;
    }

    public async Task<PayrollPaymentPostingResult> CreatePayrollPaymentVoucherAsync(
        Guid companyId,
        int year,
        int month,
        Guid cashAccountId,
        decimal amount,
        DateTime paymentDate,
        CancellationToken cancellationToken = default)
    {
        var settings = await GetOrCreateFinanceSettingsAsync(companyId, cancellationToken);

        if (settings.PayrollPayableAccountId is null)
        {
            throw new InvalidOperationException(
                "Personele borçlar (335) hesabı yapılandırılmamış. " +
                "Şirket Ayarları → Finans Ayarları'ndan seçin.");
        }

        var cashAccount = await db.CashAccounts
            .SingleOrDefaultAsync(
                x => x.Id == cashAccountId && x.CompanyId == companyId, cancellationToken);

        if (cashAccount is null)
            throw new InvalidOperationException("Kasa/banka hesabı bulunamadı.");

        var payment = Round(amount);
        if (payment <= 0m)
            throw new InvalidOperationException("Ödeme tutarı sıfırdan büyük olmalıdır.");

        var date = DateTime.SpecifyKind(paymentDate.Date, DateTimeKind.Utc);
        var period = $"{month:00}/{year}";
        var description = $"{period} dönemi net ücret ödemesi";

        var lines = new List<AccountingVoucherLineRequest>
        {
            new(
                AccountingAccountId: settings.PayrollPayableAccountId.Value,
                Description: description,
                DebitAmount: payment,
                CreditAmount: 0m,
                CurrencyCode: "TRY",
                ExchangeRate: 1m,
                CurrentAccountId: null,
                ProjectId: null,
                CostCenterCode: null,
                DocumentNumber: null,
                DocumentDate: date,
                DueDate: null),
            new(
                AccountingAccountId: cashAccount.AccountingAccountId,
                Description: $"{cashAccount.Name} — {description}",
                DebitAmount: 0m,
                CreditAmount: payment,
                CurrencyCode: "TRY",
                ExchangeRate: 1m,
                CurrentAccountId: null,
                ProjectId: null,
                CostCenterCode: null,
                DocumentNumber: null,
                DocumentDate: date,
                DueDate: null)
        };

        var created = await voucherService.CreateAsync(
            new CreateAccountingVoucherRequest(
                CompanyId: companyId,
                VoucherType: (int)AccountingVoucherType.Payment,
                VoucherDate: date,
                CurrencyCode: "TRY",
                ExchangeRate: 1m,
                Description: description,
                ReferenceNumber: $"BORDRO-ODEME-{year}-{month:00}",
                SourceModule: "PayrollPayment",
                SourceEntityId: null,
                Lines: lines),
            cancellationToken);

        await voucherService.PostAsync(created.Id, cancellationToken);

        // Kasa hareketi aynı fişe bağlanır; ikinci bir fiş üretilmez.
        var cashTransaction = new CashTransaction
        {
            CashAccountId = cashAccount.Id,
            TransactionDate = date,
            TransactionType = CashTransactionType.Payment,
            Direction = CashTransactionDirection.Out,
            Amount = payment,
            CurrencyCode = "TRY",
            Description = description,
            DocumentNumber = $"BORDRO-{year}-{month:00}",
            SourceModule = "PayrollPayment",
            AccountingVoucherId = created.Id
        };

        db.CashTransactions.Add(cashTransaction);
        await db.SaveChangesAsync(cancellationToken);

        return new PayrollPaymentPostingResult(created.Id, cashTransaction.Id);
    }

    private static decimal Round(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

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
