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

        // S5: satış artık 621 maliyet + 150/153 stok çıkışı da yazıyor.
        // O hesaplar olmadan fiş kesilemez ve satış tamamlanamaz —
        // bilinçli davranış: mal muhasebesiz çıkmasın.
        await TestDataFactory.EnsureStockAccountsAsync(db, companyId);

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

    private sealed record CreatedSale(Guid Id, string DocumentNumber, decimal GrandTotal);

    private sealed record PricingItem(
        Guid Id, string Code, string Name, string Unit,
        decimal? SalesPrice, decimal MaxDiscountRate, decimal? AverageUnitCost);

    private sealed record PricingResponse(List<PricingItem> Items, int HiddenCount);
    /// <summary>Tamamlanmış satışı hazırlayan yardımcı.</summary>
    private async Task<Guid> CompleteSaleAsync(Context context, decimal quantity = 4m)
    {
        return await WithServiceAsync(async service =>
        {
            var created = await service.CreateAsync(
                Input(context, quantity: quantity), CancellationToken.None);

            await service.SubmitAsync(created.Id, CancellationToken.None);
            return created.Id;
        });
    }

    /// <summary>
    /// İPTAL: stok geri döner, fatura ters kayıt alır, tahsilat karşıt
    /// hareketle kapanır — ve hepsi BİRER KEZ.
    /// </summary>
    [Fact]
    public async Task Cancel_ReturnsStock_AndReversesInvoiceAndCash()
    {
        var context = await CreateContextAsync($"R{Guid.NewGuid():N}"[..8]);
        var saleId = await CompleteSaleAsync(context);

        await WithServiceAsync(service =>
            service.CancelAsync(saleId, "Müşteri vazgeçti", CancellationToken.None));

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sale = await db.RetailSales.SingleAsync(x => x.Id == saleId);
        Assert.Equal(RetailSaleStatus.Cancelled, sale.Status);
        Assert.Equal("Müşteri vazgeçti", sale.DecisionReason);

        // Stok başa döndü.
        var stock = await db.WarehouseStocks.SingleAsync(
            x => x.WarehouseId == context.WarehouseId && x.InventoryItemId == context.ItemId);
        Assert.Equal(100m, stock.Quantity);

        // Çıkış ve iade hareketi BİRER tane — çift ters kayıt yok.
        Assert.Equal(1, await db.StockMovements.CountAsync(
            x => x.InventoryItemId == context.ItemId && x.Type == StockMovementType.Issue));
        Assert.Equal(1, await db.StockMovements.CountAsync(
            x => x.InventoryItemId == context.ItemId && x.Type == StockMovementType.Return));

        var invoice = await db.SalesInvoices.SingleAsync(x => x.Id == sale.SalesInvoiceId);
        Assert.Equal(SalesInvoiceStatus.Cancelled, invoice.Status);
        Assert.NotNull(invoice.ReversalVoucherId);

        Assert.Equal(1, await db.CashTransactions.CountAsync(
            x => x.SourceModule == "RETAIL_SALE_CANCEL" && x.SourceEntityId == saleId));
    }

    /// <summary>İptal gerekçesi zorunlu.</summary>
    [Fact]
    public async Task Cancel_RequiresReason()
    {
        var context = await CreateContextAsync($"R{Guid.NewGuid():N}"[..8]);
        var saleId = await CompleteSaleAsync(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WithServiceAsync(service => service.CancelAsync(saleId, "  ", CancellationToken.None)));
    }

    /// <summary>İkinci iptal reddedilir — ters kayıt iki kez üretilmez.</summary>
    [Fact]
    public async Task Cancel_Twice_IsRejected()
    {
        var context = await CreateContextAsync($"R{Guid.NewGuid():N}"[..8]);
        var saleId = await CompleteSaleAsync(context);

        await WithServiceAsync(service =>
            service.CancelAsync(saleId, "Birinci iptal", CancellationToken.None));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WithServiceAsync(service =>
                service.CancelAsync(saleId, "İkinci iptal", CancellationToken.None)));
    }

    /// <summary>
    /// KISMİ İADE doğru miktarı döndürür ve FİNANS ONAYINA bağlıdır:
    /// onaydan önce stok değişmez.
    /// </summary>
    /// <summary>
    /// İADEDE SATIŞTAKİ MALİYET KULLANILIR, bugünkü ortalama değil.
    ///
    /// Satıştan sonra araya pahalı bir alım girip ortalamayı
    /// yükseltirse, aynı malın iadesi güncel ortalamayla işlenirse
    /// depoya çıktığından PAHALI mal geri girer: stok değeri şişer,
    /// muhasebeye yazılan 621 tutarıyla tutmaz ve mutabakat raporu
    /// her iadede biraz daha sapar.
    ///
    /// BU KURAL SONDADA KAÇIRILDI: dondurulmuş maliyeti yoksayıp
    /// güncel ortalamaya döndüğümde 22 testin hiçbiri düşmedi —
    /// kuralın hiç kapsaması yoktu. Bu test o boşluğu kapatıyor.
    /// </summary>
    [Fact]
    public async Task Return_UsesTheCostFrozenAtSale_NotTodaysAverage()
    {
        var context = await CreateContextAsync($"F{Guid.NewGuid():N}"[..8]);

        // Kurulumda kartın ortalaması 60.
        var saleId = await CompleteSaleAsync(context, quantity: 10m);

        Guid returnItemId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var soldLine = await db.RetailSaleItems
                .SingleAsync(x => x.RetailSaleId == saleId);

            // Satışta 60 donduruldu.
            Assert.Equal(60m, soldLine.UnitCostAtSale);
            returnItemId = soldLine.Id;

            // Araya pahalı bir alım giriyor: ortalama 60 -> 200.
            var card = await db.InventoryItems.SingleAsync(x => x.Id == context.ItemId);
            card.AverageUnitCost = 200m;
            await db.SaveChangesAsync();
        }

        var retur = await WithServiceAsync(service => service.CreateReturnAsync(
            saleId, [new RetailReturnLineInput(returnItemId, 4m)], "Ürün kusurlu",
            CancellationToken.None));

        await WithServiceAsync(service => service.ApproveAsync(retur.Id, CancellationToken.None));

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var returnMovement = await db.StockMovements
                .Where(x => x.InventoryItemId == context.ItemId
                    && x.Type == StockMovementType.Return)
                .SingleAsync();

            // 200 DEĞİL 60: mal satıldığı maliyetle geri giriyor.
            Assert.Equal(60m, returnMovement.UnitCost);
            Assert.Equal(240m, returnMovement.TotalCost);
        }
    }

    [Fact]
    public async Task PartialReturn_NeedsApproval_ThenRestoresExactQuantity()
    {
        var context = await CreateContextAsync($"R{Guid.NewGuid():N}"[..8]);
        var saleId = await CompleteSaleAsync(context, quantity: 10m);

        Guid returnId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var itemId = await db.RetailSaleItems
                .Where(x => x.RetailSaleId == saleId).Select(x => x.Id).SingleAsync();

            var retur = await WithServiceAsync(service => service.CreateReturnAsync(
                saleId, [new RetailReturnLineInput(itemId, 3m)], "Ürün kusurlu",
                CancellationToken.None));

            returnId = retur.Id;
            Assert.Equal(RetailSaleStatus.PendingApproval, retur.Status);
        }

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // ONAYDAN ÖNCE STOK DEĞİŞMEDİ: 100 − 10 satılan.
            var before = await db.WarehouseStocks.SingleAsync(
                x => x.WarehouseId == context.WarehouseId && x.InventoryItemId == context.ItemId);
            Assert.Equal(90m, before.Quantity);
        }

        await WithServiceAsync(service => service.ApproveAsync(returnId, CancellationToken.None));

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Onaydan sonra tam 3 adet döndü.
            var after = await db.WarehouseStocks.SingleAsync(
                x => x.WarehouseId == context.WarehouseId && x.InventoryItemId == context.ItemId);
            Assert.Equal(93m, after.Quantity);

            var retur = await db.RetailSales.SingleAsync(x => x.Id == returnId);
            Assert.Equal(RetailSaleStatus.Completed, retur.Status);
            Assert.True(retur.IsReturn);
            Assert.Equal(saleId, retur.OriginalSaleId);
        }
    }

    /// <summary>
    /// FAZLA İADE ENGELLENİR: satılandan çok iade edilirse stok yoktan
    /// var edilirdi.
    /// </summary>
    [Fact]
    public async Task Return_CannotExceedSoldQuantity()
    {
        var context = await CreateContextAsync($"R{Guid.NewGuid():N}"[..8]);
        var saleId = await CompleteSaleAsync(context, quantity: 5m);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var itemId = await db.RetailSaleItems
            .Where(x => x.RetailSaleId == saleId).Select(x => x.Id).SingleAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WithServiceAsync(service => service.CreateReturnAsync(
                saleId, [new RetailReturnLineInput(itemId, 6m)], "Fazla iade denemesi",
                CancellationToken.None)));

        Assert.Contains("en fazla", error.Message);
    }

    /// <summary>
    /// ELDEN İÇEREN SATIŞIN İADESİNDE mal TAM döner, elden kısım
    /// orantılı geri alınır ve resmî kasadan ÇIKMAZ — oraya hiç
    /// girmemişti.
    /// </summary>
    [Fact]
    public async Task ReturnOfCashSale_RestoresStockFully_AndKeepsCashIsolated()
    {
        var context = await CreateContextAsync($"R{Guid.NewGuid():N}"[..8]);

        // 10 adet × 100 TL + %20 KDV = 1200; 400'ü elden.
        //
        // BU AKIŞ HTTP ÜZERİNDEN: elden işaretleme sales.cash izni
        // istiyor ve servis doğrudan çağrıldığında oturum yok, izin de
        // yok. Bu doğru davranış — testin ona uyması gerekiyor.
        using var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var createResponse = await client.PostAsJsonAsync("/api/perakende", new
        {
            companyId = context.CompanyId,
            warehouseId = context.WarehouseId,
            saleDate = DateTime.UtcNow.Date,
            customerCurrentAccountId = context.CustomerId,
            paymentMethod = 0,
            overallDiscountRate = 0m,
            cashAmount = 400m,
            cashAccountId = context.CashAccountId,
            items = new[]
            {
                new { inventoryItemId = context.ItemId, quantity = 10m, discountRate = 0m }
            }
        });

        createResponse.EnsureSuccessStatusCode();

        var created = await createResponse.Content.ReadFromJsonAsync<CreatedSale>();
        var saleId = created!.Id;

        (await client.PostAsync($"/api/perakende/{saleId}/gonder", null))
            .EnsureSuccessStatusCode();

        Guid returnId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sale = await db.RetailSales.SingleAsync(x => x.Id == saleId);

            Assert.Equal(400m, sale.CashAmount);
            Assert.Equal(sale.GrandTotal - 400m, sale.RecordedAmount);

            var itemId = await db.RetailSaleItems
                .Where(x => x.RetailSaleId == saleId).Select(x => x.Id).SingleAsync();

            var retur = await WithServiceAsync(service => service.CreateReturnAsync(
                saleId, [new RetailReturnLineInput(itemId, 10m)], "Tam iade",
                CancellationToken.None));

            returnId = retur.Id;

            // Tam iadede elden kısım da tamamen geri alınıyor.
            Assert.Equal(400m, retur.CashAmount);
        }

        await WithServiceAsync(service => service.ApproveAsync(returnId, CancellationToken.None));

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // MAL TAM DÖNDÜ.
            var stock = await db.WarehouseStocks.SingleAsync(
                x => x.WarehouseId == context.WarehouseId && x.InventoryItemId == context.ItemId);
            Assert.Equal(100m, stock.Quantity);

            // Kasadan çıkan tutar YALNIZ kayıtlı kısım; elden dahil değil.
            var refund = await db.CashTransactions
                .SingleAsync(x => x.SourceModule == "RETAIL_RETURN" && x.SourceEntityId == returnId);

            var retur = await db.RetailSales.SingleAsync(x => x.Id == returnId);
            Assert.Equal(retur.RecordedAmount, refund.Amount);
            Assert.NotEqual(retur.GrandTotal, refund.Amount);
        }
    }
    /// <summary>
    /// GÜN SONU KASA doğru okuyor: peşin satış nakde, iade nakitten
    /// DÜŞÜYOR — ayrı bir çıkarma yapılmadan, karşıt kasa hareketi
    /// aynı sorguya girdiği için.
    /// </summary>
    [Fact]
    public async Task DayEndReport_ReadsCashAndNetsOutReturns()
    {
        var context = await CreateContextAsync($"R{Guid.NewGuid():N}"[..8]);
        var saleId = await CompleteSaleAsync(context, quantity: 5m);

        using var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var before = await client.GetFromJsonAsync<DayEndReport>(
            $"/api/perakende/raporlar/gun-sonu?companyId={context.CompanyId}&date={DateTime.UtcNow:yyyy-MM-dd}");

        Assert.NotNull(before);
        // 5 × 100 + %20 KDV = 600
        Assert.Equal(600m, before!.Cash);
        Assert.Equal(1, before.SaleCount);

        Guid returnId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var itemId = await db.RetailSaleItems
                .Where(x => x.RetailSaleId == saleId).Select(x => x.Id).SingleAsync();

            var retur = await WithServiceAsync(service => service.CreateReturnAsync(
                saleId, [new RetailReturnLineInput(itemId, 2m)], "Kısmi iade",
                CancellationToken.None));

            returnId = retur.Id;
        }

        await WithServiceAsync(service => service.ApproveAsync(returnId, CancellationToken.None));

        var after = await client.GetFromJsonAsync<DayEndReport>(
            $"/api/perakende/raporlar/gun-sonu?companyId={context.CompanyId}&date={DateTime.UtcNow:yyyy-MM-dd}");

        // 2 adet iade = 240 geri çıktı.
        Assert.Equal(360m, after!.Cash);
        Assert.Equal(1, after.ReturnCount);
    }

    /// <summary>
    /// ELDEN TUTAR KAYITLI TOPLAMA KARIŞMIYOR — ve maskeli.
    ///
    /// Karışsaydı resmî ciro ile kasa dökümü birbirini tutmaz, elden
    /// para da resmî rapora sızmış olurdu.
    /// </summary>
    [Fact]
    public async Task DayEndReport_KeepsOffBookSeparate_AndMasked()
    {
        var context = await CreateContextAsync($"R{Guid.NewGuid():N}"[..8]);

        using var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var createResponse = await client.PostAsJsonAsync("/api/perakende", new
        {
            companyId = context.CompanyId,
            warehouseId = context.WarehouseId,
            saleDate = DateTime.UtcNow.Date,
            customerCurrentAccountId = context.CustomerId,
            paymentMethod = 0,
            overallDiscountRate = 0m,
            cashAmount = 200m,
            cashAccountId = context.CashAccountId,
            items = new[]
            {
                new { inventoryItemId = context.ItemId, quantity = 5m, discountRate = 0m }
            }
        });

        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedSale>();

        (await client.PostAsync($"/api/perakende/{created!.Id}/gonder", null))
            .EnsureSuccessStatusCode();

        var report = await client.GetFromJsonAsync<DayEndReport>(
            $"/api/perakende/raporlar/gun-sonu?companyId={context.CompanyId}&date={DateTime.UtcNow:yyyy-MM-dd}");

        Assert.NotNull(report);

        // Kasaya giren YALNIZ kayıtlı kısım: 600 − 200 = 400.
        Assert.Equal(400m, report!.Cash);
        Assert.Equal(200m, report.CashAmount);

        // Kayıtlı toplam elden içermiyor.
        Assert.Equal(400m, report.RecordedTotal);

        // Yetkisiz kullanıcıda elden null ve gizlenen sayı dolu.
        using var restricted = await CreateClientWithoutExtraPaymentAsync();

        var masked = await restricted.GetFromJsonAsync<DayEndReport>(
            $"/api/perakende/raporlar/gun-sonu?companyId={context.CompanyId}&date={DateTime.UtcNow:yyyy-MM-dd}");

        Assert.Null(masked!.CashAmount);
        Assert.True(masked.HiddenCount > 0);
        // Kayıtlı rakamlar maskeden etkilenmiyor.
        Assert.Equal(400m, masked.Cash);
    }

    /// <summary>Açık vade raporu tahsil edilmemiş bakiyeyi FATURADAN okuyor.</summary>
    [Fact]
    public async Task OpenReceivablesReport_ReadsInvoiceBalance()
    {
        var context = await CreateContextAsync($"R{Guid.NewGuid():N}"[..8]);

        var saleId = await WithServiceAsync(async service =>
        {
            var created = await service.CreateAsync(
                Input(context, quantity: 3m,
                    method: RetailPaymentMethod.Term,
                    dueDate: DateTime.UtcNow.Date.AddDays(30)),
                CancellationToken.None);

            await service.SubmitAsync(created.Id, CancellationToken.None);
            await service.ApproveAsync(created.Id, CancellationToken.None);
            return created.Id;
        });

        using var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var rows = await client.GetFromJsonAsync<List<OpenReceivableRow>>(
            $"/api/perakende/raporlar/acik-vade?companyId={context.CompanyId}");

        var row = Assert.Single(rows!, x => x.Id == saleId);

        // 3 × 100 + %20 = 360, hiç tahsilat yok.
        Assert.Equal(360m, row.Remaining);
        Assert.False(row.IsOverdue);
    }

    private async Task<HttpClient> CreateClientWithoutExtraPaymentAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

        const string password = "RetailReport!2026";
        var username = $"test-report-{Guid.NewGuid():N}"[..40];
        var hash = passwordService.Hash(password);

        var user = new AppUser
        {
            Username = username,
            FullName = "Rapor Kullanıcısı",
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

        var permission = await db.Permissions
            .SingleAsync(x => x.Key == PermissionCatalog.Keys.ExtraPaymentView);

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

    private sealed record DayEndReport(
        DateTime Date, decimal Cash, decimal Card, decimal Cheque, decimal Term,
        decimal RecordedTotal, decimal? CashAmount, int HiddenCount,
        int SaleCount, int ReturnCount);

    private sealed record OpenReceivableRow(
        Guid Id, string DocumentNumber, DateTime SaleDate, DateTime? DueDate,
        int PaymentMethod, string? CustomerTitle, decimal Remaining, bool IsOverdue);
}
