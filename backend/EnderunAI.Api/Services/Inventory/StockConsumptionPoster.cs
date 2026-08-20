using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Accounting;

namespace EnderunAI.Api.Services.Inventory;

/// <summary>
/// DEPODAN ÇIKAN AMA SATILMAYAN MALIN MUHASEBE FİŞİ.
///
/// S6c öncesinde depo çıkışı ve sayım düzeltmesi HİÇ fiş kesmiyordu
/// (ölçüldü: 740 hesabında yalnız 1 satır vardı, o da tedarikçi
/// faturasından geliyordu; hiçbir proje maliyet kaydı stok
/// hareketinden doğmamıştı). Taahhüt işinde malzemenin çoğu satılmaz,
/// projeye gider — yani bu, en sık kullanılan yol ve açık kaldığı
/// sürece stok ile muhasebe İLK çıkışta ayrışırdı.
///
/// Fiş, çıkışla AYNI transaction içinde kesiliyor: ayrı olsaydı stok
/// düşüp fiş kesilemediğinde mal muhasebesiz giderdi.
/// </summary>
public interface IStockConsumptionPoster
{
    /// <summary>
    /// Depo çıkışı: proje varsa 740, yoksa 770 borçlanır; alacak
    /// kartın kategorisine göre 150 / 153.
    /// </summary>
    Task<Guid> PostIssueAsync(
        Guid companyId,
        StockSaleCost cost,
        Guid? projectId,
        string? projectCode,
        string reference,
        DateTime movementDate,
        Guid movementId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sayım farkı: noksanda 689.02 borç / stok alacak, fazlada stok
    /// borç / 649.03 alacak.
    /// </summary>
    Task<Guid> PostAdjustmentAsync(
        Guid companyId,
        StockSaleCost cost,
        bool surplus,
        Guid? projectId,
        string? projectCode,
        string reference,
        DateTime movementDate,
        Guid movementId,
        CancellationToken cancellationToken);
}

public sealed class StockConsumptionPoster(
    IStockOutflowLineBuilder lines,
    IAccountingVoucherService vouchers) : IStockConsumptionPoster
{
    public Task<Guid> PostIssueAsync(
        Guid companyId,
        StockSaleCost cost,
        Guid? projectId,
        string? projectCode,
        string reference,
        DateTime movementDate,
        Guid movementId,
        CancellationToken cancellationToken)
    {
        var context = new SaleCostLineContext(
            Reference: reference,
            DocumentDate: movementDate,
            CurrencyCode: "TRY",
            ExchangeRate: 1m,

            // PROJE ETİKETİ BURADA TAŞINIYOR — girişin tersine.
            // Mal kabulde proje yazılmıyordu çünkü depoya giren mal
            // henüz bir projenin maliyeti değil, bilanço kalemiydi.
            // Çıkışta maliyet DOĞUYOR ve hangi projede doğduğu tam da
            // bu satırın anlattığı şey.
            ProjectId: projectId,
            CostCenterCode: projectCode);

        return PostAsync(
            companyId,
            lines.BuildConsumptionAsync(
                companyId, [cost], context, projectId is not null, cancellationToken),
            projectId is not null
                ? $"Depo çıkışı {reference} — proje sarfiyatı"
                : $"Depo çıkışı {reference} — merkez sarfiyatı",
            reference, movementDate, movementId, cancellationToken);
    }

    public Task<Guid> PostAdjustmentAsync(
        Guid companyId,
        StockSaleCost cost,
        bool surplus,
        Guid? projectId,
        string? projectCode,
        string reference,
        DateTime movementDate,
        Guid movementId,
        CancellationToken cancellationToken)
    {
        var context = new SaleCostLineContext(
            Reference: reference,
            DocumentDate: movementDate,
            CurrencyCode: "TRY",
            ExchangeRate: 1m,
            ProjectId: projectId,
            CostCenterCode: projectCode);

        return PostAsync(
            companyId,
            lines.BuildVarianceAsync(companyId, [cost], context, surplus, cancellationToken),
            surplus
                ? $"Sayım fazlası {reference}"
                : $"Sayım noksanı {reference}",
            reference, movementDate, movementId, cancellationToken);
    }

    private async Task<Guid> PostAsync(
        Guid companyId,
        Task<IReadOnlyList<AccountingVoucherLineRequest>> pending,
        string description,
        string reference,
        DateTime movementDate,
        Guid movementId,
        CancellationToken cancellationToken)
    {
        var voucherLines = await pending;

        if (voucherLines.Count == 0)
        {
            throw new InvalidOperationException(
                "Hareketin maliyeti sıfır; muhasebe fişi oluşturulamaz. "
                + "Stok kartında ağırlıklı ortalama maliyet oluşmamış olabilir "
                + "(malzeme hiç faturalı girmemişse maliyeti bilinmiyordur).");
        }

        var created = await vouchers.CreateAsync(
            new CreateAccountingVoucherRequest(
                CompanyId: companyId,
                VoucherType: (int)AccountingVoucherType.Journal,
                VoucherDate: movementDate,
                CurrencyCode: "TRY",
                ExchangeRate: 1m,
                Description: description,
                ReferenceNumber: reference,
                SourceModule: "StockMovement",
                SourceEntityId: movementId,
                Lines: voucherLines),
            cancellationToken);

        await vouchers.PostAsync(created.Id, cancellationToken);

        return created.Id;
    }
}
