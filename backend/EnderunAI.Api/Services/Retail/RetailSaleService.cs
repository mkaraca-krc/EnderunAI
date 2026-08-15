using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.Accounting;
using EnderunAI.Api.Services.DocumentNumbers;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Retail;

/// <summary>
/// PERAKENDE SATIŞ — hızlı giriş noktası ve onay kapısı.
///
/// Bu servis kendi defterini tutmuyor. Onaydan geçen satış mevcut
/// altyapıya akıyor:
///   gelir  -> SalesInvoice (+ muhasebe fişi, AccountingIntegration)
///   stok   -> StockMovement.Issue (mevcut çıkış deseniyle aynı)
///   tahsilat -> CashTransaction (SourceModule = "RETAIL_SALE")
///
/// Ciro/tahsilat/vade sorularının cevabı HER ZAMAN o kaynaklardan
/// toplanır; retail_sales tablosu ikinci bir toplama kaynağı değildir.
/// </summary>
public interface IRetailSaleService
{
    Task<RetailSale> CreateAsync(RetailSaleInput input, CancellationToken cancellationToken);
    Task<RetailSale> SubmitAsync(Guid id, CancellationToken cancellationToken);
    Task<RetailSale> ApproveAsync(Guid id, CancellationToken cancellationToken);
    Task<RetailSale> RejectAsync(Guid id, string reason, CancellationToken cancellationToken);

    /// <summary>
    /// Bir kalemin merkez depodaki SATILABİLİR adedi.
    /// Fiili stok eksi sanal rezerv.
    /// </summary>
    Task<decimal> GetAvailableAsync(
        Guid warehouseId, Guid inventoryItemId, CancellationToken cancellationToken);
}

public sealed record RetailSaleLineInput(
    Guid InventoryItemId,
    decimal Quantity,
    decimal DiscountRate);

public sealed record RetailSaleInput(
    Guid CompanyId,
    Guid WarehouseId,
    DateTime SaleDate,
    Guid? CustomerCurrentAccountId,
    string? WalkInCustomerName,
    RetailPaymentMethod PaymentMethod,
    DateTime? DueDate,
    decimal OverallDiscountRate,
    decimal CashAmount,
    Guid? CashAccountId,
    IReadOnlyList<RetailSaleLineInput> Items);

