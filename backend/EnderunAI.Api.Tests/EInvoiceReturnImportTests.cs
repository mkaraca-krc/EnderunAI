using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// E-faturada IADE tanıma.
///
/// Buradaki kritik ayrım şu: mal iadesinde faturayı İADE EDEN taraf
/// keser. Aldığımız malı tedarikçiye geri gönderirken faturayı biz
/// keseriz, yani XML'de satıcı biz görünürüz — ama belge bizim ALIŞ
/// İADEMİZDİR. Yön adına bakılıp satış geliri olarak kaydedilseydi hem
/// gelir hem KDV yanlış beyan edilirdi.
/// </summary>
[Collection("Integration")]
public sealed class EInvoiceReturnImportTests(DatabaseFixture fixture)
{
    private const string SupplierTaxNumber = "1234567890";

    private async Task<(Guid CompanyId, Guid ProjectId, Guid SupplierId)>
        CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var company = await db.Companies.SingleAsync(x => x.Id == project.CompanyId);
        company.TaxNumber = EInvoiceFixtures.OurTaxNumber;

        var supplier = new CurrentAccount
        {
            CompanyId = project.CompanyId,
            Code = $"TED-{suffix}",
            Title = $"AY Global {suffix}",
            TaxNumber = SupplierTaxNumber,
            Roles = CurrentAccountRoles.Supplier,
            Status = CurrentAccountStatus.Approved
        };

        db.CurrentAccounts.Add(supplier);
        await db.SaveChangesAsync();

