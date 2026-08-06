using System.IO.Compression;
using System.Text;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.DocumentNumbers;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.EInvoice;

/// <summary>Önizlemedeki tek bir faturanın özeti.</summary>
public sealed record ImportPreviewItem(
    string FileName,
    bool CanImport,
    int Direction,
    string DirectionName,
    string? InvoiceNumber,
    DateTime? IssueDate,
    string? CounterpartyTaxNumber,
    string? CounterpartyName,
    /// <summary>VKN ile eşleşen cari; yoksa null (yeni cari önerilir).</summary>
    Guid? MatchedCurrentAccountId,
    string? MatchedCurrentAccountTitle,
    decimal Subtotal,
    decimal VatTotal,
    decimal WithholdingAmount,
    decimal GrandTotal,
    IReadOnlyList<ImportPreviewLine> Lines,
    int ParseSource,
    string ParseSourceName,
    bool RequiresManualReview,
    /// <summary>Aynı fatura daha önce girilmişse dolu.</summary>
    Guid? DuplicateOfId,
    IReadOnlyList<string> Problems,
    /// <summary>
    /// Anahtar kelimeden çıkan tip önerisi (0 Alış / 1 Gider). Yalnızca
    /// öneridir; ekranda seçili gelir, kullanıcı değiştirebilir.
    /// </summary>
    int SuggestedInvoiceType,
    string SuggestedInvoiceTypeName,
    /// <summary>Öneri gider ise ve hesap planında karşılığı varsa dolu.</summary>
    Guid? SuggestedExpenseAccountId,
    string? SuggestedExpenseAccountCode,
    string? SuggestedExpenseAccountName,
    /// <summary>Önerinin neden yapıldığı — kullanıcı körü körüne onaylamasın.</summary>
    string? SuggestionReason,
    /// <summary>
    /// Belge bir IADE faturası mı (UBL InvoiceTypeCode = IADE).
    /// </summary>
    bool IsReturn,
    /// <summary>İade faturasının XML'de atıf yaptığı fatura numarası.</summary>
    string? ReferencedInvoiceNumber,
    /// <summary>Numaradan eşleşen orijinal fatura; yoksa kullanıcı seçer.</summary>
    Guid? MatchedOriginalInvoiceId,
    string? MatchedOriginalInvoiceNumber,
    /// <summary>Commit çağrısında geri gönderilecek anahtar.</summary>
    string Token);

public sealed record ImportPreviewLine(
    string Description,
    decimal Quantity,
    string Unit,
    decimal UnitPrice,
    decimal VatRate,
    decimal LineSubtotal,
    decimal VatAmount);

public sealed record ImportPreviewResult(
    int TotalFiles,
    int ReadableCount,
    int SkippedCount,
    IReadOnlyList<ImportPreviewItem> Items,
    /// <summary>Okunamayan dosyalar ve sebepleri.</summary>
    IReadOnlyList<ImportSkippedFile> Skipped);

public sealed record ImportSkippedFile(string FileName, string Reason);

/// <summary>Önizlemede onaylanan tek bir faturanın kesinleştirme talebi.</summary>
public sealed record ImportCommitItem(
    string Token,
    /// <summary>Eşleşen cari; yeni cari isteniyorsa boş bırakılır.</summary>
    Guid? CurrentAccountId,
    /// <summary>VKN eşleşmediyse XML'deki unvan+VKN ile yeni cari açılır.</summary>
    bool CreateCurrentAccount,
    /// <summary>
    /// Proje. Ofis elektriği, kira, müşavirlik gibi giderlerin projesi
    /// olmadığı için ZORUNLU DEĞİL.
    /// </summary>
    Guid? ProjectId = null,
    /// <summary>0 Alış (Stok) / 1 Gider. Varsayılan alış.</summary>
    int InvoiceType = 0,
    /// <summary>
    /// Gider faturasında kalemlerin yazılacağı hesap. Gider tipinde
    /// zorunlu: hesapsız gider faturası onaya geldiğinde fiş üretilemez.
    /// </summary>
    Guid? ExpenseAccountId = null,
    /// <summary>Masraf merkezi (Merkez veya şantiye kodu).</summary>
    string? CostCenterCode = null,
    /// <summary>
    /// Alış faturasında stok girişinin yapılacağı depo. E-faturada stok
    /// kartı eşleştirmesi yapılmadığı için depo tek başına stok girişi
    /// başlatmaz; kalemlere stok kartı fatura ekranından bağlanır.
    /// </summary>
    Guid? WarehouseId = null,
    /// <summary>
    /// İade faturasında iade edilen orijinal fatura. Önizlemedeki
    /// eşleşme yalnızca öneridir; onaylayan kullanıcıdır.
    /// </summary>
    Guid? OriginalInvoiceId = null);

public sealed record ImportCommitRequest(IReadOnlyList<ImportCommitItem> Items);

public sealed record ImportCommitCreated(
    string FileName,
    int Direction,
    string DirectionName,
    Guid InvoiceId,
    string InternalNumber,
    string? InvoiceNumber,
    string CurrentAccountTitle,
    bool CurrentAccountCreated,
    decimal GrandTotal,
    bool RequiresManualReview);