public sealed class RetailSaleService(
    AppDbContext db,
    ICurrentUserService currentUser,
    IDocumentNumberService documentNumbers,
    IAccountingIntegrationService accounting,
    IUserAuthorizationService authorization) : IRetailSaleService
{
    /// <summary>
    /// SANAL REZERV: fiili stoktan, henüz sonuçlanmamış fişlerdeki
    /// miktar düşülür.
    ///
    /// NEDEN ŞEMAYA REZERV KOVASI EKLENMEDİ: WarehouseStock üzerinde
    /// böyle bir alan vardı ve bilinçli kaldırıldı — Enderun stok
    /// bloke etmiyor, ihtiyaca göre tedarik ediyor ve alan yıllarca
    /// sıfır kaldı. O kararı geri almak, "kullanılabilir = miktar"
    /// varsayımını taşıyan BÜTÜN stok okuyucularını etkilerdi.
    /// Rezerv bu yüzden yalnız perakendeye ait ve türetilmiş bir
    /// büyüklük: taslak ve onay bekleyen fişlerden hesaplanıyor,
    /// fişin sonucu ne olursa olsun kendiliğinden çözülüyor.
    /// </summary>
    private static readonly RetailSaleStatus[] HoldingStatuses =
        [RetailSaleStatus.Draft, RetailSaleStatus.PendingApproval];

    public async Task<decimal> GetAvailableAsync(
        Guid warehouseId, Guid inventoryItemId, CancellationToken cancellationToken)
    {
        var onHand = await db.WarehouseStocks
            .AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId && x.InventoryItemId == inventoryItemId)
            .Select(x => (decimal?)x.Quantity)
            .SingleOrDefaultAsync(cancellationToken) ?? 0m;

        var reserved = await db.RetailSaleItems
            .AsNoTracking()
            .Where(x => x.InventoryItemId == inventoryItemId
                && x.RetailSale.WarehouseId == warehouseId
                && HoldingStatuses.Contains(x.RetailSale.Status))
            .SumAsync(x => (decimal?)x.Quantity, cancellationToken) ?? 0m;

        return onHand - reserved;
    }

    public async Task<RetailSale> CreateAsync(
        RetailSaleInput input, CancellationToken cancellationToken)
    {
        if (input.Items.Count == 0)
            throw new InvalidOperationException("Satış fişinde en az bir kalem olmalıdır.");

        var warehouse = await db.Warehouses
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == input.WarehouseId, cancellationToken)
            ?? throw new InvalidOperationException("Depo bulunamadı.");

        if (warehouse.Type != WarehouseType.Central)
        {
            throw new InvalidOperationException(
                "Perakende satış yalnızca merkez depodan yapılır.");
        }

        // Vade ve çek KAYITLI CARİ ister: alacağın kime ait olduğu
        // bilinmeden vade takibi ve nakit akış tahmini yapılamaz.
        var needsCustomer = input.PaymentMethod is RetailPaymentMethod.Term
            or RetailPaymentMethod.Cheque;

        if (needsCustomer && input.CustomerCurrentAccountId is null)
        {
            throw new InvalidOperationException(
                "Vadeli ve çekli satışta kayıtlı müşteri (cari) zorunludur.");
        }

        if (input.PaymentMethod == RetailPaymentMethod.Term && input.DueDate is null)
            throw new InvalidOperationException("Vadeli satışta vade tarihi zorunludur.");

        // ELDEN İŞARETLEME AYRI İZİN. Bu kontrol arayüzde de var ama
        // burada olmak zorunda: uç doğrudan çağrılabiliyor.
        if (input.CashAmount > 0 && !await HasAsync(PermissionCatalog.Keys.SalesCash, cancellationToken))
        {
            throw new UnauthorizedAccessException(
                "Elden tutar işaretleme yetkiniz yok. Standart satışta tutarın tamamı kayıtlıdır.");
        }

        var itemIds = input.Items.Select(x => x.InventoryItemId).Distinct().ToArray();

        var cards = await db.InventoryItems
            .AsNoTracking()
            .Where(x => itemIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var sale = new RetailSale
        {
            CompanyId = input.CompanyId,
            WarehouseId = input.WarehouseId,
            SaleDate = AsUtc(input.SaleDate),
            CustomerCurrentAccountId = input.CustomerCurrentAccountId,
            WalkInCustomerName = Normalize(input.WalkInCustomerName),
            PaymentMethod = input.PaymentMethod,
            DueDate = input.DueDate.HasValue ? AsUtc(input.DueDate.Value) : null,
            OverallDiscountRate = input.OverallDiscountRate,
            CashAmount = input.CashAmount,
            CashAccountId = input.CashAccountId,
            Status = RetailSaleStatus.Draft,
            DocumentNumber = await documentNumbers.GenerateAsync(
                input.CompanyId, "RETAIL_SALE", "PSF", cancellationToken)
        };

        var line = 0;

        foreach (var requested in input.Items)
        {
            if (!cards.TryGetValue(requested.InventoryItemId, out var card))
                throw new InvalidOperationException("Stok kartı bulunamadı.");

            if (card.SalesPrice is not decimal price)
            {
                throw new InvalidOperationException(
                    $"{card.Name}: satış fiyatı tanımlı değil, perakende satışa kapalı.");
            }

            if (requested.Quantity <= 0)
                throw new InvalidOperationException($"{card.Name}: miktar sıfırdan büyük olmalıdır.");

            // STOK YETERSİZSE ENGELLE. Sanal rezerv sayesinde aynı malı
            // bekleyen iki fiş birbirini görüyor; ikinci satış açılmıyor.
            var available = await GetAvailableAsync(
                input.WarehouseId, requested.InventoryItemId, cancellationToken);

            if (available < requested.Quantity)
            {
                throw new InvalidOperationException(
                    $"{card.Name}: merkez depoda yeterli stok yok. " +
                    $"Satılabilir {available:0.##} {card.Unit}, istenen {requested.Quantity:0.##} {card.Unit}. " +
                    "(Onay bekleyen fişlerdeki miktar düşülmüştür.)");
            }

            // FİYAT VE TAVAN KARTTAN. İstemciden gelen fiyat hiç okunmuyor;
            // iskonto oranı okunuyor ama tavanı burada doğrulanıyor.
            var discount = Math.Max(0m, requested.DiscountRate);
            var vatRate = card.VatRate ?? 0m;
            var gross = Round(requested.Quantity * price);
            var subtotal = Round(gross * (1 - discount / 100m));
            var vat = Round(subtotal * vatRate / 100m);

            sale.Items.Add(new RetailSaleItem
            {
                LineNumber = ++line,
                InventoryItemId = card.Id,
                Description = card.Name,
                Unit = card.Unit,
                Quantity = requested.Quantity,
                UnitPrice = price,
                DiscountRate = discount,
                MaxDiscountRateAtSale = card.MaxDiscountRate,
                VatRate = vatRate,
                LineSubtotal = subtotal,
                VatAmount = vat,
                LineTotal = subtotal + vat
            });
        }

        ApplyTotals(sale);

        if (sale.CashAmount > sale.GrandTotal)
        {
            throw new InvalidOperationException(
                "Elden tutar, fiş toplamından büyük olamaz.");
        }

        sale.RecordedAmount = sale.GrandTotal - sale.CashAmount;

        db.RetailSales.Add(sale);
        await db.SaveChangesAsync(cancellationToken);

        return sale;
    }

    /// <summary>
    /// Fişi sonuçlandırmaya gönderir.
    ///
    /// ONAY TETİKLEYİCİ İKİ TANE: iskonto tavanının aşılması VEYA vade
    /// verilmesi. İkisi de yoksa satış anında tamamlanır — peşin ve
    /// tavan içi satışta onay beklemek kasayı gereksiz durdururdu.
    /// </summary>
    public async Task<RetailSale> SubmitAsync(Guid id, CancellationToken cancellationToken)
    {
        var sale = await LoadAsync(id, cancellationToken);

        if (sale.Status != RetailSaleStatus.Draft)
            throw new InvalidOperationException("Yalnızca taslak fiş gönderilebilir.");

        var reasons = new List<string>();

        // TAVAN SUNUCUDA. Satır tavanı kartın o anki değeriyle değil,
        // fişe kopyalanmış değerle karşılaştırılıyor — arada kart
        // değişse bile fişin kendi koşulu geçerli.
        var exceeded = sale.Items
            .Where(x => x.DiscountRate > x.MaxDiscountRateAtSale)
            .ToList();

        foreach (var item in exceeded)
        {
            reasons.Add(
                $"{item.Description}: iskonto %{item.DiscountRate:0.##} " +
                $"(tavan %{item.MaxDiscountRateAtSale:0.##})");
        }

        if (sale.OverallDiscountRate > 0)
        {
            // Fiş geneli iskontonun tavanı, fişteki EN DÜŞÜK satır
            // tavanıdır: aksi hâlde tavanı düşük bir kalem, fiş
            // iskontosu üzerinden dolaylı olarak indirilebilirdi.
            var lowest = sale.Items.Min(x => x.MaxDiscountRateAtSale);

            if (sale.OverallDiscountRate > lowest)
            {
                reasons.Add(
                    $"Fiş geneli iskonto %{sale.OverallDiscountRate:0.##} " +
                    $"(fişteki en düşük tavan %{lowest:0.##})");
            }
        }

        if (sale.PaymentMethod == RetailPaymentMethod.Term)
            reasons.Add($"Vadeli satış ({sale.DueDate:dd.MM.yyyy})");

        sale.SubmittedAtUtc = DateTime.UtcNow;
        sale.SubmittedByUserId = currentUser.UserId;

        if (reasons.Count > 0)
        {
            sale.Status = RetailSaleStatus.PendingApproval;
            sale.ApprovalReason = string.Join(" · ", reasons);
            await db.SaveChangesAsync(cancellationToken);
            return sale;
        }

        await CompleteAsync(sale, cancellationToken);
        return sale;
    }

    public async Task<RetailSale> ApproveAsync(Guid id, CancellationToken cancellationToken)
    {
        var sale = await LoadAsync(id, cancellationToken);

        if (sale.Status != RetailSaleStatus.PendingApproval)
            throw new InvalidOperationException("Yalnızca onay bekleyen fiş onaylanabilir.");

        sale.DecidedAtUtc = DateTime.UtcNow;
        sale.DecidedByUserId = currentUser.UserId;

        await CompleteAsync(sale, cancellationToken);
        return sale;
    }

    public async Task<RetailSale> RejectAsync(
        Guid id, string reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Red gerekçesi zorunludur.");

        var sale = await LoadAsync(id, cancellationToken);

        if (sale.Status != RetailSaleStatus.PendingApproval)
            throw new InvalidOperationException("Yalnızca onay bekleyen fiş reddedilebilir.");

        sale.Status = RetailSaleStatus.Rejected;
        sale.DecisionReason = reason.Trim();
        sale.DecidedAtUtc = DateTime.UtcNow;
        sale.DecidedByUserId = currentUser.UserId;

        // Rezerv ayrıca serbest bırakılmıyor: sanal rezerv yalnız
        // Draft/PendingApproval fişlerden hesaplanıyor, durum değişince
        // kendiliğinden çözülüyor.
        await db.SaveChangesAsync(cancellationToken);
        return sale;
    }

    /// <summary>
    /// FİŞİ GERÇEĞE ÇEVİREN TEK YER.
    ///
    /// Fatura + stok + tahsilat aynı transaction'da, TEK KEZ oluşuyor.
    /// Üçünün ayrı uçlara dağıtılması hâlinde biri koşup ötekiler
    /// koşmazsa stok düşmüş ama gelir yazılmamış bir satış kalırdı.
    /// </summary>
    private async Task CompleteAsync(RetailSale sale, CancellationToken cancellationToken)
    {
        if (sale.SalesInvoiceId is not null)
            throw new InvalidOperationException("Bu fiş zaten sonuçlandırılmış.");

        // Belge numaraları transaction dışında üretilir: DocumentNumberService
        // kendi transaction'ını açıyor ve iç içe transaction hatası verir.
        var issueNumbers = new Dictionary<Guid, string>();

        foreach (var item in sale.Items)
        {
            issueNumbers[item.Id] = await documentNumbers.GenerateAsync(
                sale.CompanyId, "STOCK_ISSUE", "CIKIS", cancellationToken);
        }

        var invoiceNumber = sale.RecordedAmount > 0
            ? await documentNumbers.GenerateAsync(
                sale.CompanyId, "SALES_INVOICE", "SAT", cancellationToken)
            : null;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // 1) STOK — mal çıktığı için TAM miktar düşer. Elden/kayıtlı
        //    ayrımı yalnız paranın kaydında; malın kendisinde değil.
        foreach (var item in sale.Items)
        {
            var stock = await db.WarehouseStocks
                .Include(x => x.InventoryItem)
                .SingleOrDefaultAsync(
                    x => x.WarehouseId == sale.WarehouseId
                        && x.InventoryItemId == item.InventoryItemId,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    $"{item.Description}: merkez depoda stok kaydı yok.");

            if (stock.Quantity < item.Quantity)
            {
                throw new InvalidOperationException(
                    $"{item.Description}: stok yetersiz, satış tamamlanamadı.");
            }

            stock.Quantity -= item.Quantity;
            stock.UpdatedAtUtc = DateTime.UtcNow;

            var unitCost = stock.InventoryItem.AverageUnitCost;

            db.StockMovements.Add(new StockMovement
            {
                CompanyId = sale.CompanyId,
                WarehouseId = sale.WarehouseId,
                InventoryItemId = item.InventoryItemId,
                Type = StockMovementType.Issue,
                Quantity = item.Quantity,
                UnitCost = unitCost,
                TotalCost = unitCost * item.Quantity,
                ReferenceNumber = issueNumbers[item.Id],
                MovementDate = sale.SaleDate,
                Description = $"Perakende satış {sale.DocumentNumber}",
                CreatedByUserId = currentUser.UserId
            });
        }

        // 2) GELİR — YALNIZ KAYITLI TUTAR. Elden kısım faturaya, muhasebe
        //    fişine ve resmî gelire girmez.
        if (invoiceNumber is not null && sale.CustomerCurrentAccountId is Guid customerId)
        {
            var invoice = BuildInvoice(sale, customerId, invoiceNumber);

            db.SalesInvoices.Add(invoice);
            await db.SaveChangesAsync(cancellationToken);

            invoice.AccountingVoucherId = await accounting
                .CreateSalesInvoiceVoucherAsync(invoice, cancellationToken);
            invoice.Status = SalesInvoiceStatus.Posted;
            invoice.PostedAtUtc = DateTime.UtcNow;
            invoice.PostedByUserId = currentUser.UserId;

            sale.SalesInvoiceId = invoice.Id;
        }

        // 3) TAHSİLAT — yalnız peşin ve kartta, YALNIZ KAYITLI TUTAR.
        //
        // Elden kısım resmî kasaya girmez; girseydi kayıt dışı para
        // muhasebe fişine ve gün sonu kasasına sızardı. Vade ve çekte
        // ise o an tahsilat yoktur: alacak açık kalır ve nakit akışa
        // vade tarihiyle girer.
        var collects = sale.PaymentMethod is RetailPaymentMethod.Cash
            or RetailPaymentMethod.CreditCard;

        if (collects && sale.RecordedAmount > 0)
        {
            if (sale.CashAccountId is not Guid cashAccountId)
            {
                throw new InvalidOperationException(
                    "Peşin ve kartlı satışta tahsilatın gireceği kasa/banka hesabı seçilmelidir.");
            }

            var collection = new CashTransaction
            {
                CashAccountId = cashAccountId,
                TransactionDate = sale.SaleDate,
                TransactionType = CashTransactionType.Collection,
                Direction = CashTransactionDirection.In,
                Amount = sale.RecordedAmount,
                AmountTry = sale.RecordedAmount,
                CurrencyCode = "TRY",
                ExchangeRate = 1m,
                Description = $"Perakende satış {sale.DocumentNumber}",
                DocumentNumber = sale.DocumentNumber,
                CurrentAccountId = sale.CustomerCurrentAccountId,
                SourceModule = "RETAIL_SALE",
                SourceEntityId = sale.Id
            };

            db.CashTransactions.Add(collection);
            await db.SaveChangesAsync(cancellationToken);

            collection.AccountingVoucherId = await accounting
                .CreateCashTransactionVoucherAsync(collection, cancellationToken);

            sale.CashTransactionId = collection.Id;
        }

        sale.Status = RetailSaleStatus.Completed;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static SalesInvoice BuildInvoice(
        RetailSale sale, Guid customerId, string invoiceNumber)
    {
        // Kayıtlı tutar fiş toplamının bir oranıysa (kısmen elden),
        // satırlar aynı oranla ölçeklenir; toplam kayıtlı tutarla
        // birebir tutsun diye son satırda yuvarlama farkı kapatılır.
        var ratio = sale.GrandTotal == 0m ? 0m : sale.RecordedAmount / sale.GrandTotal;

        var invoice = new SalesInvoice
        {
            CompanyId = sale.CompanyId,
            CustomerCurrentAccountId = customerId,
            InternalNumber = invoiceNumber,
            InvoiceDate = sale.SaleDate,
            DueDate = sale.DueDate,
            CurrencyCode = "TRY",
            ExchangeRate = 1m,
            Description = $"Perakende satış {sale.DocumentNumber}",
            ParseSource = EInvoiceParseSource.Manual,
            Status = SalesInvoiceStatus.Draft
        };

        var line = 0;
        decimal subtotal = 0m, vatTotal = 0m;

        foreach (var item in sale.Items)
        {
            var lineSubtotal = Round(item.LineSubtotal * ratio);
            var lineVat = Round(item.VatAmount * ratio);

            subtotal += lineSubtotal;
            vatTotal += lineVat;

            invoice.Items.Add(new SalesInvoiceItem
            {
                LineNumber = ++line,
                Description = item.Description,
                Unit = item.Unit,
                Quantity = item.Quantity,
                UnitPrice = item.Quantity == 0m ? 0m : Round6(lineSubtotal / item.Quantity),
                VatRate = item.VatRate,
                LineSubtotal = lineSubtotal,
                VatAmount = lineVat,
                LineTotal = lineSubtotal + lineVat
            });
        }

        invoice.Subtotal = subtotal;
        invoice.VatTotal = vatTotal;
        invoice.GrandTotal = subtotal + vatTotal;
        invoice.NetReceivableAmount = invoice.GrandTotal;

        return invoice;
    }

    private static void ApplyTotals(RetailSale sale)
    {
        var lineSubtotal = sale.Items.Sum(x => x.LineSubtotal);
        var discount = Round(lineSubtotal * sale.OverallDiscountRate / 100m);
        var netSubtotal = lineSubtotal - discount;

        // Fiş iskontosu satır KDV'sini de orantılı düşürür; aksi hâlde
        // KDV, üzerinden hesaplandığı matrahtan büyük kalırdı.
        var ratio = lineSubtotal == 0m ? 0m : netSubtotal / lineSubtotal;
        var vat = Round(sale.Items.Sum(x => x.VatAmount) * ratio);

        sale.Subtotal = lineSubtotal;
        sale.DiscountAmount = discount;
        sale.VatTotal = vat;
        sale.GrandTotal = netSubtotal + vat;
    }

    private async Task<RetailSale> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        return await db.RetailSales
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Satış fişi bulunamadı.");
    }

    private async Task<bool> HasAsync(string permission, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
            return false;

        var snapshot = await authorization.GetAsync(userId, cancellationToken);

        return snapshot is not null
            && snapshot.IsActive
            && snapshot.Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    private static decimal Round6(decimal value) => Math.Round(value, 6, MidpointRounding.AwayFromZero);
    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