        return (project.CompanyId, project.Id, supplier.Id);
    }

    private static MultipartFormDataContent BuildUpload(string name, string xml)
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(xml)), "files", name);
        return content;
    }

    private async Task<JsonElement> PreviewAsync(
        HttpClient client, Guid companyId, string name, string xml)
    {
        var response = await client.PostAsync(
            $"/api/e-invoice/import/preview?companyId={companyId}",
            BuildUpload(name, xml));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>
    /// Bizim kestiğimiz IADE faturası (XML'de satıcı biziz) ALIŞ
    /// iademiz olarak tanınmalı ve tedarikçi defterine yazılmalı.
    /// </summary>
    [Fact]
    public async Task Preview_OutgoingReturnInvoice_IsRecognisedAsPurchaseReturn()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, _, _) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var preview = await PreviewAsync(client, companyId, "iade.xml",
            EInvoiceFixtures.PurchaseReturnInvoice(invoiceNumber: $"IADE-{suffix}"));

        var item = preview.GetProperty("items").EnumerateArray().Single();

        Assert.True(item.GetProperty("isReturn").GetBoolean());
        Assert.True(item.GetProperty("canImport").GetBoolean());
        Assert.Equal("Alış iadesi (giden)", item.GetProperty("directionName").GetString());
        Assert.Equal("AYG2026000000456",
            item.GetProperty("referencedInvoiceNumber").GetString());
    }

    /// <summary>
    /// Atıf yapılan orijinal fatura sistemde varsa eşleştirilir; yoksa
    /// null döner ve kullanıcı elle seçer — yanlış faturaya bağlanmaz.
    /// </summary>
    [Fact]
    public async Task Preview_ReturnInvoice_MatchesOriginalByReferencedNumber()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, projectId, supplierId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        Guid originalId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var original = new SupplierInvoice
            {
                CompanyId = companyId,
                SupplierCurrentAccountId = supplierId,
                ProjectId = projectId,
                InternalNumber = $"SFT-{suffix}",
                InvoiceNumber = "AYG2026000000456",
                InvoiceDate = DateTime.UtcNow.Date,
                CurrencyCode = "TRY",
                ExchangeRate = 1m,
                Subtotal = 2_584.80m,
                VatTotal = 516.96m,
                GrandTotal = 3_101.76m,
                Status = SupplierInvoiceStatus.Approved
            };

            db.SupplierInvoices.Add(original);
            await db.SaveChangesAsync();
            originalId = original.Id;
        }

        var preview = await PreviewAsync(client, companyId, "iade.xml",
            EInvoiceFixtures.PurchaseReturnInvoice(invoiceNumber: $"IADE-{suffix}"));

        var item = preview.GetProperty("items").EnumerateArray().Single();

        Assert.Equal(originalId, item.GetProperty("matchedOriginalInvoiceId").GetGuid());
        Assert.Equal("AYG2026000000456",
            item.GetProperty("matchedOriginalInvoiceNumber").GetString());
    }

    /// <summary>
    /// Orijinal fatura sistemde yoksa eşleşme boş kalır ama iade yine de
    /// aktarılabilir: belge gerçek, sistemde karşılığı olmaması onu
    /// geçersiz kılmaz.
    /// </summary>
    [Fact]
    public async Task Preview_ReturnWithoutKnownOriginal_StillImportable()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, _, _) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var preview = await PreviewAsync(client, companyId, "iade.xml",
            EInvoiceFixtures.PurchaseReturnInvoice(
                invoiceNumber: $"IADE-{suffix}",
                referencedInvoiceNumber: "BILINMEYEN-123"));

        var item = preview.GetProperty("items").EnumerateArray().Single();

        Assert.True(item.GetProperty("isReturn").GetBoolean());
        Assert.True(item.GetProperty("canImport").GetBoolean());
        Assert.Equal(JsonValueKind.Null,
            item.GetProperty("matchedOriginalInvoiceId").ValueKind);
    }

    /// <summary>
    /// Kesinleştirmede iade, tedarikçi defterine IsReturn işaretiyle ve
    /// orijinaline bağlı olarak yazılır.
    /// </summary>
    [Fact]
    public async Task Commit_ReturnInvoice_CreatesLinkedSupplierReturn()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, projectId, supplierId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        Guid originalId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var original = new SupplierInvoice
            {
                CompanyId = companyId,
                SupplierCurrentAccountId = supplierId,
                ProjectId = projectId,
                InternalNumber = $"SFT-{suffix}",
                InvoiceNumber = "AYG2026000000456",
                InvoiceDate = DateTime.UtcNow.Date,
                CurrencyCode = "TRY",
                ExchangeRate = 1m,
                Subtotal = 2_584.80m,
                VatTotal = 516.96m,
                GrandTotal = 3_101.76m,
                Status = SupplierInvoiceStatus.Approved
            };

            db.SupplierInvoices.Add(original);
            await db.SaveChangesAsync();
            originalId = original.Id;
        }

        var preview = await PreviewAsync(client, companyId, "iade.xml",
            EInvoiceFixtures.PurchaseReturnInvoice(invoiceNumber: $"IADE-{suffix}"));

        var item = preview.GetProperty("items").EnumerateArray().Single();

        var commit = await client.PostAsJsonAsync(
            $"/api/e-invoice/import/commit?companyId={companyId}",
            new
            {
                items = new[]
                {
                    new
                    {
                        token = item.GetProperty("token").GetString(),
                        currentAccountId = supplierId,
                        createCurrentAccount = false,
                        projectId = (Guid?)projectId,
                        originalInvoiceId = originalId
                    }
                }
            });

        Assert.Equal(HttpStatusCode.OK, commit.StatusCode);

        var result = await commit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, result.GetProperty("createdCount").GetInt32());

        var created = result.GetProperty("created").EnumerateArray().Single();
        Assert.Equal("Alış iadesi (giden)", created.GetProperty("directionName").GetString());

        var invoiceId = created.GetProperty("invoiceId").GetGuid();

        using var verifyScope = fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var returnInvoice = await verifyDb.SupplierInvoices
            .SingleAsync(x => x.Id == invoiceId);

        Assert.True(returnInvoice.IsReturn);
        Assert.Equal(originalId, returnInvoice.OriginalInvoiceId);
        Assert.Equal(supplierId, returnInvoice.SupplierCurrentAccountId);
        Assert.Equal(1_200m, returnInvoice.GrandTotal);
        Assert.StartsWith("AIF-", returnInvoice.InternalNumber);

        // Satış defterine hiçbir şey yazılmamalı.
        Assert.Equal(0, await verifyDb.SalesInvoices.CountAsync(x => x.CompanyId == companyId));
    }

    /// <summary>
    /// Onaylanmamış bir faturaya iade bağlanamaz: tersine çevrilecek
    /// kayıt yoktur.
    /// </summary>
    [Fact]
    public async Task Commit_ReturnLinkedToUnapprovedOriginal_IsSkippedWithReason()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, projectId, supplierId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        Guid originalId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var original = new SupplierInvoice
            {
                CompanyId = companyId,
                SupplierCurrentAccountId = supplierId,
                ProjectId = projectId,
                InternalNumber = $"SFT-{suffix}",
                InvoiceNumber = "AYG2026000000456",
                InvoiceDate = DateTime.UtcNow.Date,
                CurrencyCode = "TRY",
                ExchangeRate = 1m,
                Subtotal = 2_584.80m,
                VatTotal = 516.96m,
                GrandTotal = 3_101.76m,
                Status = SupplierInvoiceStatus.Draft
            };

            db.SupplierInvoices.Add(original);
            await db.SaveChangesAsync();
            originalId = original.Id;
        }

        var preview = await PreviewAsync(client, companyId, "iade.xml",
            EInvoiceFixtures.PurchaseReturnInvoice(invoiceNumber: $"IADE-{suffix}"));

        var token = preview.GetProperty("items").EnumerateArray().Single()
            .GetProperty("token").GetString();

        var commit = await client.PostAsJsonAsync(
            $"/api/e-invoice/import/commit?companyId={companyId}",
            new
            {
                items = new[]
                {
                    new
                    {
                        token,
                        currentAccountId = supplierId,
                        createCurrentAccount = false,
                        projectId = (Guid?)projectId,
                        originalInvoiceId = originalId
                    }
                }
            });

        Assert.Equal(HttpStatusCode.OK, commit.StatusCode);

        var result = await commit.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(0, result.GetProperty("createdCount").GetInt32());
        Assert.Contains("onaylanmamış",
            result.GetProperty("skipped").EnumerateArray().Single()
                .GetProperty("reason").GetString());
    }

    /// <summary>
    /// Normal (SATIS) fatura iade sanılmamalı — yanlış pozitif, alışı
    /// satış iadesine çevirirdi.
    /// </summary>
    [Fact]
    public async Task Preview_NormalInvoice_IsNotMarkedAsReturn()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, _, _) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var preview = await PreviewAsync(client, companyId, "alis.xml",
            EInvoiceFixtures.PurchaseInvoice(invoiceNumber: $"AYG-{suffix}"));

        var item = preview.GetProperty("items").EnumerateArray().Single();

        Assert.False(item.GetProperty("isReturn").GetBoolean());
        Assert.Equal("Gelen (Alış)", item.GetProperty("directionName").GetString());
    }
}