public sealed record ImportCommitResult(
    int CreatedCount,
    int SkippedCount,
    IReadOnlyList<ImportCommitCreated> Created,
    IReadOnlyList<ImportSkippedFile> Skipped);

public interface IEInvoiceImportService
{
    /// <summary>
    /// Dosyaları okur ve önizleme döner. HİÇBİR KAYIT YAZMAZ —
    /// ön muhasebe kontrol etmeden hiçbir şey sisteme girmemeli.
    /// </summary>
    Task<ImportPreviewResult> PreviewAsync(
        Guid companyId,
        IReadOnlyList<(string FileName, Stream Content)> files,
        CancellationToken cancellationToken);

    /// <summary>
    /// Önizlenen faturaları kaydeder. Alış faturası taslak
    /// <c>SupplierInvoice</c> olur (mevcut onay/3 yönlü kontrol akışı
    /// aynen devam eder), satış faturası taslak <c>SalesInvoice</c>.
    /// Fiş üretilmez — o mevcut onay adımlarının işi.
    /// </summary>
    Task<ImportCommitResult> CommitAsync(
        Guid companyId,
        ImportCommitRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// E-fatura içe aktarma. XML/ZIP okur, yönü VKN'den belirler, cariyi
/// eşleştirir ve önizleme üretir.
///
/// Toplu yüklemede her dosya TEK TEK işlenir ve biri patlarsa
/// diğerleri devam eder; sonuçta kaç başarılı kaç atlandı ve neden
/// atlandığı raporlanır.
/// </summary>
public sealed class EInvoiceImportService(
    AppDbContext db,
    IEInvoiceReader reader,
    IEInvoiceStagingStore staging,
    IDocumentNumberService documentNumberService,
    IEInvoiceArchive archive,
    EnderunAI.Api.Services.Market.IInvoiceExchangeRateResolver rateResolver)
    : IEInvoiceImportService
{
    public async Task<ImportPreviewResult> PreviewAsync(
        Guid companyId,
        IReadOnlyList<(string FileName, Stream Content)> files,
        CancellationToken cancellationToken)
    {
        var company = await db.Companies
            .AsNoTracking()
            .Where(x => x.Id == companyId)
            .Select(x => new { x.Id, x.Name, x.TaxNumber })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Şirket bulunamadı.");

        if (string.IsNullOrWhiteSpace(company.TaxNumber))
        {
            throw new InvalidOperationException(
                "Şirketin vergi numarası tanımlı değil; faturanın yönü " +
                "belirlenemez. Şirket Ayarları'ndan VKN girin.");
        }

        var expanded = await ExpandAsync(files, cancellationToken);

        var items = new List<ImportPreviewItem>();
        var skipped = new List<ImportSkippedFile>();

        foreach (var (fileName, xml) in expanded)
        {
            try
            {
                var item = await BuildPreviewAsync(
                    company.Id, company.TaxNumber!, fileName, xml, cancellationToken);

                if (item is null)
                {
                    skipped.Add(new ImportSkippedFile(
                        fileName,
                        "Fatura okunamadı; ayrıntı önizleme listesinde."));
                    continue;
                }

                if (item.CanImport)
                    items.Add(item);
                else
                {
                    // Okunamayan/atlanan da listede görünsün ki kullanıcı
                    // sebebini bilsin; ama içe aktarılamaz.
                    items.Add(item);
                    skipped.Add(new ImportSkippedFile(
                        fileName, string.Join(" ", item.Problems)));
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Tek dosyanın hatası tüm yüklemeyi düşürmemeli.
                skipped.Add(new ImportSkippedFile(
                    fileName, $"Beklenmeyen hata: {exception.Message}"));
            }
        }

        return new ImportPreviewResult(
            TotalFiles: expanded.Count,
            ReadableCount: items.Count(x => x.CanImport),
            SkippedCount: skipped.Count,
            Items: items,
            Skipped: skipped);
    }

    public async Task<ImportCommitResult> CommitAsync(
        Guid companyId,
        ImportCommitRequest request,
        CancellationToken cancellationToken)
    {
        var created = new List<ImportCommitCreated>();
        var skipped = new List<ImportSkippedFile>();

        foreach (var item in request.Items)
        {
            var staged = staging.Take(item.Token);

            if (staged is null)
            {
                skipped.Add(new ImportSkippedFile(
                    "(bilinmeyen dosya)",
                    "Önizleme süresi dolmuş. Dosyayı yeniden yükleyin."));
                continue;
            }

            try
            {
                created.Add(await CommitOneAsync(
                    companyId, item, staged, cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Bir faturanın hatası diğerlerinin kaydedilmesini
                // engellememeli; her fatura kendi işlemidir.
                skipped.Add(new ImportSkippedFile(staged.FileName, exception.Message));
            }
        }

        return new ImportCommitResult(
            CreatedCount: created.Count,
            SkippedCount: skipped.Count,
            Created: created,
            Skipped: skipped);
    }

    private async Task<ImportCommitCreated> CommitOneAsync(
        Guid companyId,
        ImportCommitItem item,
        StagedInvoice staged,
        CancellationToken cancellationToken)
    {
        var invoice = staged.Invoice;
        var isReturn = invoice.IsReturnDocument;
        var toSupplierLedger = TargetsSupplierLedger(staged.Direction, isReturn);

        var counterparty = staged.Direction == InvoiceDirection.Sales
            ? invoice.Customer
            : invoice.Supplier;

        var (currentAccountId, currentAccountTitle, accountCreated) =
            await ResolveCurrentAccountAsync(
                companyId, item, toSupplierLedger, counterparty, cancellationToken);

        // Mükerrer kontrolü kaydetmeden hemen önce tekrar yapılır:
        // önizleme ile onay arasında başkası aynı faturayı girmiş olabilir.
        var duplicate = await FindDuplicateAsync(
            companyId, toSupplierLedger, currentAccountId,
            invoice.InvoiceNumber, cancellationToken);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Bu fatura ({invoice.InvoiceNumber}) sistemde zaten kayıtlı; " +
                "mükerrer kayıt engellendi.");
        }

        var blocking = UblTrInvoiceValidator.CollectBlockingProblems(invoice);

        if (blocking.Count > 0)
            throw new InvalidOperationException(string.Join(" ", blocking));

        // Orijinal XML denetim izi için saklanır — okuma hatalı çıksa
        // bile kaynağa dönülebilmeli.
        var xmlPath = await archive.SaveAsync(
            staged.FileName, staged.Xml, cancellationToken);

        var parseSource = staged.Source == InvoiceParseSource.Ai
            ? EInvoiceParseSource.Ai
            : EInvoiceParseSource.Standard;

        var subtotal = Round(invoice.TaxExclusiveAmount
            ?? invoice.LineExtensionTotal
            ?? invoice.Lines.Sum(x => x.LineExtensionAmount));

        var vatTotal = Round(invoice.VatTotal);
        var withholding = Round(invoice.WithholdingAmount);
        var grandTotal = Round(invoice.TaxInclusiveAmount ?? (subtotal + vatTotal));
        var issueDate = DateTime.SpecifyKind(
            (invoice.IssueDate ?? DateTime.UtcNow).Date, DateTimeKind.Utc);

        // Dövizli faturanın kuru: önce belgenin kendi beyanı
        // (cac:PricingExchangeRate), yoksa fatura tarihinin TCMB döviz
        // alışı. Burada eskiden sabit 1 yazılıyordu; USD bir fatura
        // defterine tutarı kadar TL olarak giriyor ve tedarikçi kırk
        // küsur kat eksik alacaklandırılıyordu.
        var rate = await rateResolver.ResolveAsync(
            invoice.CurrencyCode, issueDate, invoice.ExchangeRate, cancellationToken);

        if (!rate.Success)
            throw new InvalidOperationException(rate.Error);

        var exchangeRate = rate.Rate;

        if (toSupplierLedger)
        {
            if (item.ProjectId is Guid projectId)
            {
                var projectExists = await db.Projects.AnyAsync(
                    x => x.Id == projectId && x.CompanyId == companyId, cancellationToken);

                if (!projectExists)
                    throw new InvalidOperationException("Seçilen proje bulunamadı.");
            }

            var invoiceType = ResolveInvoiceType(item.InvoiceType);

            var expenseAccountId = await ResolveExpenseAccountAsync(
                companyId, invoiceType, item.ExpenseAccountId, cancellationToken);

            var warehouseId = await ResolveWarehouseAsync(
                companyId, invoiceType, item.WarehouseId, cancellationToken);

            var costCenterCode = string.IsNullOrWhiteSpace(item.CostCenterCode)
                ? null
                : item.CostCenterCode.Trim();

            var originalSupplierInvoiceId = await ResolveOriginalSupplierInvoiceAsync(
                companyId, isReturn, item.OriginalInvoiceId, currentAccountId,
                cancellationToken);

            var internalNumber = await documentNumberService.GenerateAsync(
                companyId,
                isReturn ? "SUPPLIER_INVOICE_RETURN" : "SUPPLIER_INVOICE",
                isReturn ? "AIF" : "SFT",
                cancellationToken);

            var supplierInvoice = new SupplierInvoice
            {
                CompanyId = companyId,
                SupplierCurrentAccountId = currentAccountId,
                ProjectId = item.ProjectId,
                InvoiceType = invoiceType,
                CostCenterCode = costCenterCode,
                WarehouseId = warehouseId,
                InternalNumber = internalNumber,
                InvoiceNumber = invoice.InvoiceNumber!.Trim(),
                InvoiceDate = issueDate,
                CurrencyCode = invoice.CurrencyCode,
                ExchangeRate = exchangeRate,
                Subtotal = subtotal,
                VatTotal = vatTotal,
                GrandTotal = grandTotal,
                WithholdingAmount = withholding,
                SourceXmlPath = xmlPath,
                ParseSource = parseSource,
                RequiresManualReview = staged.RequiresManualReview,
                Description = $"E-fatura içe aktarma — {staged.FileName}",
                Status = SupplierInvoiceStatus.Draft,
                IsReturn = isReturn,
                OriginalInvoiceId = originalSupplierInvoiceId
            };

            var lineNumber = 1;

            foreach (var line in invoice.Lines)
            {
                var lineSubtotal = Round(line.LineExtensionAmount);
                var lineVat = Round(line.VatAmount);

                supplierInvoice.Items.Add(new SupplierInvoiceItem
                {
                    LineNumber = lineNumber++,
                    Description = line.Name,
                    Quantity = line.Quantity,
                    Unit = line.Unit,
                    UnitPrice = line.UnitPrice,
                    VatRate = line.VatRate,
                    LineSubtotal = lineSubtotal,
                    VatAmount = lineVat,
                    LineTotal = lineSubtotal + lineVat,
                    // Gider hesabı ve masraf merkezi tüm kalemlere aynı
                    // uygulanır; kalem bazında ayrıştırma gerekiyorsa
                    // taslak fatura ekranından değiştirilir.
                    ExpenseAccountId = expenseAccountId,
                    CostCenterCode = costCenterCode
                });
            }

            db.SupplierInvoices.Add(supplierInvoice);
            await db.SaveChangesAsync(cancellationToken);

            return new ImportCommitCreated(
                staged.FileName, (int)staged.Direction,
                DocumentName(staged.Direction, isReturn),
                supplierInvoice.Id, supplierInvoice.InternalNumber,
                supplierInvoice.InvoiceNumber, currentAccountTitle, accountCreated,
                supplierInvoice.GrandTotal, supplierInvoice.RequiresManualReview);
        }

        var salesNumber = await documentNumberService.GenerateAsync(
            companyId,
            isReturn ? "SALES_INVOICE_RETURN" : "SALES_INVOICE",
            isReturn ? "SIF" : "SAT",
            cancellationToken);

        var originalSalesInvoiceId = await ResolveOriginalSalesInvoiceAsync(
            companyId, isReturn, item.OriginalInvoiceId, currentAccountId,
            cancellationToken);

        var salesInvoice = new SalesInvoice
        {
            CompanyId = companyId,
            CustomerCurrentAccountId = currentAccountId,
            ProjectId = item.ProjectId,
            InternalNumber = salesNumber,
            OfficialInvoiceNumber = invoice.InvoiceNumber!.Trim(),
            InvoiceDate = issueDate,
            CurrencyCode = invoice.CurrencyCode,
            ExchangeRate = exchangeRate,
            Subtotal = subtotal,
            VatTotal = vatTotal,
            GrandTotal = grandTotal,
            WithholdingAmount = withholding,
            NetReceivableAmount = grandTotal - withholding,
            SourceXmlPath = xmlPath,
            ParseSource = parseSource,
            RequiresManualReview = staged.RequiresManualReview,
            Description = $"E-fatura içe aktarma — {staged.FileName}",
            Status = SalesInvoiceStatus.Draft,
            IsReturn = isReturn,
            OriginalInvoiceId = originalSalesInvoiceId
        };

        var salesLineNumber = 1;

        foreach (var line in invoice.Lines)
        {
            var lineSubtotal = Round(line.LineExtensionAmount);
            var lineVat = Round(line.VatAmount);

            salesInvoice.Items.Add(new SalesInvoiceItem
            {
                LineNumber = salesLineNumber++,
                Description = line.Name,
                Quantity = line.Quantity,
                Unit = line.Unit,
                UnitPrice = line.UnitPrice,
                VatRate = line.VatRate,
                LineSubtotal = lineSubtotal,
                VatAmount = lineVat,
                LineTotal = lineSubtotal + lineVat
            });
        }

        db.SalesInvoices.Add(salesInvoice);
        await db.SaveChangesAsync(cancellationToken);

        return new ImportCommitCreated(
            staged.FileName, (int)staged.Direction,
            DocumentName(staged.Direction, isReturn),
            salesInvoice.Id, salesInvoice.InternalNumber,
            salesInvoice.OfficialInvoiceNumber, currentAccountTitle, accountCreated,
            salesInvoice.GrandTotal, salesInvoice.RequiresManualReview);
    }

    /// <summary>
    /// Belge hangi deftere yazılacak: alış (tedarikçi) tarafı mı, satış
    /// tarafı mı.
    ///
    /// İADE faturasında yön TERSİNE döner ve bunun sebebi mevzuattır:
    /// mal iadesinde faturayı İADE EDEN taraf keser. Bize aldığımız malı
    /// tedarikçiye geri gönderirken faturayı biz keseriz — XML'de satıcı
    /// biz görünürüz ama bu bizim ALIŞ İADEMİZDİR. Tersi de doğru:
    /// müşterimiz mal iade ederken bize fatura keser, XML'de alıcı biz
    /// görünürüz ama bu bizim SATIŞ İADEMİZDİR.
    ///
    /// Bu ayrım yapılmasaydı alış iademiz sisteme satış geliri olarak
    /// girerdi.
    /// </summary>
    private static bool TargetsSupplierLedger(InvoiceDirection direction, bool isReturn) =>
        isReturn
            ? direction == InvoiceDirection.Sales
            : direction == InvoiceDirection.Purchase;

    private static SupplierInvoiceType ResolveInvoiceType(int value) =>
        Enum.IsDefined(typeof(SupplierInvoiceType), value)
            ? (SupplierInvoiceType)value
            : throw new InvalidOperationException("Geçersiz fatura tipi.");

    /// <summary>
    /// Gider faturasında hesap ZORUNLU. Hesapsız kaydedilseydi taslak
    /// onaya geldiğinde fiş üretilemez ve kullanıcı hatayı günler sonra,
    /// faturanın kaynağını unuttuğunda görürdü.
    ///
    /// Kurallar fatura ekranındakinin aynısı: kayıt kabul eden, aktif ve
    /// 6xx/7xx grubunda bir hesap.
    /// </summary>
    private async Task<Guid?> ResolveExpenseAccountAsync(
        Guid companyId,
        SupplierInvoiceType invoiceType,
        Guid? expenseAccountId,
        CancellationToken cancellationToken)
    {
        if (invoiceType != SupplierInvoiceType.Expense)
        {
            if (expenseAccountId is not null)
                throw new InvalidOperationException(
                    "Gider hesabı yalnızca gider faturasında seçilebilir.");

            return null;
        }

        if (expenseAccountId is not Guid accountId)
            throw new InvalidOperationException(
                "Gider faturası için gider hesabı seçilmelidir.");

        var account = await db.AccountingAccounts
            .AsNoTracking()
            .Where(x => x.Id == accountId && x.CompanyId == companyId)
            .Select(x => new { x.Code, x.Name, x.IsActive, x.IsPostingAllowed })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Seçilen gider hesabı bulunamadı.");

        if (!account.IsActive || !account.IsPostingAllowed)
            throw new InvalidOperationException(
                $"{account.Code} {account.Name} hesabına kayıt yapılamaz " +
                "(grup hesabı veya pasif). Alt kırılımlardan birini seçin.");

        if (!account.Code.StartsWith('6') && !account.Code.StartsWith('7'))
            throw new InvalidOperationException(
                $"{account.Code} {account.Name} bir gider/maliyet hesabı değil. " +
                "6xx/7xx grubundan bir hesap seçilmelidir.");

        return accountId;
    }

    private async Task<Guid?> ResolveWarehouseAsync(
        Guid companyId,
        SupplierInvoiceType invoiceType,
        Guid? warehouseId,
        CancellationToken cancellationToken)
    {
        if (warehouseId is not Guid id)
            return null;

        if (invoiceType != SupplierInvoiceType.Stock)
            throw new InvalidOperationException(
                "Depo yalnızca alış (stok) faturasında seçilebilir.");

        var exists = await db.Warehouses.AnyAsync(
            x => x.Id == id && x.CompanyId == companyId && x.IsActive, cancellationToken);

        if (!exists)
            throw new InvalidOperationException(
                "Seçilen depo bulunamadı veya aktif değil.");

        return id;
    }

    /// <summary>
    /// Cariyi bulur veya kullanıcı istediyse XML'deki VKN + unvanla
    /// yenisini açar. Yeni cari Taslak durumda açılır — muhasebe
    /// hesabını ve rolünü sonradan cari kartından tamamlar.
    /// </summary>
    private async Task<(Guid Id, string Title, bool Created)> ResolveCurrentAccountAsync(
        Guid companyId,
        ImportCommitItem item,
        bool toSupplierLedger,
        ParsedParty counterparty,
        CancellationToken cancellationToken)
    {
        if (item.CurrentAccountId is Guid existingId)
        {
            var existing = await db.CurrentAccounts
                .Where(x => x.Id == existingId && x.CompanyId == companyId)
                .Select(x => new { x.Id, x.Title })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("Seçilen cari bulunamadı.");

            return (existing.Id, existing.Title, false);
        }

        if (!item.CreateCurrentAccount)
            throw new InvalidOperationException(
                "Cari seçilmedi. Mevcut bir cari seçin veya yeni cari oluşturmayı işaretleyin.");

        var taxNumber = ParsedInvoice.Normalize(counterparty.TaxNumber);

        if (taxNumber.Length == 0)
            throw new InvalidOperationException(
                "Karşı tarafın vergi numarası XML'de bulunamadı; yeni cari açılamaz.");

        // Önizleme ile onay arasında cari açılmış olabilir — tekrar bak.
        var rematched = await MatchCurrentAccountAsync(
            companyId, taxNumber, cancellationToken);

        if (rematched is not null)
            return (rematched.Id, rematched.Title, false);

        var title = string.IsNullOrWhiteSpace(counterparty.Name)
            ? $"VKN {taxNumber}"
            : counterparty.Name!.Trim();

        // Rol, belgenin YAZILACAĞI deftere göre belirlenir: iade
        // faturasında XML yönü tersine döndüğü için yöne bakılsaydı
        // tedarikçiye "müşteri" rolü açılırdı.
        var prefix = toSupplierLedger ? "TED" : "MUS";
        var code = await GenerateCurrentAccountCodeAsync(
            companyId, $"{prefix}-{taxNumber}", cancellationToken);

        var account = new CurrentAccount
        {
            CompanyId = companyId,
            Code = code,
            Title = title,
            TaxNumber = taxNumber,
            Roles = toSupplierLedger
                ? CurrentAccountRoles.Supplier
                : CurrentAccountRoles.Customer,
            Status = CurrentAccountStatus.Draft
        };

        db.CurrentAccounts.Add(account);
        await db.SaveChangesAsync(cancellationToken);

        return (account.Id, account.Title, true);
    }

    private async Task<string> GenerateCurrentAccountCodeAsync(
        Guid companyId, string preferred, CancellationToken cancellationToken)
    {
        var code = preferred.ToUpperInvariant();
        var suffix = 1;

        while (await db.CurrentAccounts.AnyAsync(
            x => x.CompanyId == companyId && x.Code == code, cancellationToken))
        {
            code = $"{preferred.ToUpperInvariant()}-{++suffix}";
        }

        return code;
    }

    private async Task<ImportPreviewItem?> BuildPreviewAsync(
        Guid companyId,
        string ourTaxNumber,
        string fileName,
        string xml,
        CancellationToken cancellationToken)
    {
        var read = await reader.ReadAsync(xml, cancellationToken);

        if (!read.Success || read.Invoice is null)
        {
            return new ImportPreviewItem(
                fileName, false, (int)InvoiceDirection.Unknown, "Belirlenemedi",
                null, null, null, null, null, null,
                0m, 0m, 0m, 0m, [],
                (int)read.Source, SourceName(read.Source), true, null,
                read.Problems,
                (int)SupplierInvoiceType.Stock, InvoiceTypeName(SupplierInvoiceType.Stock),
                null, null, null, null,
                false, null, null, null,
                string.Empty);
        }

        var invoice = read.Invoice;
        var direction = invoice.ResolveDirection(ourTaxNumber);
        var isReturn = invoice.IsReturnDocument;
        var toSupplierLedger = TargetsSupplierLedger(direction, isReturn);

        var problems = new List<string>(read.Problems);

        if (direction == InvoiceDirection.Unknown)
        {
            problems.Add(
                $"Bu fatura sizin şirketinize ait değil: satıcı VKN " +
                $"{invoice.Supplier.TaxNumber ?? "-"}, alıcı VKN " +
                $"{invoice.Customer.TaxNumber ?? "-"}. İçe aktarılmadı.");
        }

        // Karşı taraf: alışta satıcı, satışta alıcı.
        var counterparty = direction == InvoiceDirection.Sales
            ? invoice.Customer
            : invoice.Supplier;

        var matched = await MatchCurrentAccountAsync(
            companyId, counterparty.TaxNumber, cancellationToken);

        var duplicateId = await FindDuplicateAsync(
            companyId, toSupplierLedger, matched?.Id, invoice.InvoiceNumber, cancellationToken);

        // İade faturasında orijinali numaradan bulmaya çalış; bulunamazsa
        // kullanıcı ekrandan seçer, uydurulmaz.
        var matchedOriginal = isReturn && matched is not null
            ? await MatchOriginalInvoiceAsync(
                companyId, toSupplierLedger, matched.Id,
                invoice.ReferencedInvoiceNumber, cancellationToken)
            : null;

        if (duplicateId is not null)
        {
            problems.Add(
                $"Bu fatura daha önce içe aktarılmış ({invoice.InvoiceNumber}). " +
                "Mükerrer kayıt engellendi.");
        }

        var subtotal = invoice.TaxExclusiveAmount
            ?? invoice.LineExtensionTotal
            ?? invoice.Lines.Sum(x => x.LineExtensionAmount);

        var canImport =
            direction != InvoiceDirection.Unknown &&
            duplicateId is null &&
            UblTrInvoiceValidator.CollectBlockingProblems(invoice).Count == 0;

        var token = canImport
            ? staging.Store(new StagedInvoice(
                fileName, xml, invoice, direction, read.Source,
                read.RequiresManualReview))
            : string.Empty;

        // Tip önerisi yalnızca alış tarafında anlamlı; satış faturasının
        // stok/gider ayrımı yoktur.
        var suggestion = toSupplierLedger
            ? await BuildSuggestionAsync(companyId, invoice, cancellationToken)
            : (Type: SupplierInvoiceType.Stock,
               AccountId: (Guid?)null, Code: (string?)null,
               Name: (string?)null, Reason: (string?)null);

        return new ImportPreviewItem(
            FileName: fileName,
            CanImport: canImport,
            Direction: (int)direction,
            DirectionName: DocumentName(direction, isReturn),
            InvoiceNumber: invoice.InvoiceNumber,
            IssueDate: invoice.IssueDate,
            CounterpartyTaxNumber: counterparty.TaxNumber,
            CounterpartyName: counterparty.Name,
            MatchedCurrentAccountId: matched?.Id,
            MatchedCurrentAccountTitle: matched?.Title,
            Subtotal: Round(subtotal),
            VatTotal: Round(invoice.VatTotal),
            WithholdingAmount: Round(invoice.WithholdingAmount),
            GrandTotal: Round(invoice.PayableAmount ?? 0m),
            Lines: invoice.Lines.Select(x => new ImportPreviewLine(
                x.Name, x.Quantity, x.Unit, x.UnitPrice, x.VatRate,
                Round(x.LineExtensionAmount), Round(x.VatAmount))).ToList(),
            ParseSource: (int)read.Source,
            ParseSourceName: SourceName(read.Source),
            RequiresManualReview: read.RequiresManualReview,
            DuplicateOfId: duplicateId,
            Problems: problems,
            SuggestedInvoiceType: (int)suggestion.Type,
            SuggestedInvoiceTypeName: InvoiceTypeName(suggestion.Type),
            SuggestedExpenseAccountId: suggestion.AccountId,
            SuggestedExpenseAccountCode: suggestion.Code,
            SuggestedExpenseAccountName: suggestion.Name,
            SuggestionReason: suggestion.Reason,
            IsReturn: isReturn,
            ReferencedInvoiceNumber: invoice.ReferencedInvoiceNumber,
            MatchedOriginalInvoiceId: matchedOriginal?.Id,
            MatchedOriginalInvoiceNumber: matchedOriginal?.Number,
            Token: token);
    }

    /// <summary>
    /// Anahtar kelimeden tip ve gider hesabı önerir. Önerilen kod
    /// şirketin hesap planında yoksa (ya da kayıt kabul etmiyorsa) hesap
    /// boş bırakılır ama gider önerisi durur — kullanıcı hesabı kendisi
    /// seçer; yanlış hesaba yazmaktansa seçtirmek daha doğrudur.
    /// </summary>
    private async Task<(SupplierInvoiceType Type, Guid? AccountId, string? Code,
        string? Name, string? Reason)> BuildSuggestionAsync(
        Guid companyId,
        ParsedInvoice invoice,
        CancellationToken cancellationToken)
    {
        var suggestion = EInvoiceExpenseSuggester.Suggest(
            invoice.Lines.Select(x => x.Name));

        if (!suggestion.IsExpense)
            return (SupplierInvoiceType.Stock, null, null, null, null);

        var account = suggestion.AccountCode is null
            ? null
            : await db.AccountingAccounts
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId &&
                            x.Code == suggestion.AccountCode &&
                            x.IsActive && x.IsPostingAllowed)
                .Select(x => new { x.Id, x.Code, x.Name })
                .FirstOrDefaultAsync(cancellationToken);

        return (SupplierInvoiceType.Expense, account?.Id, account?.Code,
            account?.Name, suggestion.Reason);
    }

    /// <summary>
    /// İade faturasının atıf yaptığı orijinal faturayı numaradan bulur.
    /// Bulunamazsa null döner — yanlış faturaya bağlamaktansa kullanıcıya
    /// seçtirmek doğru.
    /// </summary>
    private async Task<(Guid Id, string Number)?> MatchOriginalInvoiceAsync(
        Guid companyId,
        bool toSupplierLedger,
        Guid currentAccountId,
        string? referencedInvoiceNumber,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(referencedInvoiceNumber))
            return null;

        var number = referencedInvoiceNumber.Trim();

        if (toSupplierLedger)
        {
            var supplierInvoice = await db.SupplierInvoices
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId &&
                            x.SupplierCurrentAccountId == currentAccountId &&
                            x.InvoiceNumber == number &&
                            !x.IsReturn &&
                            x.Status == SupplierInvoiceStatus.Approved)
                .Select(x => new { x.Id, Number = x.InvoiceNumber })
                .FirstOrDefaultAsync(cancellationToken);

            return supplierInvoice is null
                ? null
                : (supplierInvoice.Id, supplierInvoice.Number);
        }

        var salesInvoice = await db.SalesInvoices
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.CustomerCurrentAccountId == currentAccountId &&
                        x.OfficialInvoiceNumber == number &&
                        !x.IsReturn &&
                        x.Status == SalesInvoiceStatus.Posted)
            .Select(x => new { x.Id, Number = x.OfficialInvoiceNumber! })
            .FirstOrDefaultAsync(cancellationToken);

