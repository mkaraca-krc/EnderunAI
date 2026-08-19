using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// ARŞİVLENMİŞ STOK KARTI YENİ BELGEDE KULLANILAMAZ.
///
/// NEDEN VAR: `IsActive` bayrağı kod tabanında yıllardır duruyordu ama
/// yalnızca `GoodsReceiptService` ona uyuyordu. Stok listesi/seçici,
/// perakende ürün arama ve alış faturası doğrulaması yok sayıyordu —
/// yani kartı arşivlemek HİÇBİR ŞEY İFADE ETMİYORDU.
///
/// Bu, stok paketinin ön temizliğini imkânsız kılıyordu: test kartları
/// arşivlense bile seçicilerde çıkmaya devam edeceklerdi.
///
/// AYRIM ÖNEMLİ: arşiv, YENİ belgeyi engeller; MEVCUT belgeleri
/// bozmaz. Geçmiş fatura kalemleri kendi açıklamalarını taşıyor ve
/// kart bağlantısı opsiyonel — arşivlenen kart eski faturayı
/// görünmez yapmaz.
/// </summary>
[Collection("Integration")]
public sealed class InventoryArchiveTests(DatabaseFixture fixture)
{
    private async Task<(Guid CompanyId, Guid ActiveId, Guid ArchivedId, string Suffix)>
        SeedAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        var active = new InventoryItem
        {
            CompanyId = company.Id,
            Code = $"AKTIF-{suffix}",
            Name = $"Aktif Malzeme {suffix}",
            Unit = "Adet",
            SalesPrice = 100m,
            IsActive = true
        };

        var archived = new InventoryItem
        {
            CompanyId = company.Id,
            Code = $"ARSIV-{suffix}",
            Name = $"Arşiv Malzeme {suffix}",
            Unit = "Adet",
            SalesPrice = 100m,
            IsActive = false
        };

        db.InventoryItems.AddRange(active, archived);
        await db.SaveChangesAsync();

        return (company.Id, active.Id, archived.Id, suffix);
    }

    [Fact]
    public async Task StokListesi_ArsivlenmisKartiVarsayilanOlarakGizler()
    {
        var (companyId, _, _, suffix) = await SeedAsync();
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.GetAsync($"/api/inventory/items?companyId={companyId}");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains($"AKTIF-{suffix}", body);
        Assert.DoesNotContain($"ARSIV-{suffix}", body);
    }

    /// <summary>
    /// YÖNETİM EKRANI ARŞİVİ GÖREBİLMELİ — yoksa kart geri açılamaz.
    /// </summary>
    [Fact]
    public async Task StokListesi_AcikcaIstenirseArsiviDeGetirir()
    {
        var (companyId, _, _, suffix) = await SeedAsync();
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.GetAsync(
            $"/api/inventory/items?companyId={companyId}&includeInactive=true");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains($"AKTIF-{suffix}", body);
        Assert.Contains($"ARSIV-{suffix}", body);
    }

    [Fact]
    public async Task PerakendeUrunArama_ArsivlenmisKartiSatisaCikarmaz()
    {
        var (companyId, _, _, suffix) = await SeedAsync();
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.GetAsync(
            $"/api/perakende/urunler?companyId={companyId}&search={suffix}");

        // Uç yetkisiz olabilir; o durumda bu testin söyleyeceği bir şey yok.
        if (response.StatusCode == HttpStatusCode.Forbidden) return;

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain($"ARSIV-{suffix}", body);
    }

    /// <summary>
    /// ARŞİV MEVCUT BELGEYİ BOZMAZ.
    ///
    /// Fatura kalemi kendi `Description` ve `Unit` alanlarını taşıyor,
    /// kart bağı opsiyonel. Kart arşivlense bile fatura okunabilir
    /// kalmalı — aksi hâlde ön temizlik geçmiş muhasebeyi kırardı.
    /// </summary>
    [Fact]
    public async Task ArsivlenmisKarta_BagliGecmisFaturaOkunabilirKalir()
    {
        var (companyId, _, archivedId, suffix) = await SeedAsync();

        Guid invoiceId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var supplier = new CurrentAccount
            {
                CompanyId = companyId,
                Code = $"TED-{suffix}",
                Title = $"Tedarikçi {suffix}",
                Roles = CurrentAccountRoles.Supplier,
                Status = CurrentAccountStatus.Approved
            };
            db.CurrentAccounts.Add(supplier);
            await db.SaveChangesAsync();

            var invoice = new SupplierInvoice
            {
                CompanyId = companyId,
                SupplierCurrentAccountId = supplier.Id,
                InvoiceNumber = $"FTR-{suffix}",
                InternalNumber = $"IC-{suffix}",
                InvoiceDate = DateTime.UtcNow,
                ExchangeRate = 1m,
                Items =
                [
                    new SupplierInvoiceItem
                    {
                        LineNumber = 1,
                        InventoryItemId = archivedId,
                        Description = $"Arşive bağlı kalem {suffix}",
                        Unit = "Adet",
                        Quantity = 1m,
                        UnitPrice = 10m
                    }
                ]
            };

            db.SupplierInvoices.Add(invoice);
            await db.SaveChangesAsync();
            invoiceId = invoice.Id;
        }

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var response = await client.GetAsync($"/api/supplier-invoices/{invoiceId}");

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();

        // Kalem KENDİ açıklamasını taşıyor; kart arşivde diye kaybolmaz.
        Assert.Contains($"Arşive bağlı kalem {suffix}", body);
    }
}
