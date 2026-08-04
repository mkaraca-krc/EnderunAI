using System.IO.Compression;
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
/// E-fatura içe aktarma uçtan uca: yön tespiti (VKN), cari eşleştirme,
/// mükerrer engeli, hata izolasyonu ve toplu (ZIP) yükleme.
/// </summary>
[Collection("Integration")]
public sealed class EInvoiceImportTests(DatabaseFixture fixture)
{
    private const string SupplierTaxNumber = "1234567890";
    private const string CustomerTaxNumber = "7710035506";

    private async Task<(Guid CompanyId, Guid ProjectId)> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        // Yön tespiti şirketin VKN'sine dayanır.
        var company = await db.Companies.SingleAsync(x => x.Id == project.CompanyId);
        company.TaxNumber = EInvoiceFixtures.OurTaxNumber;

        db.AccountingAccounts.AddRange(
            new AccountingAccount
            {
                CompanyId = company.Id, Code = "120", Name = "Alıcılar",
                Nature = AccountingAccountNature.Debit, Level = 3, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = company.Id, Code = "600.03", Name = "% 20 KDV Lİ SATIŞLAR",
                Nature = AccountingAccountNature.Credit, Level = 4, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = company.Id, Code = "391.09", Name = "% 20 HESAPLANAN KDV",
                Nature = AccountingAccountNature.Credit, Level = 4, IsPostingAllowed = true
            });

        await db.SaveChangesAsync();

