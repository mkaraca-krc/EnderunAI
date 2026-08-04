using EnderunAI.Api.Services.EInvoice;
using EnderunAI.Api.Services.Hizir;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// AI yedek ayrıştırıcı ve uydurma engeli.
///
/// Bu testlerin asıl konusu şu: modele güvenmiyoruz. Döndürdüğü her
/// değer XML'de aranır; bulunmayan değer alınmaz. Aşağıdaki sahte
/// model bilerek uydurma değerler döndürüyor ve bunların elendiği
/// gösteriliyor.
/// </summary>
public sealed class AiInvoiceParserTests
{
    /// <summary>İstenen JSON'u aynen döndüren sahte model.</summary>
    private sealed class FakeLlm(string? response, bool configured = true)
        : IHizirLlmClient
    {
        public int CallCount { get; private set; }
        public bool IsConfigured => configured;
        public string ModelId => "sahte";

        public Task<LlmCompletion> CompleteAsync(
            string systemPrompt,
            IReadOnlyList<LlmMessage> messages,
            IReadOnlyList<LlmToolDefinition> tools,
            CancellationToken cancellationToken = default)
        {
            CallCount++;

            // Yedek ayrıştırıcı modele ARAÇ VERMEMELİ; verseydi model
            // kendi başına veri çekebilirdi.
            Assert.Empty(tools);

            return Task.FromResult(
                new LlmCompletion(response, [], 0, 0));
        }
    }

    private static AiInvoiceParser Parser(FakeLlm llm) =>
        new(llm, NullLogger<AiInvoiceParser>.Instance);

    // ---------- Doğru okuma ----------

