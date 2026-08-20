using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Accounting;
using EnderunAI.Api.Services.Inventory;

namespace EnderunAI.Api.Services.Retail;

/// <summary>
/// FATURASIZ PERAKENDE SATIŞIN MUHASEBE FİŞİ.
///
/// İki durum faturasız kalıyor ve ikisinde de mal depodan çıkıyor:
///
/// 1) İSİMSİZ NAKİT SATIŞ — cari kartı yok, dolayısıyla satış faturası
///    kurulamıyor. S5 öncesi bu satışta HİÇ kayıt oluşmuyordu: mal
///    çıkıyor, gelir de maliyet de yazılmıyordu. Ölçüldü, kod okundu,
///    doğrulandı.
///
/// 2) TAMAMI ELDEN SATIŞ — kayıtlı tutar sıfır. Resmi gelir yok ama
///    MAL YİNE DE ÇIKTI; maliyet yazılmazsa 150/153 kapanmaz ve stok
///    muhasebesi kalıcı olarak sapardı.
///
/// KULLANICI KARARI (isimsiz satış): gelir ve maliyet kayda girer.
///
/// GELİR TARAFI 120 ÜZERİNDEN GEÇİYOR, doğrudan kasaya değil: tahsilat
/// zaten kendi fişini kesiyor (borç 100/102, alacak 120) ve o fiş
/// koşulsuz çalışıyor. Burada da kasa borçlandırılsaydı aynı para iki
/// kez kasaya girerdi. 120 aynı anda açılıp kapandığı için net etki
/// kullanıcının seçtiğiyle birebir aynı: borç kasa, alacak 600 + 391.
/// </summary>
public interface IRetailSaleVoucherPoster
{
    /// <summary>
    /// Faturasız satışın fişini keser. Yazılacak bir şey yoksa
    /// (ne kayıtlı gelir ne maliyet) null döner.
    /// </summary>
    Task<Guid?> PostAsync(
        RetailSale sale,
        IReadOnlyList<StockSaleCost> costs,
        CancellationToken cancellationToken);
}

public sealed class RetailSaleVoucherPoster(
    IAccountingIntegrationService integration,
    IAccountingVoucherService vouchers,
    IStockOutflowLineBuilder saleCostLines) : IRetailSaleVoucherPoster
{
    public async Task<Guid?> PostAsync(
        RetailSale sale,
        IReadOnlyList<StockSaleCost> costs,
        CancellationToken cancellationToken)
    {
        var context = new SaleCostLineContext(
            Reference: sale.DocumentNumber,
            DocumentDate: sale.SaleDate,
            CurrencyCode: "TRY",
            ExchangeRate: 1m,
            // Perakende satış bir projeye ait değil; proje etiketi
            // konsaydı satılan mal proje maliyeti gibi görünürdü.
            ProjectId: null,
            CostCenterCode: null);

        var lines = new List<AccountingVoucherLineRequest>(
            await saleCostLines.BuildSaleCostAsync(
                sale.CompanyId, costs, context, cancellationToken));

        var recorded = decimal.Round(sale.RecordedAmount, 2);

        if (recorded > 0m)
        {
            var settings = await integration.GetOrCreateFinanceSettingsAsync(
                sale.CompanyId, cancellationToken);

            if (settings.SalesAccountId is null)
            {
                throw new InvalidOperationException(
                    "Yurtiçi satışlar hesabı yapılandırılmamış. Şirket Ayarları → "
                    + "Finans Ayarları'ndan seçin.");
            }

            if (settings.ReceivablesAccountId is null)
            {
                throw new InvalidOperationException(
                    "Alıcılar (120) hesabı yapılandırılmamış. Şirket Ayarları → "
                    + "Finans Ayarları'ndan seçin.");
            }

            // Kayıtlı tutarın içindeki KDV, fişin KDV oranlarından
            // ORANTIYLA çıkarılıyor: elden kısım kayıtlı tutarı
            // düşürdüğünde KDV de aynı oranda düşmeli, yoksa beyan
            // edilen KDV tahsil edilenden büyük kalırdı.
            var ratio = sale.GrandTotal == 0m ? 0m : recorded / sale.GrandTotal;
            var vat = decimal.Round(sale.VatTotal * ratio, 2);
            var net = decimal.Round(recorded - vat, 2);

            if (net <= 0m)
            {
                throw new InvalidOperationException(
                    "Perakende satışta KDV'siz matrah sıfır ya da negatif çıktı; "
                    + "fiş kesilemez.");
            }

            var customerLabel = string.IsNullOrWhiteSpace(sale.WalkInCustomerName)
                ? "isimsiz müşteri"
                : sale.WalkInCustomerName!.Trim();

            lines.Add(new AccountingVoucherLineRequest(
                AccountingAccountId: settings.ReceivablesAccountId.Value,
                Description: $"Perakende satış alacağı — {customerLabel}",
                DebitAmount: recorded,
                CreditAmount: 0m,
                CurrencyCode: "TRY",
                ExchangeRate: 1m,
                CurrentAccountId: null,
                ProjectId: null,
                CostCenterCode: null,
                DocumentNumber: sale.DocumentNumber,
                DocumentDate: sale.SaleDate,
                DueDate: null));

            lines.Add(new AccountingVoucherLineRequest(
                AccountingAccountId: settings.SalesAccountId.Value,
                Description: $"Perakende satış geliri — {sale.DocumentNumber}",
                DebitAmount: 0m,
                CreditAmount: net,
                CurrencyCode: "TRY",
                ExchangeRate: 1m,
                CurrentAccountId: null,
                ProjectId: null,
                CostCenterCode: null,
                DocumentNumber: sale.DocumentNumber,
                DocumentDate: sale.SaleDate,
                DueDate: null));

            if (vat > 0m)
            {
                if (settings.VatOutAccountId is null)
                {
                    throw new InvalidOperationException(
                        "Hesaplanan KDV hesabı yapılandırılmamış. Şirket Ayarları → "
                        + "Finans Ayarları'ndan seçin.");
                }

                lines.Add(new AccountingVoucherLineRequest(
                    AccountingAccountId: settings.VatOutAccountId.Value,
                    Description: "Hesaplanan KDV",
                    DebitAmount: 0m,
                    CreditAmount: vat,
                    CurrencyCode: "TRY",
                    ExchangeRate: 1m,
                    CurrentAccountId: null,
                    ProjectId: null,
                    CostCenterCode: null,
                    DocumentNumber: sale.DocumentNumber,
                    DocumentDate: sale.SaleDate,
                    DueDate: null));
            }
        }

        if (lines.Count == 0) return null;

        var created = await vouchers.CreateAsync(
            new CreateAccountingVoucherRequest(
                CompanyId: sale.CompanyId,
                VoucherType: (int)AccountingVoucherType.Journal,
                VoucherDate: sale.SaleDate,
                CurrencyCode: "TRY",
                ExchangeRate: 1m,
                Description: $"Perakende satış {sale.DocumentNumber}",
                ReferenceNumber: sale.DocumentNumber,
                SourceModule: "RetailSale",
                SourceEntityId: sale.Id,
                Lines: lines),
            cancellationToken);

        await vouchers.PostAsync(created.Id, cancellationToken);

        return created.Id;
    }
}
