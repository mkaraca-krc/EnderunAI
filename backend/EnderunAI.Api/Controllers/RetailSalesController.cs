using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.Retail;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

public sealed record RetailSaleLineRequest(
    Guid InventoryItemId,
    decimal Quantity,
    decimal DiscountRate);

public sealed record CreateRetailSaleRequest(
    Guid CompanyId,
    Guid WarehouseId,
    DateTime SaleDate,
    Guid? CustomerCurrentAccountId,
    string? WalkInCustomerName,
    int PaymentMethod,
    DateTime? DueDate,
    decimal OverallDiscountRate,
    decimal CashAmount,
    Guid? CashAccountId,
    List<RetailSaleLineRequest> Items);

public sealed record RejectRetailSaleRequest(string Reason);

public sealed record CancelRetailSaleRequest(string Reason);

public sealed record RetailReturnLineRequest(Guid RetailSaleItemId, decimal Quantity);

public sealed record CreateRetailReturnRequest(
    string Reason,
    List<RetailReturnLineRequest> Items);

/// <summary>
/// Perakende satış uçları.
///
/// SATIŞ EKRANI MALİYETİ HİÇ GÖRMEZ: ürün araması bu controller'daki
/// dar uçtan besleniyor ve o uç AverageUnitCost okumuyor. Mevcut stok
/// uçları (InventoryController) maliyeti döndürmeye devam ediyor —
/// oradaki davranış değiştirilmedi, çünkü onu okuyan satın alma ve
/// muhasebe ekranları maliyeti görmek zorunda.
/// </summary>
[ApiController]
/*
 * [Authorize] ZORUNLU — EKSİKTİ.
 *
 * `RequirePermission` düz bir ATTRIBUTE'tur, filtre değil: zorlamayı
 * PermissionAuthorizationMiddleware yapıyor ve o middleware kimlik
 * doğrulanmamış isteği kontrol etmeden `next`'e geçiriyor. Yani izin
 * kontrolü YALNIZCA giriş yapmış kullanıcılar için çalışıyor.
 *
 * Bu sınıfta [Authorize] yoktu: perakende modülünün TAMAMI —
 * satış listesi, ürün fiyatları, gün sonu kasa raporu — kimlik
 * doğrulaması olmadan çağrılabiliyordu. Sistemdeki diğer bütün
 * controller'lar ya [Authorize] ya [Authorize(Roles=...)] taşıyor;
 * bilinçli anonim olanlar yalnız AuthController (giriş) ve
 * PortalController (kendi token modeli).
 */