    /// <summary>
    /// Model XML'de gerçekten geçen değerleri döndürürse fatura kabul
    /// edilir ve kaynağı "AI" işaretlenir.
    /// </summary>
    [Fact]
    public async Task TruthfulAiResponse_IsAccepted()
    {
        var xml = EInvoiceFixtures.PurchaseInvoice();

        var llm = new FakeLlm("""
            {
              "invoiceNumber": "AYG2026000000456",
              "issueDate": "2026-03-05",
              "currencyCode": "TRY",
              "supplierTaxNumber": "1234567890",
              "supplierName": "AY GLOBAL ELEKTRIK MALZEMELERI LTD. STI.",
              "customerTaxNumber": "3341211200",
              "customerName": "ENDERUN ELEKTRIK URETIM ENERJI A.S.",
              "lines": [
                {"name":"NYAF Kablo 3x2.5","quantity":100,"unit":"MTR",
                 "unitPrice":18.50,"lineExtensionAmount":1850.00,
                 "vatRate":20,"vatAmount":370.00},
                {"name":"Kofra 12 Modul","quantity":40,"unit":"NIU",
                 "unitPrice":18.37,"lineExtensionAmount":734.80,
                 "vatRate":20,"vatAmount":146.96}
              ],
              "vatTotal": 516.96,
              "withholdingAmount": 0,
              "lineExtensionTotal": 2584.80,
              "taxExclusiveAmount": 2584.80,
              "taxInclusiveAmount": 3101.76,
              "payableAmount": 3101.76
            }
            """);

        var result = await Parser(llm).ParseAsync(xml, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(InvoiceParseSource.Ai, result!.ParseSource);
        Assert.Equal("AYG2026000000456", result.InvoiceNumber);
        Assert.Equal(2, result.Lines.Count);
        Assert.Equal(3_101.76m, result.PayableAmount);
        Assert.Empty(result.Problems);
    }

    // ---------- Uydurma engeli (paketin kritik kuralı) ----------

    /// <summary>
    /// UYDURULMUŞ VKN: model XML'de hiç geçmeyen bir VKN döndürüyor.
    /// Kaynak doğrulaması bunu eler ve alan boş kalır; zorunlu alan
    /// eksik kaldığı için okuma tamamen reddedilir.
    /// </summary>
    [Fact]
    public async Task FabricatedTaxNumber_IsRejected()
    {
        var xml = EInvoiceFixtures.PurchaseInvoice();

        var llm = new FakeLlm("""
            {
              "invoiceNumber": "AYG2026000000456",
              "issueDate": "2026-03-05",
              "supplierTaxNumber": "9999999999",
              "supplierName": "AY GLOBAL ELEKTRIK MALZEMELERI LTD. STI.",
              "customerTaxNumber": "3341211200",
              "lines": [{"name":"NYAF Kablo 3x2.5","quantity":100,
                 "unitPrice":18.50,"lineExtensionAmount":1850.00,
                 "vatRate":20,"vatAmount":370.00}],
              "payableAmount": 3101.76
            }
            """);

        var result = await Parser(llm).ParseAsync(xml, CancellationToken.None);

        // Satıcı VKN'si elendiği için zorunlu alanlar tamamlanamadı.
        Assert.Null(result);
    }

    /// <summary>
    /// UYDURULMUŞ TUTAR: model XML'de geçmeyen bir ödenecek tutar
    /// döndürüyor. Tutar alınmaz, gerekçesi yazılır ve zorunlu alan
    /// eksildiği için okuma reddedilir.
    /// </summary>
    [Fact]
    public async Task FabricatedAmount_IsRejected()
    {
        var xml = EInvoiceFixtures.PurchaseInvoice();

        var llm = new FakeLlm("""
            {
              "invoiceNumber": "AYG2026000000456",
              "issueDate": "2026-03-05",
              "supplierTaxNumber": "1234567890",
              "customerTaxNumber": "3341211200",
              "lines": [{"name":"NYAF Kablo 3x2.5","quantity":100,
                 "unitPrice":18.50,"lineExtensionAmount":1850.00,
                 "vatRate":20,"vatAmount":370.00}],
              "payableAmount": 99999.99
            }
            """);

        var result = await Parser(llm).ParseAsync(xml, CancellationToken.None);

        Assert.Null(result);
    }

    /// <summary>
    /// UYDURULMUŞ KALEM: XML'de olmayan bir kalem eklenirse o satır
    /// atılır, gerçek satırlar kalır.
    /// </summary>
    [Fact]
    public async Task FabricatedLine_IsDroppedButRealLinesRemain()
    {
        var xml = EInvoiceFixtures.PurchaseInvoice();

        var llm = new FakeLlm("""
            {
              "invoiceNumber": "AYG2026000000456",
              "issueDate": "2026-03-05",
              "supplierTaxNumber": "1234567890",
              "customerTaxNumber": "3341211200",
              "lines": [
                {"name":"NYAF Kablo 3x2.5","quantity":100,
                 "unitPrice":18.50,"lineExtensionAmount":1850.00,
                 "vatRate":20,"vatAmount":370.00},
                {"name":"Hic Olmayan Malzeme","quantity":5,
                 "unitPrice":100.00,"lineExtensionAmount":500.00,
                 "vatRate":20,"vatAmount":100.00}
              ],
              "payableAmount": 3101.76
            }
            """);

        var result = await Parser(llm).ParseAsync(xml, CancellationToken.None);

        Assert.NotNull(result);
        var line = Assert.Single(result!.Lines);
        Assert.Equal("NYAF Kablo 3x2.5", line.Name);
        Assert.Contains(result.Problems, x => x.Contains("Hic Olmayan Malzeme"));
    }

    /// <summary>Model JSON dışında bir şey döndürürse okuma başarısız.</summary>
    [Fact]
    public async Task NonJsonResponse_IsRejected()
    {
        var llm = new FakeLlm("Bu faturayı okuyamadım, üzgünüm.");

        Assert.Null(await Parser(llm)
            .ParseAsync(EInvoiceFixtures.PurchaseInvoice(), CancellationToken.None));
    }

    /// <summary>Model yapılandırılmamışsa hiç çağrılmaz.</summary>
    [Fact]
    public async Task UnconfiguredModel_IsNotCalled()
    {
        var llm = new FakeLlm("{}", configured: false);

        Assert.Null(await Parser(llm)
            .ParseAsync(EInvoiceFixtures.PurchaseInvoice(), CancellationToken.None));
        Assert.Equal(0, llm.CallCount);
    }

    // ---------- Kaynak doğrulama yardımcıları ----------

    [Theory]
    [InlineData("3341211200", true)]
    [InlineData("ENDERUN ELEKTRIK URETIM ENERJI A.S.", true)]
    [InlineData("enderun elektrik uretim enerji a.s.", true)]
    [InlineData("9999999999", false)]
    [InlineData("Uydurma Firma Ltd.", false)]
    public void XmlContains_DetectsFabricatedText(string value, bool expected)
    {
        Assert.Equal(expected,
            AiInvoiceParser.XmlContains(EInvoiceFixtures.PurchaseInvoice(), value));
    }

    [Theory]
    [InlineData(3101.76, true)]
    [InlineData(1850.00, true)]
    [InlineData(99999.99, false)]
    // Sıfır "yok" demektir, doğrulanması gerekmez.
    [InlineData(0, true)]
    public void XmlContainsAmount_DetectsFabricatedAmounts(decimal value, bool expected)
    {
        Assert.Equal(expected,
            AiInvoiceParser.XmlContainsAmount(
                EInvoiceFixtures.PurchaseInvoice(), value));
    }
}

/// <summary>
/// İki katmanlı okuyucu: AI yalnızca gerektiğinde çağrılmalı (token
/// maliyeti) ve AI ile okunan fatura her zaman elle kontrole düşmeli.
/// </summary>
public sealed class EInvoiceReaderTests
{
    private sealed class CountingAiParser(ParsedInvoice? result) : IAiInvoiceParser
    {
        public int CallCount { get; private set; }
        public bool IsAvailable => true;

