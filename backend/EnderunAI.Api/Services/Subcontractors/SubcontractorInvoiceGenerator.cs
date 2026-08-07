using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Data;
using EnderunAI.Api.Formatting;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Accounting;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Subcontractors;

/// <summary>
/// Otomatik fatura üretiminin sonucu.
/// </summary>
/// <param name="SupplierInvoiceId">Üretilen fatura; üretilmediyse null.</param>
/// <param name="InvoiceNumber">Fatura numarası.</param>
/// <param name="Amount">Faturaya giren tutar (KDV hariç).</param>
/// <param name="Skipped">Üretilmediyse nedeni; üretildiyse null.</param>
public sealed record SubcontractorInvoiceResult(
    Guid? SupplierInvoiceId,
    string? InvoiceNumber,
    decimal Amount,
    string? Skipped);

/// <summary>
/// Onaylanan taşeron hakedişinden TEDARİKÇİ FATURASI üretir.
///
/// Hakediş onaylandığında ne kadar borçlandığımız kesinleşir; o tutarın
/// deftere girmesi için bir alış faturası gerekir. Elle girilmesine
/// bırakılırsa hakediş ile muhasebe ayrışır: hakedişte onaylı, defterde
/// yok.
///
/// SADECE FATURALI KISIM: elden ödenecek tutar bu faturaya GİRMEZ.
/// Elden kısım ayrı tabloda durur, muhasebeye hiç uğramaz ve yalnızca
/// <c>extra_payment.view</c> ile okunur. Faturalı/elden ayrımını
/// çağıran verir; bu servis kendi başına elden tutara hiç erişmez.
///
/// KDV TEVKİFATI sözleşmedeki pay/paydadan gelir (yapım işlerinde
/// tipik 4/10). Her faturada elle girilseydi aynı taşeronun iki
/// faturası farklı oranla muhasebeleşir ve KDV beyanı tutmazdı.
/// Tevkifat hesabı fatura servisinin kendi akışında yürür; burada
/// yalnızca oran taşınır.
/// </summary>
public sealed class SubcontractorInvoiceGenerator(
    AppDbContext db,
    ISupplierInvoiceService supplierInvoices)
{
    /// <summary>
    /// Hakedişin faturalı kısmı için tedarikçi faturası üretir.
    /// </summary>
    /// <param name="payment">Onaylanmış taşeron hakedişi.</param>
    /// <param name="invoicedAmount">Faturalanacak tutar (KDV hariç).
    /// Karma ödemede net tutarın yalnızca faturalı kısmı.</param>
    /// <param name="vatRate">KDV oranı.</param>
    /// <param name="expenseAccountId">740 alt yüklenici gider hesabı.</param>
    public async Task<SubcontractorInvoiceResult> GenerateAsync(
        SubcontractorProgressPayment payment,
        decimal invoicedAmount,
        decimal vatRate,
        Guid? expenseAccountId,
        CancellationToken cancellationToken)
    {
        if (payment.Status != SubcontractorProgressPaymentStatus.Approved)
        {
            return new SubcontractorInvoiceResult(
                null, null, 0m,
                "Yalnızca onaylanmış hakedişten fatura üretilir.");
        }

        if (invoicedAmount <= 0m)
        {
            return new SubcontractorInvoiceResult(
                null, null, 0m,
                "Faturalanacak tutar yok; hakediş tamamen elden ödeniyor " +
                "olabilir.");
        }

        // Aynı hakedişten ikinci fatura mükerrer gider yazar.
        if (payment.SupplierInvoiceId is Guid existingId)
        {
            var existingNumber = await db.SupplierInvoices
                .AsNoTracking()
                .Where(x => x.Id == existingId)
                .Select(x => x.InvoiceNumber)
                .FirstOrDefaultAsync(cancellationToken);

            return new SubcontractorInvoiceResult(
                existingId, existingNumber, 0m,
                $"Bu hakedişten zaten fatura üretilmiş ({existingNumber}).");
        }

        var contract = await db.SubcontractorContracts
            .AsNoTracking()
            .Include(x => x.CurrentAccount)
            .SingleOrDefaultAsync(
                x => x.Id == payment.SubcontractorContractId, cancellationToken);

        if (contract is null)
        {
            return new SubcontractorInvoiceResult(
                null, null, 0m, "Sözleşme bulunamadı.");
        }

        if (expenseAccountId is null)
        {
            return new SubcontractorInvoiceResult(
                null, null, 0m,
                "Alt yüklenici gider hesabı (740) belirlenemedi; " +
                "hesap planında tanımlayın.");
        }

        var description =
            $"Taşeron hakedişi {payment.Year}/{payment.Month:D2} — " +
            $"{contract.ContractNumber} ({contract.CurrentAccount.Title})";

        var request = new CreateSupplierInvoiceRequest(
            CompanyId: contract.CompanyId,
            SupplierCurrentAccountId: contract.CurrentAccountId,
            ProjectId: contract.ProjectId,
            PurchaseOrderId: null,
            GoodsReceiptId: null,
            // Taşeron kendi faturasını sonradan getirir; o numara
            // girilene kadar hakediş referansı kullanılır ki fatura
            // numarasız kalmasın ve hangi hakedişten geldiği görünsün.
            InvoiceNumber: $"THK-{payment.Year}{payment.Month:D2}-{contract.ContractNumber}",
            InvoiceDate: payment.ApprovedAtUtc?.Date ?? DateTime.UtcNow.Date,
            DueDate: null,
            CurrencyCode: payment.CurrencyCode,
            ExchangeRate: 1m,
            Description: description,
            Items:
            [
                new SupplierInvoiceItemRequest(
                    Description: description,
                    Quantity: 1m,
                    Unit: "AD",
                    UnitPrice: decimal.Round(invoicedAmount, 2),
                    VatRate: vatRate,
                    PurchaseOrderItemId: null,
                    InventoryItemId: null,
                    WarehouseId: null,
                    // Taşeron işçiliği stoğa girmez, doğrudan gider:
                    // 740 alt yüklenici giderleri.
                    ExpenseAccountId: expenseAccountId,
                    CostCenterCode: null,
                    ProjectBoqItemId: null)
            ],
            // Gider faturası: stok hareketi üretmez.
            InvoiceType: (int)SupplierInvoiceType.Expense,
            WarehouseId: null,
            CostCenterCode: null);

        var created = await supplierInvoices.CreateAsync(request, cancellationToken);

        // Kaynak bağı hakediş tarafında: mükerrer kontrolü buna bakıyor.
        var tracked = await db.SubcontractorProgressPayments
            .SingleAsync(x => x.Id == payment.Id, cancellationToken);

        tracked.SupplierInvoiceId = created.Id;
        tracked.InvoicedAmount = decimal.Round(invoicedAmount, 2);

        await db.SaveChangesAsync(cancellationToken);

        var invoiceNumber = await db.SupplierInvoices
            .AsNoTracking()
            .Where(x => x.Id == created.Id)
            .Select(x => x.InvoiceNumber)
            .SingleAsync(cancellationToken);

        return new SubcontractorInvoiceResult(
            created.Id,
            invoiceNumber,
            decimal.Round(invoicedAmount, 2),
            null);
    }

    /// <summary>
    /// 740 Alt yüklenici giderleri hesabını çözer.
    /// </summary>
    public async Task<Guid?> ResolveExpenseAccountAsync(
        Guid companyId, CancellationToken cancellationToken)
    {
        string[] candidates = ["740.03", "740.01", "740", "770"];

        foreach (var code in candidates)
        {
            var id = await db.AccountingAccounts
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId &&
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

    /// <summary>
    /// Sözleşmedeki tevkifat oranının okunabilir hâli — ekranda
    /// gösterilir ki hangi oranla muhasebeleştiği görünsün.
    /// </summary>
    public static string? DescribeWithholding(SubcontractorContract contract)
    {
        if (contract.WithholdingNumerator <= 0 ||
            contract.WithholdingDenominator <= 0)
        {
            return null;
        }

        return
            $"KDV tevkifatı {contract.WithholdingNumerator}/" +
            $"{contract.WithholdingDenominator}";
    }

}