[Authorize]
[Route("api/perakende")]
public sealed class RetailSalesController(
    AppDbContext db,
    IRetailSaleService sales,
    IExtraPaymentVisibilityService cashVisibility,
    ICurrentUserService currentUser,
    IUserAuthorizationService authorization,
    ICurrentDataScopeService dataScope) : ControllerBase
{
    private async Task<CurrentDataScopeSnapshot> GetScopeAsync(
        CancellationToken cancellationToken) =>
        await dataScope.GetAsync(cancellationToken) ??
        throw new UnauthorizedAccessException("Kullanıcı veri kapsamı bulunamadı.");

    /// <summary>
    /// Kullanıcının izni var mı. Görünürlük servisleriyle aynı desen:
    /// karar rolden değil İZİNDEN türetiliyor, böylece kullanıcı bazlı
    /// kısıtlama (UserPermissionOverride) da geçerli oluyor.
    /// </summary>
    /// <summary>Tarihi gün başına indirip UTC olarak işaretler.</summary>
    private static DateTime AsUtcDate(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);

    private async Task<bool> HasAsync(string permission, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
            return false;

        var snapshot = await authorization.GetAsync(userId, cancellationToken);

        return snapshot is not null
            && snapshot.IsActive
            && snapshot.Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Satış ekranının ürün araması. Kod, ad ve barkodla arar.
    ///
    /// DÖNDÜRÜLMEYEN ALAN: maliyet. Satış personeli fiyatı ve tavanı
    /// görür, malın kaça alındığını görmez.
    /// </summary>
    [HttpGet("urunler")]
    [RequirePermission(PermissionCatalog.Keys.SalesView)]
    public async Task<IActionResult> SearchProducts(
        [FromQuery] Guid warehouseId,
        [FromQuery] string? search,
        [FromQuery] Guid? itemId,
        CancellationToken cancellationToken)
    {
        var term = search?.Trim();

        var query = db.InventoryItems
            .AsNoTracking()
            .ApplyScope(await GetScopeAsync(cancellationToken))
            // ARŞİVLENMİŞ KART SATIŞA ÇIKMAZ. Fiyatı olması yetmez;
            // arşivden çıkarılmış bir malzeme yeni satışa girerse
            // "temiz başlangıç" ilk gün bozulur.
            .Where(x => x.IsActive && x.SalesPrice != null);

        // QR ETİKETİ KİMLİKLE GELİR. Bizim bastığımız stok etiketinde
        // kart sayfasının URL'i var; kasada okutulunca oradan çıkan
        // kimlik buraya düşüyor. Kimlik METİN OLARAK aratılsaydı kod,
        // ad ve barkodun hiçbiriyle eşleşmez, etiket okutmak sessizce
        // çalışmazdı.
        if (itemId is Guid id)
        {
            query = query.Where(x => x.Id == id);
        }
        else if (!string.IsNullOrWhiteSpace(term))
        {
            query = query.Where(x =>
                EF.Functions.ILike(x.Code, $"%{term}%")
                || EF.Functions.ILike(x.Name, $"%{term}%")
                || (x.Barcode != null && EF.Functions.ILike(x.Barcode, $"%{term}%")));
        }

        var cards = await query
            .OrderBy(x => x.Name)
            .Take(50)
            .Select(x => new
            {
                x.Id,
                x.Code,
                x.Name,
                x.Unit,
                x.Barcode,
                SalesPrice = x.SalesPrice!.Value,
                x.MaxDiscountRate,
                VatRate = x.VatRate ?? 0m,
                OnHand = x.WarehouseStocks
                    .Where(s => s.WarehouseId == warehouseId)
                    .Sum(s => (decimal?)s.Quantity) ?? 0m
            })
            .ToListAsync(cancellationToken);

        // Satılabilir adet, onay bekleyen fişlerdeki miktar düşülerek
        // hesaplanıyor; ekranda görünen sayı budur.
        var ids = cards.Select(x => x.Id).ToArray();

        var reserved = await db.RetailSaleItems
            .AsNoTracking()
            .Where(x => ids.Contains(x.InventoryItemId)
                && x.RetailSale.WarehouseId == warehouseId
                && (x.RetailSale.Status == RetailSaleStatus.Draft
                    || x.RetailSale.Status == RetailSaleStatus.PendingApproval))
            .GroupBy(x => x.InventoryItemId)
            .Select(g => new { g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Quantity, cancellationToken);

        return Ok(cards.Select(x => new
        {
            x.Id,
            x.Code,
            x.Name,
            x.Unit,
            x.Barcode,
            x.SalesPrice,
            x.MaxDiscountRate,
            x.VatRate,
            Available = x.OnHand - (reserved.TryGetValue(x.Id, out var held) ? held : 0m)
        }));
    }

    /// <summary>
    /// Satışın yapılabileceği merkez depolar ve tahsilat hesapları.
    ///
    /// AYRI UÇ, ÇÜNKÜ SATIŞ PERSONELİNDE `inventory.view` YOK: genel
    /// depo ucu stok değeri ve maliyet taşıyan ekranlara hizmet ediyor.
    /// Burada yalnız seçim için gereken kimlik ve ad dönüyor.
    /// </summary>
    [HttpGet("kaynaklar")]
    [RequirePermission(PermissionCatalog.Keys.SalesView)]
    public async Task<IActionResult> GetResources(CancellationToken cancellationToken)
    {
        var scope = await GetScopeAsync(cancellationToken);

        var warehouses = await db.Warehouses
            .AsNoTracking()
            .ApplyScope(scope)
            .Where(x => x.Type == WarehouseType.Central)
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Code, x.Name, x.CompanyId })
            .ToListAsync(cancellationToken);

        var cashAccounts = await db.CashAccounts
            .AsNoTracking()
            .ApplyScope(scope)
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Code, x.Name, Type = (int)x.Type, x.CompanyId })
            .ToListAsync(cancellationToken);

        var customers = await db.CurrentAccounts
            .AsNoTracking()
            .ApplyScope(scope)
            .Where(x => x.Roles.HasFlag(CurrentAccountRoles.Customer)
                && x.Status == CurrentAccountStatus.Approved)
            .OrderBy(x => x.Title)
            .Take(500)
            .Select(x => new { x.Id, x.Code, x.Title })
            .ToListAsync(cancellationToken);

        return Ok(new { warehouses, cashAccounts, customers });
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.SalesView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? status,
        CancellationToken cancellationToken)
    {
        var canSeeCash = await cashVisibility.CanViewExtraPaymentAsync(cancellationToken);

        var query = db.RetailSales
            .AsNoTracking()
            .ApplyScope(await GetScopeAsync(cancellationToken));

        if (status.HasValue)
            query = query.Where(x => (int)x.Status == status.Value);

        var rows = await query
            .OrderByDescending(x => x.SaleDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.DocumentNumber,
                x.SaleDate,
                Status = (int)x.Status,
                PaymentMethod = (int)x.PaymentMethod,
                x.DueDate,
                CustomerTitle = x.CustomerCurrentAccount != null
                    ? x.CustomerCurrentAccount.Title
                    : x.WalkInCustomerName,
                x.GrandTotal,
                x.RecordedAmount,
                x.CashAmount,
                x.ApprovalReason,
                x.DecisionReason,
                x.SalesInvoiceId,

                // FİŞ KÂRI: satır matrahları toplamı eksi dondurulmuş
                // maliyetler toplamı. Maliyeti yazılmamış satır varsa
                // (S5 öncesi fişler) kâr hesaplanmaz — eksik veriden
                // üretilen bir kâr rakamı, hiç göstermemekten kötüdür.
                Profit = x.Items.All(i => i.LineCost != null)
                    ? x.Items.Sum(i => i.LineSubtotal) - x.Items.Sum(i => i.LineCost!.Value)
                    : (decimal?)null
            })
            .ToListAsync(cancellationToken);

        // ELDEN MASKESİ: yetkisiz kullanıcıya elden tutar null döner ve
        // kaç kayıtta gizlendiği ayrıca bildirilir — tutar sızmaz ama
        // eksik olduğu belli olur. Desen VehiclesController ile aynı.
        var hiddenCount = canSeeCash ? 0 : rows.Count(x => x.CashAmount > 0);

        var canSeeProfit = await HasAsync(
            PermissionCatalog.Keys.InventoryView, cancellationToken);

        return Ok(new
        {
            items = rows.Select(x => new
            {
                x.Id,
                x.DocumentNumber,
                x.SaleDate,
                x.Status,
                x.PaymentMethod,
                x.DueDate,
                x.CustomerTitle,
                x.GrandTotal,
                x.RecordedAmount,
                CashAmount = canSeeCash ? x.CashAmount : (decimal?)null,
                x.ApprovalReason,
                x.DecisionReason,
                x.SalesInvoiceId,

                // Kâr maliyeti ele verir; elden maskesinden AYRI bir
                // kapıya bağlı (`inventory.view`), çünkü koruduğu şey
                // farklı: biri kayıt dışı parayı, diğeri malın alış
                // fiyatını gizliyor.
                Profit = canSeeProfit ? x.Profit : null
            }),
            hiddenCount,
            profitHidden = !canSeeProfit
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.SalesCreate)]
    public async Task<IActionResult> Create(
        CreateRetailSaleRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var sale = await sales.CreateAsync(
                new RetailSaleInput(
                    request.CompanyId,
                    request.WarehouseId,
                    request.SaleDate,
                    request.CustomerCurrentAccountId,
                    request.WalkInCustomerName,
                    (RetailPaymentMethod)request.PaymentMethod,
                    request.DueDate,
                    request.OverallDiscountRate,
                    request.CashAmount,
                    request.CashAccountId,
                    request.Items.Select(x => new RetailSaleLineInput(
                        x.InventoryItemId, x.Quantity, x.DiscountRate)).ToList()),
                cancellationToken);

            return Ok(new { sale.Id, sale.DocumentNumber, sale.GrandTotal });
        }
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary>
    /// Fişi sonuçlandırmaya gönderir. Tavan aşımı ya da vade varsa
    /// finans onayına düşer; yoksa satış anında tamamlanır.
    /// </summary>
    [HttpPost("{id:guid}/gonder")]
    [RequirePermission(PermissionCatalog.Keys.SalesCreate)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var sale = await sales.SubmitAsync(id, cancellationToken);

            return Ok(new
            {
                Status = (int)sale.Status,
                sale.ApprovalReason,
                sale.SalesInvoiceId
            });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary>
    /// Finans onayı. AYRI İZİN: satışı hazırlayan personel kendi
    /// iskontosunu onaylayamaz.
    /// </summary>
    [HttpPost("{id:guid}/onayla")]
    [RequirePermission(PermissionCatalog.Keys.SalesApprove)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var sale = await sales.ApproveAsync(id, cancellationToken);
            return Ok(new { Status = (int)sale.Status, sale.SalesInvoiceId });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("{id:guid}/reddet")]
    [RequirePermission(PermissionCatalog.Keys.SalesApprove)]
    public async Task<IActionResult> Reject(
        Guid id, RejectRetailSaleRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var sale = await sales.RejectAsync(id, request.Reason, cancellationToken);
            return Ok(new { Status = (int)sale.Status, sale.DecisionReason });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary>
    /// Fiş iptali. Tamamlanmış fişte stok geri döner, fatura ters kayıt
    /// üretir ve tahsilat karşıt hareketle kapanır.
    ///
    /// FİNANS YETKİSİ: iptal geliri ve kasayı geri alıyor; satışı
    /// hazırlayan personelin işi değil.
    /// </summary>
    [HttpPost("{id:guid}/iptal")]
    [RequirePermission(PermissionCatalog.Keys.SalesApprove)]
    public async Task<IActionResult> Cancel(
        Guid id, CancelRetailSaleRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var sale = await sales.CancelAsync(id, request.Reason, cancellationToken);
            return Ok(new { Status = (int)sale.Status, sale.DecisionReason });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary>
    /// Kısmi ya da tam iade açar. Fiş HER ZAMAN finans onayına düşer.
    ///
    /// Açma yetkisi satış personelinde: iadeyi kabul eden tezgâhtaki
    /// kişidir. Onay ayrı ve finansta.
    /// </summary>
    [HttpPost("{id:guid}/iade")]
    [RequirePermission(PermissionCatalog.Keys.SalesCreate)]
    public async Task<IActionResult> CreateReturn(
        Guid id, CreateRetailReturnRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var retur = await sales.CreateReturnAsync(
                id,
                request.Items
                    .Select(x => new RetailReturnLineInput(x.RetailSaleItemId, x.Quantity))
                    .ToList(),
                request.Reason,
                cancellationToken);

            return Ok(new { retur.Id, retur.DocumentNumber, retur.GrandTotal });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary>Fişin satırları — iade ekranı bununla doluyor.</summary>
    [HttpGet("{id:guid}/kalemler")]
    [RequirePermission(PermissionCatalog.Keys.SalesView)]
    public async Task<IActionResult> GetItems(Guid id, CancellationToken cancellationToken)
    {
        var items = await db.RetailSaleItems
            .AsNoTracking()
            .Where(x => x.RetailSaleId == id)
            .OrderBy(x => x.LineNumber)
            .Select(x => new
            {
                x.Id,
                x.Description,
                x.Unit,
                x.Quantity,
                x.UnitPrice,
                x.DiscountRate,
                x.LineTotal,
                x.LineSubtotal,
                x.LineCost
            })
            .ToListAsync(cancellationToken);

        // SATIR KÂRI MALİYETİ ELE VERİR, bu yüzden maliyetle AYNI
        // kapıya bağlı: `inventory.view`.
        //
        // Satış ekranının maliyeti görmemesi bilinçli bir karardı ve
        // korunuyor — satış personelinde bu izin yok, kâr sütunu ona
        // boş gelir. Yeni bir izin anahtarı açılmadı: stok maliyetini
        // bugün fiilen bu izin koruyor (fiyatlandırma ekranı da aynı
        // kapıyı kullanıyor) ve ikinci bir anahtar iki ekranın zamanla
        // ayrışmasına yol açardı.
        var canSeeCost = await HasAsync(
            PermissionCatalog.Keys.InventoryView, cancellationToken);

        // Daha önce iade edilen miktar: ekran kalan iade edilebiliri
        // göstersin diye. Sunucu ayrıca kendi kontrolünü yapıyor.
        var returned = await db.RetailSaleItems
            .AsNoTracking()
            .Where(x => x.RetailSale.OriginalSaleId == id
                && x.RetailSale.Status != RetailSaleStatus.Cancelled
                && x.RetailSale.Status != RetailSaleStatus.Rejected)
            .GroupBy(x => x.Description)
            .Select(g => new { g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Quantity, cancellationToken);

        return Ok(items.Select(x => new
        {
            x.Id,
            x.Description,
            x.Unit,
            x.Quantity,
            x.UnitPrice,
            x.DiscountRate,
            x.LineTotal,
            LineCost = canSeeCost ? x.LineCost : null,
            LineProfit = canSeeCost && x.LineCost is decimal cost
                ? decimal.Round(x.LineSubtotal - cost, 2)
                : (decimal?)null,
            AlreadyReturned = returned.GetValueOrDefault(x.Description, 0m)
        }));
    }

    /// <summary>
    /// GÜN SONU KASA.
    ///
    /// ŞİRKET FİLTRESİ ZORUNLU: kasa dökümü şirket bazındadır. Filtre
    /// olmadan yazılmıştı ve test yakaladı — çok şirketli kurulumda
    /// bir şirketin kasası ötekinin satışlarını da toplardı.
    ///
    /// HER RAKAM KENDİ KAYNAĞINDAN OKUNUYOR, yeniden hesaplanmıyor:
    ///   nakit / kart -> CashTransaction (fiilen kasaya giren para)
    ///   çek / vade   -> henüz tahsilat yok; fişin kendi tutarı
    ///   elden        -> fişin ayrı alanı, extra_payment.view maskeli
    ///
    /// İADE VE İPTAL DÜŞÜLÜR: kasadan çıkan karşıt hareketler aynı
    /// sorguya giriyor, ayrı bir çıkarma yapılmıyor — o da ikinci bir
    /// hesap olurdu.
    /// </summary>
    [HttpGet("raporlar/gun-sonu")]
    [RequirePermission(PermissionCatalog.Keys.SalesView)]
    public async Task<IActionResult> DayEnd(
        [FromQuery] Guid companyId,
        [FromQuery] DateTime? date,
        CancellationToken cancellationToken)
    {
        // Sorgudan gelen tarih Kind=Unspecified geliyor; PostgreSQL
        // 'timestamp with time zone' yalnız UTC kabul ediyor. İşaretleme
        // yapılmazsa uç 500 veriyor.
        /*
         * `companyId` ZORUNLU PARAMETRE AMA KAPSAM DEĞİL: kullanıcının
         * yazdığı bir değer. Kapsam süzgeci olmadan, A şirketinin
         * kullanıcısı adres çubuğuna B'nin kimliğini yazarak B'nin GÜN
         * SONU KASASINI görebiliyordu. Rapor uçları liste uçlarından
         * ayrı kod; listeyi süzmek burayı süzmedi.
         */
        var raporKapsami = await GetScopeAsync(cancellationToken);

        var day = AsUtcDate(date ?? DateTime.UtcNow);
        var next = day.AddDays(1);
        var canSeeCash = await cashVisibility.CanViewExtraPaymentAsync(cancellationToken);

        var retailModules = new[] { "RETAIL_SALE", "RETAIL_RETURN", "RETAIL_SALE_CANCEL" };

        // Kasaya fiilen giren/çıkan para. Hesap TÜRÜNE göre ayrılıyor:
        // peşin kasaya, POS tahsilatı bankaya düşüyor.
        var movements = await db.CashTransactions
            .AsNoTracking()
            .ApplyScope(raporKapsami)
            .Where(x => retailModules.Contains(x.SourceModule!)
                && x.CashAccount.CompanyId == companyId
                && x.TransactionDate >= day && x.TransactionDate < next)
            .Select(x => new
            {
                AccountType = (int)x.CashAccount.Type,
                Signed = x.Direction == CashTransactionDirection.In ? x.Amount : -x.Amount
            })
            .ToListAsync(cancellationToken);

        var cash = movements.Where(x => x.AccountType == 0).Sum(x => x.Signed);
        var card = movements.Where(x => x.AccountType == 1).Sum(x => x.Signed);

        // Çek ve vadede tahsilat yok; alacak açık. Fişin kendi tutarı
        // okunuyor ve iade fişleri ters işaretle giriyor.
        var openSales = await db.RetailSales
            .AsNoTracking()
            .ApplyScope(raporKapsami)
            .Where(x => x.CompanyId == companyId
                && x.Status == RetailSaleStatus.Completed
                && x.SaleDate >= day && x.SaleDate < next
                && (x.PaymentMethod == RetailPaymentMethod.Cheque
                    || x.PaymentMethod == RetailPaymentMethod.Term))
            .Select(x => new
            {
                Method = (int)x.PaymentMethod,
                Signed = x.IsReturn ? -x.RecordedAmount : x.RecordedAmount
            })
            .ToListAsync(cancellationToken);

        var cheque = openSales.Where(x => x.Method == 2).Sum(x => x.Signed);
        var term = openSales.Where(x => x.Method == 3).Sum(x => x.Signed);

        var cashSideRows = await db.RetailSales
            .AsNoTracking()
            .ApplyScope(raporKapsami)
            .Where(x => x.CompanyId == companyId
                && x.Status == RetailSaleStatus.Completed
                && x.SaleDate >= day && x.SaleDate < next
                && x.CashAmount > 0)
            .Select(x => new { x.IsReturn, x.CashAmount })
            .ToListAsync(cancellationToken);

        var offBook = cashSideRows.Sum(x => x.IsReturn ? -x.CashAmount : x.CashAmount);

        var saleCount = await db.RetailSales.ApplyScope(raporKapsami).CountAsync(
            x => x.CompanyId == companyId && x.Status == RetailSaleStatus.Completed
                && x.SaleDate >= day && x.SaleDate < next && !x.IsReturn,
            cancellationToken);

        var returnCount = await db.RetailSales.ApplyScope(raporKapsami).CountAsync(
            x => x.CompanyId == companyId && x.Status == RetailSaleStatus.Completed
                && x.SaleDate >= day && x.SaleDate < next && x.IsReturn,
            cancellationToken);

        return Ok(new
        {
            date = day,
            cash,
            card,
            cheque,
            term,
            recordedTotal = cash + card + cheque + term,
            // ELDEN AYRI SATIRDA ve yalnız yetkiliye. Kayıtlı toplama
            // EKLENMİYOR: eklenirse resmî ciro ile kasa dökümü
            // birbirini tutmaz.
            cashAmount = canSeeCash ? offBook : (decimal?)null,
            hiddenCount = canSeeCash ? 0 : cashSideRows.Count,
            saleCount,
            returnCount
        });
    }

    /// <summary>
    /// PERSONEL BAZINDA SATIŞ VE İSKONTO.
    ///
    /// Kimin ne kadar sattığı ve NE KADAR İSKONTO VERDİĞİ. İkincisi
    /// asıl soru: tavan içinde kalmak tek başına yeterli değil, sürekli
    /// tavana yakın iskonto veren satıcı marjı sessizce eritir.
    /// </summary>
    [HttpGet("raporlar/personel")]
    [RequirePermission(PermissionCatalog.Keys.SalesView)]
    public async Task<IActionResult> ByStaff(
        [FromQuery] Guid companyId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken)
    {
        var raporKapsami = await GetScopeAsync(cancellationToken);

        var start = AsUtcDate(from ?? DateTime.UtcNow.AddDays(-30));
        var end = AsUtcDate(to ?? DateTime.UtcNow).AddDays(1);

        var rows = await db.RetailSales
            .AsNoTracking()
            .ApplyScope(raporKapsami)
            .Where(x => x.CompanyId == companyId
                && x.Status == RetailSaleStatus.Completed
                && x.SaleDate >= start && x.SaleDate < end
                && !x.IsReturn)
            .GroupBy(x => x.SubmittedByUserId)
            .Select(g => new
            {
                UserId = g.Key,
                SaleCount = g.Count(),
                Total = g.Sum(x => x.GrandTotal),
                DiscountTotal = g.Sum(x => x.DiscountAmount),
                ApprovalCount = g.Count(x => x.ApprovalReason != null)
            })
            .ToListAsync(cancellationToken);

        var userIds = rows.Where(x => x.UserId != null).Select(x => x.UserId!.Value).ToArray();

        var names = await db.Users
            .AsNoTracking()
            .Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);

        return Ok(rows
            .OrderByDescending(x => x.Total)
            .Select(x => new
            {
                x.UserId,
                FullName = x.UserId != null && names.TryGetValue(x.UserId.Value, out var name)
                    ? name
                    : "—",
                x.SaleCount,
                x.Total,
                x.DiscountTotal,
                // Toplam içinde iskontonun payı: satıcıyı satıcıyla
                // karşılaştırmanın tek adil yolu, çünkü ciro farklı.
                DiscountRate = x.Total + x.DiscountTotal == 0m
                    ? 0m
                    : x.DiscountTotal / (x.Total + x.DiscountTotal) * 100m,
                x.ApprovalCount
            }));
    }

    /// <summary>
    /// AÇIK VADE / ALACAK.
    ///
    /// Kaynağı perakende olan satış faturalarının TAHSİL EDİLMEMİŞ
    /// bakiyesi. Fişten değil FATURADAN okunuyor — alacağın tek kaynağı
    /// o; fişten okunsaydı faturaya sonradan yapılan tahsilat
    /// görünmezdi.
    /// </summary>
    [HttpGet("raporlar/acik-vade")]
    [RequirePermission(PermissionCatalog.Keys.SalesView)]
    public async Task<IActionResult> OpenReceivables(
        [FromQuery] Guid companyId,
        CancellationToken cancellationToken)
    {
        var raporKapsami = await GetScopeAsync(cancellationToken);

        var sales = await db.RetailSales
            .AsNoTracking()
            .ApplyScope(raporKapsami)
            .Where(x => x.CompanyId == companyId
                && x.Status == RetailSaleStatus.Completed
                && !x.IsReturn
                && x.SalesInvoiceId != null
                && (x.PaymentMethod == RetailPaymentMethod.Term
                    || x.PaymentMethod == RetailPaymentMethod.Cheque))
            .Select(x => new
            {
                x.Id,
                x.DocumentNumber,
                x.SaleDate,
                x.DueDate,
                PaymentMethod = (int)x.PaymentMethod,
                InvoiceId = x.SalesInvoiceId!.Value,
                CustomerTitle = x.CustomerCurrentAccount != null
                    ? x.CustomerCurrentAccount.Title
                    : x.WalkInCustomerName
            })
            .ToListAsync(cancellationToken);

        if (sales.Count == 0)
            return Ok(Array.Empty<object>());

        var invoiceIds = sales.Select(x => x.InvoiceId).ToList();

        var invoices = await db.SalesInvoices
            .AsNoTracking()
            .ApplyScope(raporKapsami)
            .Where(x => invoiceIds.Contains(x.Id) && x.Status == SalesInvoiceStatus.Posted)
            .ToDictionaryAsync(x => x.Id, x => x.NetReceivableAmount, cancellationToken);

        var collected = await db.CashTransactions
            .AsNoTracking()
            .ApplyScope(raporKapsami)
            .Where(x => x.SourceModule == "SalesInvoice"
                && x.SourceEntityId != null
                && invoiceIds.Contains(x.SourceEntityId!.Value)
                && x.Direction == CashTransactionDirection.In)
            .GroupBy(x => x.SourceEntityId!.Value)
            .Select(g => new { g.Key, Total = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.Key, x => x.Total, cancellationToken);

        var today = DateTime.UtcNow.Date;

        return Ok(sales
            .Where(x => invoices.ContainsKey(x.InvoiceId))
            .Select(x => new
            {
                x.Id,
                x.DocumentNumber,
                x.SaleDate,
                x.DueDate,
                x.PaymentMethod,
                x.CustomerTitle,
                Remaining = invoices[x.InvoiceId] - collected.GetValueOrDefault(x.InvoiceId, 0m),
                IsOverdue = x.DueDate != null && x.DueDate.Value.Date < today
            })
            .Where(x => x.Remaining > 0m)
            .OrderBy(x => x.DueDate ?? x.SaleDate));
    }

    /// <summary>
    /// FİYATLANDIRMA EKRANININ VERİSİ — MALİYET DAHİL.
    ///
    /// Satış ekranı maliyeti görmez; BU EKRAN GÖRÜR ve görmesi şart:
    /// satış fiyatını ve iskonto tavanını maliyetten habersiz koymak,
    /// tavana kadar iskonto yapan personelin farkında olmadan maliyet
    /// altına satmasına yol açar. Tavanı koyan kişi marjı görmeli.
    ///
    /// MALİYET GÖRÜNÜRLÜĞÜ MEVCUT İZNE BAĞLI, yeni anahtar açılmadı:
    /// stok maliyetini bugün fiilen `inventory.view` koruyor
    /// (InventoryController AverageUnitCost'u o izinle döndürüyor).
    /// Tek kaynak o. Ekran `inventory.edit` ile açılıyor; maliyet
    /// ayrıca `inventory.view` isteyip yoksa null dönüyor ve kaç
    /// kalemde gizlendiği hiddenCount ile bildiriliyor.
    ///
    /// Satış personelinde ikisi de yok — ekrana giremiyor, girse de
    /// maliyet maskeli gelirdi.
    /// </summary>
    [HttpGet("fiyatlar")]
    [RequirePermission(PermissionCatalog.Keys.InventoryEdit)]
    public async Task<IActionResult> GetPricing(
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var canSeeCost = await HasAsync(
            PermissionCatalog.Keys.InventoryView, cancellationToken);

        var term = search?.Trim();

        var query = db.InventoryItems
            .AsNoTracking()
            .ApplyScope(await GetScopeAsync(cancellationToken));

        if (!string.IsNullOrWhiteSpace(term))
        {
            query = query.Where(x =>
                EF.Functions.ILike(x.Code, $"%{term}%")
                || EF.Functions.ILike(x.Name, $"%{term}%")
                || (x.Barcode != null && EF.Functions.ILike(x.Barcode, $"%{term}%")));
        }

        var rows = await query
            .OrderBy(x => x.Name)
            .Take(200)
            .Select(x => new
            {
                x.Id,
                x.Code,
                x.Name,
                x.Unit,
                x.SalesPrice,
                x.MaxDiscountRate,
                x.AverageUnitCost
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            items = rows.Select(x => new
            {
                x.Id,
                x.Code,
                x.Name,
                x.Unit,
                x.SalesPrice,
                x.MaxDiscountRate,
                AverageUnitCost = canSeeCost ? x.AverageUnitCost : (decimal?)null
            }),
            hiddenCount = canSeeCost ? 0 : rows.Count
        });
    }

    /// <summary>
    /// Satış fiyatı ve iskonto tavanı güncelleme — tek tek ya da toplu.
    /// Yönetim işi olduğu için stok düzenleme izni aranıyor.
    /// </summary>
    [HttpPut("fiyatlar")]
    [RequirePermission(PermissionCatalog.Keys.InventoryEdit)]
    public async Task<IActionResult> UpdatePricing(
        List<RetailPricingRequest> request, CancellationToken cancellationToken)
    {
        var ids = request.Select(x => x.InventoryItemId).ToArray();

        var cards = await db.InventoryItems
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        foreach (var row in request)
        {
            if (!cards.TryGetValue(row.InventoryItemId, out var card))
                continue;

            if (row.MaxDiscountRate is < 0 or > 100)
            {
                return BadRequest(new
                {
                    message = $"{card.Name}: iskonto tavanı 0 ile 100 arasında olmalıdır."
                });
            }

            card.SalesPrice = row.SalesPrice;
            card.MaxDiscountRate = row.MaxDiscountRate;
            card.UpdatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { updated = request.Count });
    }
}

public sealed record RetailPricingRequest(
    Guid InventoryItemId,
    decimal? SalesPrice,
    decimal MaxDiscountRate);