        public Task<ParsedInvoice?> ParseAsync(
            string xml, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private static EInvoiceReader Reader(IAiInvoiceParser ai) =>
        new(ai, NullLogger<EInvoiceReader>.Instance);

    /// <summary>
    /// Standart ayrıştırıcı başarılıysa AI HİÇ çağrılmaz — maliyet
    /// kuralının kendisi.
    /// </summary>
    [Fact]
    public async Task AiIsNotCalled_WhenStandardParserSucceeds()
    {
        var ai = new CountingAiParser(null);

        var result = await Reader(ai).ReadAsync(
            EInvoiceFixtures.PurchaseInvoice(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(InvoiceParseSource.Standard, result.Source);
        Assert.False(result.RequiresManualReview);
        Assert.Equal(0, ai.CallCount);
    }

    /// <summary>Standart ayrıştırıcı yetersizse AI devreye girer.</summary>
    [Fact]
    public async Task AiIsCalled_WhenStandardParserFails()
    {
        var ai = new CountingAiParser(null);

        await Reader(ai).ReadAsync(
            EInvoiceFixtures.WrongDocumentType, CancellationToken.None);

        Assert.Equal(1, ai.CallCount);
    }

    /// <summary>
    /// Tutarsız faturada da AI denenir — standart okuma "başarılı"
    /// görünse bile tutarlar tutmuyorsa güvenilmez.
    /// </summary>
    [Fact]
    public async Task AiIsCalled_WhenTotalsAreInconsistent()
    {
        var ai = new CountingAiParser(null);

        await Reader(ai).ReadAsync(
            EInvoiceFixtures.InconsistentInvoice(), CancellationToken.None);

        Assert.Equal(1, ai.CallCount);
    }

    /// <summary>
    /// AI ile okunan fatura HER ZAMAN elle kontrol işaretiyle döner;
    /// otomatik onaylanamaz.
    /// </summary>
    [Fact]
    public async Task AiParsedInvoice_AlwaysRequiresManualReview()
    {
        var aiInvoice = UblTrInvoiceParser.Parse(EInvoiceFixtures.PurchaseInvoice())
            with
        { ParseSource = InvoiceParseSource.Ai };

        var ai = new CountingAiParser(aiInvoice);

        var result = await Reader(ai).ReadAsync(
            EInvoiceFixtures.WrongDocumentType, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(InvoiceParseSource.Ai, result.Source);
        Assert.True(result.RequiresManualReview);
    }
}
