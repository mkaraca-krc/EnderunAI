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
    /// <summary>Alış faturasında zorunlu — fişin masraf merkezi.</summary>
    Guid? ProjectId);

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
    IEInvoiceArchive archive) : IEInvoiceImportService
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

        var counterparty = staged.Direction == InvoiceDirection.Sales
            ? invoice.Customer
            : invoice.Supplier;

        var (currentAccountId, currentAccountTitle, accountCreated) =
            await ResolveCurrentAccountAsync(
                companyId, item, staged.Direction, counterparty, cancellationToken);

        // Mükerrer kontrolü kaydetmeden hemen önce tekrar yapılır:
        // önizleme ile onay arasında başkası aynı faturayı girmiş olabilir.
        var duplicate = await FindDuplicateAsync(
            companyId, staged.Direction, currentAccountId,
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

        if (staged.Direction == InvoiceDirection.Purchase)
        {
            if (item.ProjectId is not Guid projectId)
                throw new InvalidOperationException(
                    "Alış faturası için proje seçimi zorunludur.");

            var projectExists = await db.Projects.AnyAsync(
                x => x.Id == projectId && x.CompanyId == companyId, cancellationToken);

            if (!projectExists)
                throw new InvalidOperationException("Seçilen proje bulunamadı.");

            var internalNumber = await documentNumberService.GenerateAsync(
                companyId, "SUPPLIER_INVOICE", "SFT", cancellationToken);

            var supplierInvoice = new SupplierInvoice
            {
                CompanyId = companyId,
                SupplierCurrentAccountId = currentAccountId,
                ProjectId = projectId,
                InternalNumber = internalNumber,
                InvoiceNumber = invoice.InvoiceNumber!.Trim(),
                InvoiceDate = issueDate,
                CurrencyCode = invoice.CurrencyCode,
                ExchangeRate = 1m,
                Subtotal = subtotal,
                VatTotal = vatTotal,
                GrandTotal = grandTotal,
                WithholdingAmount = withholding,
                SourceXmlPath = xmlPath,
                ParseSource = parseSource,
                RequiresManualReview = staged.RequiresManualReview,
                Description = $"E-fatura içe aktarma — {staged.FileName}",
                Status = SupplierInvoiceStatus.Draft
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
                    LineTotal = lineSubtotal + lineVat
                });
            }

            db.SupplierInvoices.Add(supplierInvoice);
            await db.SaveChangesAsync(cancellationToken);

            return new ImportCommitCreated(
                staged.FileName, (int)staged.Direction, DirectionName(staged.Direction),
                supplierInvoice.Id, supplierInvoice.InternalNumber,
                supplierInvoice.InvoiceNumber, currentAccountTitle, accountCreated,
                supplierInvoice.GrandTotal, supplierInvoice.RequiresManualReview);
        }

        var salesNumber = await documentNumberService.GenerateAsync(
            companyId, "SALES_INVOICE", "SAT", cancellationToken);

        var salesInvoice = new SalesInvoice
        {
            CompanyId = companyId,
            CustomerCurrentAccountId = currentAccountId,
            ProjectId = item.ProjectId,
            InternalNumber = salesNumber,
            OfficialInvoiceNumber = invoice.InvoiceNumber!.Trim(),
            InvoiceDate = issueDate,
            CurrencyCode = invoice.CurrencyCode,
            ExchangeRate = 1m,
            Subtotal = subtotal,
            VatTotal = vatTotal,
            GrandTotal = grandTotal,
            WithholdingAmount = withholding,
            NetReceivableAmount = grandTotal - withholding,
            SourceXmlPath = xmlPath,
            ParseSource = parseSource,
            RequiresManualReview = staged.RequiresManualReview,
            Description = $"E-fatura içe aktarma — {staged.FileName}",
            Status = SalesInvoiceStatus.Draft
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
            staged.FileName, (int)staged.Direction, DirectionName(staged.Direction),
            salesInvoice.Id, salesInvoice.InternalNumber,
            salesInvoice.OfficialInvoiceNumber, currentAccountTitle, accountCreated,
            salesInvoice.GrandTotal, salesInvoice.RequiresManualReview);
    }

    /// <summary>
    /// Cariyi bulur veya kullanıcı istediyse XML'deki VKN + unvanla
    /// yenisini açar. Yeni cari Taslak durumda açılır — muhasebe
    /// hesabını ve rolünü sonradan cari kartından tamamlar.
    /// </summary>
    private async Task<(Guid Id, string Title, bool Created)> ResolveCurrentAccountAsync(
        Guid companyId,
        ImportCommitItem item,
        InvoiceDirection direction,
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

        var prefix = direction == InvoiceDirection.Sales ? "MUS" : "TED";
        var code = await GenerateCurrentAccountCodeAsync(
            companyId, $"{prefix}-{taxNumber}", cancellationToken);

        var account = new CurrentAccount
        {
            CompanyId = companyId,
            Code = code,
            Title = title,
            TaxNumber = taxNumber,
            Roles = direction == InvoiceDirection.Sales
                ? CurrentAccountRoles.Customer
                : CurrentAccountRoles.Supplier,
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
                read.Problems, string.Empty);
        }

        var invoice = read.Invoice;
        var direction = invoice.ResolveDirection(ourTaxNumber);

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
            companyId, direction, matched?.Id, invoice.InvoiceNumber, cancellationToken);

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

        return new ImportPreviewItem(
            FileName: fileName,
            CanImport: canImport,
            Direction: (int)direction,
            DirectionName: DirectionName(direction),
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
            Token: token);
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
        InvoiceDirection direction,
        Guid? currentAccountId,
        string? invoiceNumber,
        CancellationToken cancellationToken)
    {
        if (currentAccountId is null || string.IsNullOrWhiteSpace(invoiceNumber))
            return null;

        if (direction == InvoiceDirection.Purchase)
        {
            return await db.SupplierInvoices
                .Where(x => x.CompanyId == companyId &&
                            x.SupplierCurrentAccountId == currentAccountId &&
                            x.InvoiceNumber == invoiceNumber)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (direction == InvoiceDirection.Sales)
        {
            return await db.SalesInvoices
                .Where(x => x.CompanyId == companyId &&
                            x.CustomerCurrentAccountId == currentAccountId &&
                            x.OfficialInvoiceNumber == invoiceNumber)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return null;
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

    private static string SourceName(InvoiceParseSource source) =>
        source == InvoiceParseSource.Ai ? "AI ile okundu" : "Standart";

    private static decimal Round(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private sealed record CurrentAccountMatch(Guid Id, string Title, string TaxNumber);
}