        return salesInvoice is null ? null : (salesInvoice.Id, salesInvoice.Number);
    }

    private async Task<CurrentAccountMatch?> MatchCurrentAccountAsync(
        Guid companyId, string? taxNumber, CancellationToken cancellationToken)
    {
        var normalized = ParsedInvoice.Normalize(taxNumber);

        if (normalized.Length == 0)
            return null;

        // VKN'ler kayıtlarda boşluklu/tireli olabilir; karşılaştırma
        // yalnızca rakamlar üzerinden yapılmalı.
        var candidates = await db.CurrentAccounts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.TaxNumber != null)
            .Select(x => new CurrentAccountMatch(x.Id, x.Title, x.TaxNumber!))
            .ToListAsync(cancellationToken);

        return candidates.FirstOrDefault(
            x => ParsedInvoice.Normalize(x.TaxNumber) == normalized);
    }

    /// <summary>
    /// Aynı fatura daha önce girilmiş mi. Alışta tedarikçi + fatura no,
    /// satışta müşteri + resmi fatura no.
    /// </summary>
    private async Task<Guid?> FindDuplicateAsync(
        Guid companyId,
        bool toSupplierLedger,
        Guid? currentAccountId,
        string? invoiceNumber,
        CancellationToken cancellationToken)
    {
        if (currentAccountId is null || string.IsNullOrWhiteSpace(invoiceNumber))
            return null;

        if (toSupplierLedger)
        {
            return await db.SupplierInvoices
                .Where(x => x.CompanyId == companyId &&
                            x.SupplierCurrentAccountId == currentAccountId &&
                            x.InvoiceNumber == invoiceNumber)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await db.SalesInvoices
            .Where(x => x.CompanyId == companyId &&
                        x.CustomerCurrentAccountId == currentAccountId &&
                        x.OfficialInvoiceNumber == invoiceNumber)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// İade faturasının bağlanacağı orijinal alış faturası. Seçim
    /// doğrulanır: aynı şirket, aynı cari ve ONAYLANMIŞ olmalı —
    /// onaylanmamış faturanın tersine çevrilecek kaydı yoktur.
    /// </summary>
    private async Task<Guid?> ResolveOriginalSupplierInvoiceAsync(
        Guid companyId,
        bool isReturn,
        Guid? originalInvoiceId,
        Guid currentAccountId,
        CancellationToken cancellationToken)
    {
        if (!isReturn || originalInvoiceId is not Guid id)
            return null;

        var original = await db.SupplierInvoices
            .AsNoTracking()
            .Where(x => x.Id == id && x.CompanyId == companyId)
            .Select(x => new { x.SupplierCurrentAccountId, x.Status, x.IsReturn })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Seçilen orijinal fatura bulunamadı.");

        if (original.IsReturn)
            throw new InvalidOperationException("İade faturası orijinal olarak seçilemez.");

        if (original.SupplierCurrentAccountId != currentAccountId)
            throw new InvalidOperationException(
                "Seçilen orijinal fatura bu cariye ait değil.");

        if (original.Status != SupplierInvoiceStatus.Approved)
            throw new InvalidOperationException(
                "Orijinal fatura onaylanmamış; iade bağlanamaz.");

        return id;
    }

    private async Task<Guid?> ResolveOriginalSalesInvoiceAsync(
        Guid companyId,
        bool isReturn,
        Guid? originalInvoiceId,
        Guid currentAccountId,
        CancellationToken cancellationToken)
    {
        if (!isReturn || originalInvoiceId is not Guid id)
            return null;

        var original = await db.SalesInvoices
            .AsNoTracking()
            .Where(x => x.Id == id && x.CompanyId == companyId)
            .Select(x => new { x.CustomerCurrentAccountId, x.Status, x.IsReturn })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Seçilen orijinal fatura bulunamadı.");

        if (original.IsReturn)
            throw new InvalidOperationException("İade faturası orijinal olarak seçilemez.");

        if (original.CustomerCurrentAccountId != currentAccountId)
            throw new InvalidOperationException(
                "Seçilen orijinal fatura bu cariye ait değil.");

        if (original.Status != SalesInvoiceStatus.Posted)
            throw new InvalidOperationException(
                "Orijinal fatura kesinleşmemiş; iade bağlanamaz.");

        return id;
    }

    /// <summary>
    /// ZIP dosyalarını açar, XML'leri düz listeye çevirir. ZIP içindeki
    /// XML olmayan dosyalar sessizce atlanır.
    /// </summary>
    private static async Task<List<(string FileName, string Xml)>> ExpandAsync(
        IReadOnlyList<(string FileName, Stream Content)> files,
        CancellationToken cancellationToken)
    {
        var result = new List<(string, string)>();

        foreach (var (fileName, content) in files)
        {
            if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var archive = new ZipArchive(content, ZipArchiveMode.Read);

                foreach (var entry in archive.Entries)
                {
                    if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                        continue;

                    using var entryStream = entry.Open();
                    using var entryReader = new StreamReader(entryStream, Encoding.UTF8);

                    result.Add((
                        $"{fileName}/{entry.FullName}",
                        await entryReader.ReadToEndAsync(cancellationToken)));
                }

                continue;
            }

            using var streamReader = new StreamReader(content, Encoding.UTF8);
            result.Add((fileName, await streamReader.ReadToEndAsync(cancellationToken)));
        }

        return result;
    }

    private static string DirectionName(InvoiceDirection direction) => direction switch
    {
        InvoiceDirection.Purchase => "Gelen (Alış)",
        InvoiceDirection.Sales => "Giden (Satış)",
        _ => "Belirlenemedi"
    };

    /// <summary>
    /// Ekranda görünen belge adı. İade faturasında yön adı tek başına
    /// yanıltıcı olur: alış iademizi biz kestiğimiz için XML "giden"
    /// görünür ama belge bir ALIŞ iadesidir.
    /// </summary>
    private static string DocumentName(InvoiceDirection direction, bool isReturn)
    {
        if (!isReturn)
            return DirectionName(direction);

        return direction switch
        {
            InvoiceDirection.Sales => "Alış iadesi (giden)",
            InvoiceDirection.Purchase => "Satış iadesi (gelen)",
            _ => "Belirlenemedi"
        };
    }

    private static string InvoiceTypeName(SupplierInvoiceType type) =>
        type == SupplierInvoiceType.Expense ? "Gider" : "Alış (Stok)";

    private static string SourceName(InvoiceParseSource source) =>
        source == InvoiceParseSource.Ai ? "AI ile okundu" : "Standart";

    private static decimal Round(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private sealed record CurrentAccountMatch(Guid Id, string Title, string TaxNumber);
}