        return (project.CompanyId, project.Id);
    }

    private async Task<Guid> CreateCurrentAccountAsync(
        Guid companyId, string suffix, string taxNumber, CurrentAccountRoles roles)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var account = new CurrentAccount
        {
            CompanyId = companyId,
            Code = $"CR-{suffix}",
            Title = $"Test Cari {suffix}",
            TaxNumber = taxNumber,
            Roles = roles,
            Status = CurrentAccountStatus.Approved
        };

        db.CurrentAccounts.Add(account);
        await db.SaveChangesAsync();

        return account.Id;
    }

    private static MultipartFormDataContent BuildUpload(params (string Name, string Xml)[] files)
    {
        var content = new MultipartFormDataContent();

        foreach (var (name, xml) in files)
        {
            var part = new ByteArrayContent(Encoding.UTF8.GetBytes(xml));
            content.Add(part, "files", name);
        }

        return content;
    }

    private static MultipartFormDataContent BuildZipUpload(
        string zipName, params (string Name, string Xml)[] files)
    {
        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, xml) in files)
            {
                var entry = archive.CreateEntry(name);
                using var stream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(xml);
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(buffer.ToArray()), "files", zipName);

        return content;
    }

    private async Task<JsonElement> PreviewAsync(
        HttpClient client, Guid companyId, MultipartFormDataContent upload)
    {
        var response = await client.PostAsync(
            $"/api/e-invoice/import/preview?companyId={companyId}", upload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Preview_IncomingInvoice_IsRoutedToPurchaseAndMatchesSupplier()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, _) = await CreateContextAsync(suffix);
        var supplierId = await CreateCurrentAccountAsync(
            companyId, suffix, SupplierTaxNumber, CurrentAccountRoles.Supplier);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var preview = await PreviewAsync(client, companyId,
            BuildUpload(("alis.xml", EInvoiceFixtures.PurchaseInvoice())));

        var item = preview.GetProperty("items").EnumerateArray().Single();

        // InvoiceTypeCode "SATIS" yazsa da alıcı biziz → ALIŞ.
        Assert.Equal(1, item.GetProperty("direction").GetInt32());
        Assert.True(item.GetProperty("canImport").GetBoolean());
        Assert.Equal(supplierId, item.GetProperty("matchedCurrentAccountId").GetGuid());
        Assert.Equal(3_101.76m, item.GetProperty("grandTotal").GetDecimal());
        Assert.Equal(2, item.GetProperty("lines").GetArrayLength());
        Assert.Equal("Standart", item.GetProperty("parseSourceName").GetString());
        Assert.False(item.GetProperty("requiresManualReview").GetBoolean());
    }

    [Fact]
    public async Task Preview_OutgoingInvoice_IsRoutedToSales()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, _) = await CreateContextAsync(suffix);
        await CreateCurrentAccountAsync(
            companyId, suffix, CustomerTaxNumber, CurrentAccountRoles.Customer);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var preview = await PreviewAsync(client, companyId,
            BuildUpload(("satis.xml", EInvoiceFixtures.SalesInvoice())));

        var item = preview.GetProperty("items").EnumerateArray().Single();

        Assert.Equal(2, item.GetProperty("direction").GetInt32());
        Assert.True(item.GetProperty("canImport").GetBoolean());
        Assert.Equal(CustomerTaxNumber,
            item.GetProperty("counterpartyTaxNumber").GetString());
        Assert.Equal(68_443.20m, item.GetProperty("grandTotal").GetDecimal());
    }

    [Fact]
    public async Task Preview_ThirdPartyInvoice_IsSkippedWithReason()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, _) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var preview = await PreviewAsync(client, companyId,
            BuildUpload(("yabanci.xml", EInvoiceFixtures.ForeignInvoice())));

        var item = preview.GetProperty("items").EnumerateArray().Single();

        Assert.Equal(0, item.GetProperty("direction").GetInt32());
        Assert.False(item.GetProperty("canImport").GetBoolean());
        Assert.Equal(1, preview.GetProperty("skippedCount").GetInt32());
        Assert.Contains("şirketinize ait değil",
            string.Join(" ", item.GetProperty("problems").EnumerateArray()
                .Select(x => x.GetString())));
    }

    [Fact]
    public async Task Preview_BrokenFile_DoesNotStopOtherFiles()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, _) = await CreateContextAsync(suffix);
        await CreateCurrentAccountAsync(
            companyId, suffix, SupplierTaxNumber, CurrentAccountRoles.Supplier);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var preview = await PreviewAsync(client, companyId, BuildUpload(
            ("bozuk.xml", EInvoiceFixtures.BrokenXml),
            ("yanlis-belge.xml", EInvoiceFixtures.WrongDocumentType),
            ("saglam.xml", EInvoiceFixtures.PurchaseInvoice())));

        Assert.Equal(3, preview.GetProperty("totalFiles").GetInt32());
        Assert.Equal(1, preview.GetProperty("readableCount").GetInt32());
        Assert.Equal(2, preview.GetProperty("skippedCount").GetInt32());

        // Atlanan her dosya için sebep yazmalı.
        foreach (var skipped in preview.GetProperty("skipped").EnumerateArray())
            Assert.False(string.IsNullOrWhiteSpace(skipped.GetProperty("reason").GetString()));
    }

    [Fact]
    public async Task Preview_ZipUpload_ExpandsAllInvoices()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, _) = await CreateContextAsync(suffix);
        await CreateCurrentAccountAsync(
            companyId, suffix, SupplierTaxNumber, CurrentAccountRoles.Supplier);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var preview = await PreviewAsync(client, companyId, BuildZipUpload("paket.zip",
            ("fatura-1.xml", EInvoiceFixtures.PurchaseInvoice(invoiceNumber: $"AYG{suffix}1")),
            ("fatura-2.xml", EInvoiceFixtures.PurchaseInvoice(invoiceNumber: $"AYG{suffix}2"))));

        Assert.Equal(2, preview.GetProperty("totalFiles").GetInt32());
        Assert.Equal(2, preview.GetProperty("readableCount").GetInt32());
    }

    [Fact]
    public async Task Commit_IncomingInvoice_CreatesDraftSupplierInvoiceWithSourceXml()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, projectId) = await CreateContextAsync(suffix);
        var supplierId = await CreateCurrentAccountAsync(
            companyId, suffix, SupplierTaxNumber, CurrentAccountRoles.Supplier);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var invoiceNumber = $"AYG{suffix}";
        var preview = await PreviewAsync(client, companyId,
            BuildUpload(("alis.xml", EInvoiceFixtures.PurchaseInvoice(invoiceNumber))));

        var token = preview.GetProperty("items").EnumerateArray()
            .Single().GetProperty("token").GetString();

        var commit = await client.PostAsJsonAsync(
            $"/api/e-invoice/import/commit?companyId={companyId}",
            new
            {
                items = new[]
                {
                    new { token, currentAccountId = supplierId, createCurrentAccount = false, projectId }
                }
            });

        Assert.Equal(HttpStatusCode.OK, commit.StatusCode);
        var result = await commit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, result.GetProperty("createdCount").GetInt32());

        var invoiceId = result.GetProperty("created").EnumerateArray()
            .Single().GetProperty("invoiceId").GetGuid();

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var invoice = await db.SupplierInvoices.AsNoTracking()
            .Include(x => x.Items)
            .SingleAsync(x => x.Id == invoiceId);

        Assert.Equal(SupplierInvoiceStatus.Draft, invoice.Status);
        Assert.Equal(invoiceNumber, invoice.InvoiceNumber);
        Assert.Equal(supplierId, invoice.SupplierCurrentAccountId);
        Assert.Equal(projectId, invoice.ProjectId);
        Assert.Equal(2_584.80m, invoice.Subtotal);
        Assert.Equal(3_101.76m, invoice.GrandTotal);
        Assert.Equal(2, invoice.Items.Count);
        Assert.Equal(EInvoiceParseSource.Standard, invoice.ParseSource);
        // Orijinal XML denetim izi için saklanmalı.
        Assert.False(string.IsNullOrWhiteSpace(invoice.SourceXmlPath));
    }

    [Fact]
    public async Task Commit_OutgoingInvoice_CreatesDraftSalesInvoice()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, projectId) = await CreateContextAsync(suffix);
        var customerId = await CreateCurrentAccountAsync(
            companyId, suffix, CustomerTaxNumber, CurrentAccountRoles.Customer);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var invoiceNumber = $"ENE{suffix}";
        var preview = await PreviewAsync(client, companyId,
            BuildUpload(("satis.xml", EInvoiceFixtures.SalesInvoice(invoiceNumber))));

        var token = preview.GetProperty("items").EnumerateArray()
            .Single().GetProperty("token").GetString();

        var commit = await client.PostAsJsonAsync(
            $"/api/e-invoice/import/commit?companyId={companyId}",
            new
            {
                items = new[]
                {
                    new { token, currentAccountId = customerId, createCurrentAccount = false, projectId }
                }
            });

        Assert.Equal(HttpStatusCode.OK, commit.StatusCode);

        var invoiceId = (await commit.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("created").EnumerateArray().Single()
            .GetProperty("invoiceId").GetGuid();

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var invoice = await db.SalesInvoices.AsNoTracking()
            .SingleAsync(x => x.Id == invoiceId);

        Assert.Equal(SalesInvoiceStatus.Draft, invoice.Status);
        Assert.Equal(invoiceNumber, invoice.OfficialInvoiceNumber);
        Assert.StartsWith("SAT-", invoice.InternalNumber);
        Assert.Equal(customerId, invoice.CustomerCurrentAccountId);
        Assert.Equal(68_443.20m, invoice.GrandTotal);
    }

    [Fact]
    public async Task Commit_CreatesNewCurrentAccountFromXmlWhenRequested()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, projectId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var newSupplierVkn = "9998887770";
        var preview = await PreviewAsync(client, companyId,
            BuildUpload(("alis.xml", EInvoiceFixtures.PurchaseInvoice(
                invoiceNumber: $"AYG{suffix}", supplierTaxNumber: newSupplierVkn))));

        var item = preview.GetProperty("items").EnumerateArray().Single();

        // Eşleşen cari yok; kullanıcıya "yeni cari oluştur" önerilir.
        Assert.Equal(JsonValueKind.Null, item.GetProperty("matchedCurrentAccountId").ValueKind);

        var commit = await client.PostAsJsonAsync(
            $"/api/e-invoice/import/commit?companyId={companyId}",
            new
            {
                items = new[]
                {
                    new
                    {
                        token = item.GetProperty("token").GetString(),
                        currentAccountId = (Guid?)null,
                        createCurrentAccount = true,
                        projectId
                    }
                }
            });

        Assert.Equal(HttpStatusCode.OK, commit.StatusCode);

        var created = (await commit.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("created").EnumerateArray().Single();

        Assert.True(created.GetProperty("currentAccountCreated").GetBoolean());

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var account = await db.CurrentAccounts.AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId && x.TaxNumber == newSupplierVkn);

        // Unvan XML'den gelir, cari taslak açılır (muhasebe kartı sonra tamamlanır).
        Assert.Contains("AY GLOBAL", account.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(CurrentAccountStatus.Draft, account.Status);
        Assert.True(account.Roles.HasFlag(CurrentAccountRoles.Supplier));
    }

    [Fact]
    public async Task Commit_SameInvoiceTwice_IsBlockedAsDuplicate()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, projectId) = await CreateContextAsync(suffix);
        var supplierId = await CreateCurrentAccountAsync(
            companyId, suffix, SupplierTaxNumber, CurrentAccountRoles.Supplier);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var xml = EInvoiceFixtures.PurchaseInvoice(invoiceNumber: $"AYG{suffix}");

        var first = await PreviewAsync(client, companyId, BuildUpload(("alis.xml", xml)));

        var commitBody = new
        {
            items = new[]
            {
                new
                {
                    token = first.GetProperty("items").EnumerateArray()
                        .Single().GetProperty("token").GetString(),
                    currentAccountId = supplierId,
                    createCurrentAccount = false,
                    projectId
                }
            }
        };

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync(
                $"/api/e-invoice/import/commit?companyId={companyId}", commitBody)).StatusCode);

        // Aynı VKN + fatura no ikinci kez: önizlemede uyarı, aktarım kapalı.
        var second = await PreviewAsync(client, companyId, BuildUpload(("alis.xml", xml)));
        var item = second.GetProperty("items").EnumerateArray().Single();

        Assert.False(item.GetProperty("canImport").GetBoolean());
        Assert.Equal(JsonValueKind.String, item.GetProperty("duplicateOfId").ValueKind);
        Assert.Contains("daha önce içe aktarılmış",
            string.Join(" ", item.GetProperty("problems").EnumerateArray()
                .Select(x => x.GetString())));

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(1, await db.SupplierInvoices
            .CountAsync(x => x.CompanyId == companyId));
    }

    [Fact]
    public async Task Commit_PurchaseWithoutProject_IsSkippedWithReason()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, _) = await CreateContextAsync(suffix);
        var supplierId = await CreateCurrentAccountAsync(
            companyId, suffix, SupplierTaxNumber, CurrentAccountRoles.Supplier);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var preview = await PreviewAsync(client, companyId,
            BuildUpload(("alis.xml", EInvoiceFixtures.PurchaseInvoice(
                invoiceNumber: $"AYG{suffix}"))));

        var commit = await client.PostAsJsonAsync(
            $"/api/e-invoice/import/commit?companyId={companyId}",
            new
            {
                items = new[]
                {
                    new
                    {
                        token = preview.GetProperty("items").EnumerateArray()
                            .Single().GetProperty("token").GetString(),
                        currentAccountId = supplierId,
                        createCurrentAccount = false,
                        projectId = (Guid?)null
                    }
                }
            });

        Assert.Equal(HttpStatusCode.OK, commit.StatusCode);

        var result = await commit.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(0, result.GetProperty("createdCount").GetInt32());
        Assert.Contains("proje seçimi zorunludur",
            result.GetProperty("skipped").EnumerateArray()
                .Single().GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Preview_WithoutCompanyTaxNumber_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        // VKN yoksa yön belirlenemez; sessizce yanlış tarafa yazmaktansa dur.
        var response = await client.PostAsync(
            $"/api/e-invoice/import/preview?companyId={project.CompanyId}",
            BuildUpload(("alis.xml", EInvoiceFixtures.PurchaseInvoice())));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
