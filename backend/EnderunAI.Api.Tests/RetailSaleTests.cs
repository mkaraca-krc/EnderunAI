using System.Net;
using System.Net.Http.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Security;

using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Retail;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// PERAKENDE SATIŞ — çekirdek kurallar.
///
/// Asıl güvence ÇİFT SAYIM YOKLUĞU: onaylı satış fatura, stok ve
/// tahsilatı BİRER KEZ oluşturmalı. Perakende ayrı bir satış defteri
/// olsaydı ciro iki kaynaktan toplanır ve rakamlar sessizce şişerdi.
///
/// İkinci güvence TAVANIN SUNUCUDA olması: ekran tavanı gösterip
/// girişi kısıtlasa bile uç doğrudan çağrılabiliyor.
/// </summary>
[Collection("Integration")]
public sealed class RetailSaleTests(DatabaseFixture fixture)
{
    private sealed record Context(
        Guid CompanyId, Guid WarehouseId, Guid ItemId, Guid CustomerId, Guid CashAccountId);

    /// <summary>
    /// Merkez depoda 100 adet stoğu, 100 TL satış fiyatı ve %10 iskonto
    /// tavanı olan bir kalem kurar.
    /// </summary>
    private async Task<Context> CreateContextAsync(
        string suffix, decimal onHand = 100m, decimal maxDiscount = 10m)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);
        var companyId = project.CompanyId;

        var warehouse = new Warehouse
        {
            CompanyId = companyId,
            BranchId = await db.Branches.Where(x => x.CompanyId == companyId)
                .Select(x => x.Id).FirstAsync(),
            Code = $"MRK-{suffix}",
            Name = $"Merkez Depo {suffix}",
            Type = WarehouseType.Central
        };
        db.Warehouses.Add(warehouse);

        var item = new InventoryItem
        {
            CompanyId = companyId,
            Code = $"URN-{suffix}",
            Name = $"Test Ürün {suffix}",
            Unit = "adet",
            AverageUnitCost = 60m,
            SalesPrice = 100m,
            MaxDiscountRate = maxDiscount,
            VatRate = 20m
        };
        db.InventoryItems.Add(item);

        // Müşteri olarak proje ile birlikte açılan cariyi kullanıyoruz;
        // yenisini açmak CompanyId+Code tekilliğine takılıyor.
        var customerId = project.EmployerCurrentAccountId;

        // Test şirketinde hesap planı kurulu değil; kasa hesabının
        // bağlanacağı defter hesabını burada açıyoruz.
        var accountingAccount = new AccountingAccount
        {
            CompanyId = companyId,
            Code = $"100.{suffix}"[..12],
            Name = $"Kasa {suffix}",
            Nature = AccountingAccountNature.Debit,
            Level = 1,
            IsPostingAllowed = true
        };
        db.AccountingAccounts.Add(accountingAccount);
        await db.SaveChangesAsync();

        var cashAccount = new CashAccount
        {
            CompanyId = companyId,
            Type = CashAccountType.Cash,
            Code = $"KASA-{suffix}",
            Name = $"Test Kasa {suffix}",
            CurrencyCode = "TRY",
            AccountingAccountId = accountingAccount.Id,
            IsActive = true
        };
        db.CashAccounts.Add(cashAccount);

        // Satış fişi muhasebe fişi üretiyor; gelir ve KDV hesapları
        // yapılandırılmamışsa AccountingIntegrationService bilinçli
        // olarak reddediyor. Test bu ayarları kuruyor.
        var revenueAccount = new AccountingAccount
        {
            CompanyId = companyId,
            Code = $"600.{suffix}"[..12],
            Name = $"Yurtiçi Satışlar {suffix}",
            Nature = AccountingAccountNature.Credit,
            Level = 1,
            IsPostingAllowed = true
        };
        var vatOutAccount = new AccountingAccount
        {
            CompanyId = companyId,
            Code = $"391.{suffix}"[..12],
            Name = $"Hesaplanan KDV {suffix}",
            Nature = AccountingAccountNature.Credit,
            Level = 1,
            IsPostingAllowed = true
        };
        var receivableAccount = new AccountingAccount
        {
            CompanyId = companyId,
            Code = $"120.{suffix}"[..12],
            Name = $"Alıcılar {suffix}",
            Nature = AccountingAccountNature.Debit,
            Level = 1,
            IsPostingAllowed = true
        };
        db.AccountingAccounts.AddRange(revenueAccount, vatOutAccount, receivableAccount);
        await db.SaveChangesAsync();

        db.CompanyFinanceSettings.Add(new CompanyFinanceSettings
        {
            CompanyId = companyId,
            SalesAccountId = revenueAccount.Id,
            VatOutAccountId = vatOutAccount.Id,
            ReceivablesAccountId = receivableAccount.Id
        });

        await db.SaveChangesAsync();

        db.WarehouseStocks.Add(new WarehouseStock
        {
            WarehouseId = warehouse.Id,
            InventoryItemId = item.Id,
            Quantity = onHand
        });
        await db.SaveChangesAsync();

        return new Context(companyId, warehouse.Id, item.Id, customerId!.Value, cashAccount.Id);
    }

    private static RetailSaleInput Input(
        Context context,
        decimal quantity = 1m,
        decimal discount = 0m,
        RetailPaymentMethod method = RetailPaymentMethod.Cash,
        DateTime? dueDate = null,
        decimal cashAmount = 0m,
        Guid? customerId = null)
    {
        return new RetailSaleInput(
            context.CompanyId,
            context.WarehouseId,
            DateTime.UtcNow.Date,
            customerId ?? context.CustomerId,
            null,
            method,
            dueDate,
            0m,
            cashAmount,
            method is RetailPaymentMethod.Cash or RetailPaymentMethod.CreditCard
                ? context.CashAccountId
                : null,
            [new RetailSaleLineInput(context.ItemId, quantity, discount)]);
    }

    private async Task<T> WithServiceAsync<T>(Func<IRetailSaleService, Task<T>> action)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IRetailSaleService>());
    }

    /// <summary>
    /// Peşin ve tavan içi satış onay beklemeden tamamlanır — kasayı
    /// gereksiz durdurmamak için.
    /// </summary>
    [Fact]
    public async Task CashSaleWithinCap_CompletesWithoutApproval()
    {
        var context = await CreateContextAsync($"R{Guid.NewGuid():N}"[..8]);

        var sale = await WithServiceAsync(async service =>
        {
            var created = await service.CreateAsync(
                Input(context, quantity: 2m, discount: 5m), CancellationToken.None);

            return await service.SubmitAsync(created.Id, CancellationToken.None);
        });

        Assert.Equal(RetailSaleStatus.Completed, sale.Status);
        Assert.Null(sale.ApprovalReason);
        Assert.NotNull(sale.SalesInvoiceId);
    }

    /// <summary>
    /// TAVAN AŞIMI ONAYA DÜŞER — ve onaya kadar STOK DÜŞMEZ, fatura
    /// oluşmaz. Tavanı aşan satış reddedilmiyor; yetki sınırı olduğu
    /// için finansa gidiyor.
    /// </summary>
    [Fact]
    public async Task DiscountAboveCap_WaitsForApproval_AndDoesNotTouchStock()
    {
        var context = await CreateContextAsync($"R{Guid.NewGuid():N}"[..8]);

        var sale = await WithServiceAsync(async service =>
        {
            var created = await service.CreateAsync(
                Input(context, quantity: 3m, discount: 25m), CancellationToken.None);

            return await service.SubmitAsync(created.Id, CancellationToken.None);
        });

        Assert.Equal(RetailSaleStatus.PendingApproval, sale.Status);
        Assert.Contains("iskonto", sale.ApprovalReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(sale.SalesInvoiceId);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var stock = await db.WarehouseStocks.SingleAsync(
            x => x.WarehouseId == context.WarehouseId && x.InventoryItemId == context.ItemId);

        Assert.Equal(100m, stock.Quantity);
        Assert.False(await db.StockMovements.AnyAsync(
            x => x.InventoryItemId == context.ItemId));
    }

    /// <summary>Vade, iskonto tavan içinde olsa bile onaya düşürür.</summary>
    [Fact]
    public async Task TermSale_WaitsForApproval()
    {
        var context = await CreateContextAsync($"R{Guid.NewGuid():N}"[..8]);

        var sale = await WithServiceAsync(async service =>
        {
            var created = await service.CreateAsync(
                Input(context,
                    method: RetailPaymentMethod.Term,
                    dueDate: DateTime.UtcNow.Date.AddDays(30)),
                CancellationToken.None);

            return await service.SubmitAsync(created.Id, CancellationToken.None);
        });

        Assert.Equal(RetailSaleStatus.PendingApproval, sale.Status);
        Assert.Contains("Vadeli", sale.ApprovalReason!);
    }

    /// <summary>
    /// ONAYLI SATIŞ FATURA + STOK + TAHSİLATI BİRER KEZ ÜRETİR.
    ///
    /// Çift sayımın en olası hâli: onay ucunun iki kez çağrılması.
    /// İkinci çağrı reddedilmeli, aksi hâlde aynı satış iki fatura ve
    /// iki stok düşümü doğururdu.
    /// </summary>
    [Fact]
    public async Task ApprovedSale_CreatesInvoiceStockAndCollection_Once()
    {
        var context = await CreateContextAsync($"R{Guid.NewGuid():N}"[..8]);

        var saleId = await WithServiceAsync(async service =>
        {
            var created = await service.CreateAsync(
                Input(context, quantity: 4m, discount: 30m), CancellationToken.None);

            await service.SubmitAsync(created.Id, CancellationToken.None);
            await service.ApproveAsync(created.Id, CancellationToken.None);

            return created.Id;
        });

        // İkinci onay reddedilmeli.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WithServiceAsync(service => service.ApproveAsync(saleId, CancellationToken.None)));

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sale = await db.RetailSales.SingleAsync(x => x.Id == saleId);

        Assert.Equal(RetailSaleStatus.Completed, sale.Status);

        var movements = await db.StockMovements
            .CountAsync(x => x.InventoryItemId == context.ItemId);
        Assert.Equal(1, movements);

        var stock = await db.WarehouseStocks.SingleAsync(
            x => x.WarehouseId == context.WarehouseId && x.InventoryItemId == context.ItemId);
        Assert.Equal(96m, stock.Quantity);

        var invoices = await db.SalesInvoices
            .CountAsync(x => x.Id == sale.SalesInvoiceId);
        Assert.Equal(1, invoices);

        var collections = await db.CashTransactions
            .CountAsync(x => x.SourceModule == "RETAIL_SALE" && x.SourceEntityId == saleId);
        Assert.Equal(1, collections);
    }

    /// <summary>
    /// SANAL REZERV ÇİFT SATIŞI ENGELLER: onay bekleyen fişteki miktar
    /// satılabilir stoktan düşülür, ikinci fiş açılamaz.
    /// </summary>
    [Fact]
    public async Task PendingSale_ReservesStock_BlockingSecondSale()
    {
        var context = await CreateContextAsync($"R{Guid.NewGuid():N}"[..8], onHand: 5m);

        await WithServiceAsync(async service =>
        {
            var created = await service.CreateAsync(
                Input(context, quantity: 5m, discount: 40m), CancellationToken.None);

            return await service.SubmitAsync(created.Id, CancellationToken.None);
        });

        var available = await WithServiceAsync(service =>
            service.GetAvailableAsync(context.WarehouseId, context.ItemId, CancellationToken.None));

        Assert.Equal(0m, available);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WithServiceAsync(service =>
                service.CreateAsync(Input(context, quantity: 1m), CancellationToken.None)));

        Assert.Contains("yeterli stok yok", error.Message);
    }

    /// <summary>Reddedilen fişin rezervi çözülür — mal yeniden satılabilir.</summary>
    [Fact]
    public async Task RejectedSale_ReleasesReservation()
    {
        var context = await CreateContextAsync($"R{Guid.NewGuid():N}"[..8], onHand: 5m);

        await WithServiceAsync(async service =>
        {
            var created = await service.CreateAsync(
                Input(context, quantity: 5m, discount: 40m), CancellationToken.None);

            await service.SubmitAsync(created.Id, CancellationToken.None);
            return await service.RejectAsync(created.Id, "Iskonto kabul edilmedi", CancellationToken.None);
        });

        var available = await WithServiceAsync(service =>
            service.GetAvailableAsync(context.WarehouseId, context.ItemId, CancellationToken.None));

        Assert.Equal(5m, available);
    }

    /// <summary>Red gerekçesi zorunlu — boş gerekçeyle kapatılamaz.</summary>
    [Fact]
    public async Task Reject_RequiresReason()
    {
        var context = await CreateContextAsync($"R{Guid.NewGuid():N}"[..8]);

        var saleId = await WithServiceAsync(async service =>
        {
            var created = await service.CreateAsync(
                Input(context, discount: 50m), CancellationToken.None);

            await service.SubmitAsync(created.Id, CancellationToken.None);
            return created.Id;
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WithServiceAsync(service =>
                service.RejectAsync(saleId, "   ", CancellationToken.None)));
    }

    /// <summary>Vadeli satışta kayıtlı cari zorunlu — alacak sahipsiz olamaz.</summary>
    [Fact]
    public async Task TermSale_RequiresRegisteredCustomer()
    {
        var context = await CreateContextAsync($"R{Guid.NewGuid():N}"[..8]);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WithServiceAsync(service => service.CreateAsync(
                new RetailSaleInput(
                    context.CompanyId, context.WarehouseId, DateTime.UtcNow.Date,
                    null, "İsimsiz Müşteri", RetailPaymentMethod.Term,
                    DateTime.UtcNow.Date.AddDays(15), 0m, 0m, null,
                    [new RetailSaleLineInput(context.ItemId, 1m, 0m)]),
                CancellationToken.None)));

        Assert.Contains("kayıtlı müşteri", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Satış fiyatı tanımsız kalem perakendeye KAPALIDIR — sıfır fiyatla
    /// bedava satılamaz.
    /// </summary>
    [Fact]
    public async Task ItemWithoutSalesPrice_CannotBeSold()
    {
        var context = await CreateContextAsync($"R{Guid.NewGuid():N}"[..8]);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var item = await db.InventoryItems.SingleAsync(x => x.Id == context.ItemId);
            item.SalesPrice = null;
            await db.SaveChangesAsync();
        }

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WithServiceAsync(service =>
                service.CreateAsync(Input(context), CancellationToken.None)));

        Assert.Contains("satış fiyatı tanımlı değil", error.Message);
    }

    /// <summary>
    /// FİYAT KARTTAN GELİR: istemcinin gönderdiği miktar okunur ama
    /// fiyat okunmaz. 2 adet × 100 TL, %10 iskonto, %20 KDV
    /// => 180 + 36 = 216.
    /// </summary>
    [Fact]
    public async Task PriceComesFromCard_NotFromClient()
    {
        var context = await CreateContextAsync($"R{Guid.NewGuid():N}"[..8]);

        var sale = await WithServiceAsync(service =>
            service.CreateAsync(Input(context, quantity: 2m, discount: 10m), CancellationToken.None));

        Assert.Equal(100m, sale.Items.Single().UnitPrice);
        Assert.Equal(180m, sale.Subtotal);
        Assert.Equal(216m, sale.GrandTotal);
    }
    /// <summary>
    /// FİYAT EKRANINDA MALİYET YETKİYE BAĞLI.
    ///
    /// Maliyeti gören izin `inventory.view` — stok maliyetini bugün
    /// fiilen koruyan anahtar o, yenisi açılmadı. Bu izni kapatılan
    /// kullanıcı fiyat düzenleyebiliyor ama maliyeti göremiyor;
    /// null geliyor ve kaç kalemde gizlendiği bildiriliyor.
    ///
    /// Maskeleme projeksiyon seviyesinde: arayüzde gizlemek yetmez,
    /// uç doğrudan çağrılabiliyor.
    /// </summary>
    [Fact]
    public async Task PricingScreen_HidesCost_WhenInventoryViewDenied()
    {
        var context = await CreateContextAsync($"R{Guid.NewGuid():N}"[..8]);

        using var admin = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var visible = await admin.GetFromJsonAsync<PricingResponse>(
            $"/api/perakende/fiyatlar?search=URN-");

        Assert.NotNull(visible);
        Assert.Equal(0, visible!.HiddenCount);
        Assert.All(visible.Items, item => Assert.NotNull(item.AverageUnitCost));

        using var restricted = await CreateClientWithoutInventoryViewAsync();

        var masked = await restricted.GetFromJsonAsync<PricingResponse>(
            $"/api/perakende/fiyatlar?search=URN-");

        Assert.NotNull(masked);
        Assert.True(masked!.HiddenCount > 0);
        Assert.All(masked.Items, item => Assert.Null(item.AverageUnitCost));

        // Maliyet gizliyken bile fiyat ve tavan görünüyor: ekranın asıl
        // işi o, yalnız marj hesabı yapılamıyor.
        Assert.Contains(masked.Items, item => item.MaxDiscountRate >= 0);
        Assert.NotEqual(Guid.Empty, context.ItemId);
    }

    /// <summary>Toplu güncelleme fiyatı ve tavanı yazıyor.</summary>
    [Fact]
    public async Task PricingUpdate_WritesPriceAndCap()
    {
        var context = await CreateContextAsync($"R{Guid.NewGuid():N}"[..8]);

        using var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PutAsJsonAsync("/api/perakende/fiyatlar", new[]
        {
            new { inventoryItemId = context.ItemId, salesPrice = 250.5m, maxDiscountRate = 15m }
        });

        response.EnsureSuccessStatusCode();

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var card = await db.InventoryItems.SingleAsync(x => x.Id == context.ItemId);

        Assert.Equal(250.5m, card.SalesPrice);
        Assert.Equal(15m, card.MaxDiscountRate);
    }

    /// <summary>Tavan 0-100 dışında kabul edilmiyor.</summary>
    [Fact]
    public async Task PricingUpdate_RejectsCapOutsideRange()
    {
        var context = await CreateContextAsync($"R{Guid.NewGuid():N}"[..8]);

        using var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PutAsJsonAsync("/api/perakende/fiyatlar", new[]
        {
            new { inventoryItemId = context.ItemId, salesPrice = 100m, maxDiscountRate = 140m }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<HttpClient> CreateClientWithoutInventoryViewAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

        const string password = "RetailPricing!2026";
        var username = $"test-pricing-{Guid.NewGuid():N}"[..40];
        var hash = passwordService.Hash(password);

        var user = new AppUser
        {
            Username = username,
            FullName = "Fiyatlandırma Kullanıcısı",
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            IsActive = true,
            WorkHoursExempt = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var role = await db.Roles.SingleAsync(x => x.Name == "Admin");
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        db.UserDataScopes.Add(new UserDataScope { UserId = user.Id, ScopeType = DataScopeType.All });

        // Rol izni verse bile bu kullanıcıya kapatılıyor: maskenin role
        // değil İZNE bağlı olduğunu doğrulamak için.
        var permission = await db.Permissions
            .SingleAsync(x => x.Key == PermissionCatalog.Keys.InventoryView);

        db.UserPermissionOverrides.Add(new UserPermissionOverride
        {
            UserId = user.Id,
            PermissionId = permission.Id,
            Effect = PermissionOverrideEffect.Deny
        });

        await db.SaveChangesAsync();

        var client = fixture.Factory.CreateClient();
        var token = await AuthHelper.LoginAsync(client, username, password);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private sealed record PricingItem(
        Guid Id, string Code, string Name, string Unit,
        decimal? SalesPrice, decimal MaxDiscountRate, decimal? AverageUnitCost);

    private sealed record PricingResponse(List<PricingItem> Items, int HiddenCount);
}
