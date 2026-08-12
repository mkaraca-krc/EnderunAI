using ClosedXML.Excel;
using EnderunAI.Api.Services.Hakedis;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Kısım adlarının özet (icmal) sayfasından alias olarak okunması.
///
/// GERÇEK DURUM: NATURA icmalinde detay ve özet sayfaları aynı kısma
/// farklı ad veriyor — 12 çiftin 5'i tutmuyor ("KABLO TAVASI" ↔
/// "KABLO KANAL SİSTEMİ" gibi). Detay adı birincil; özet adı
/// kaybolmasın diye alias olarak taşınıyor.
///
/// EŞLEŞTİRME SIRAYA GÖRE, çünkü adlar tutmuyor — tutsalardı alias'a
/// gerek olmazdı. Bu yüzden sayılar eşit değilse HİÇ eşleştirilmiyor:
/// kaydırılmış bir eşleştirme kısımları birbirine karıştırır ve
/// hakediş yanlış satıra yazılır.
/// </summary>
public sealed class ContractSummaryAliasTests
{
    private static ContractSummaryMapping Mapping(
        string? aliasSheet = "İcmal") => new(
        SheetName: "Elektrik",
        HeaderRowIndex: 1,
        CodeColumn: 1,
        DescriptionColumn: 2,
        UnitColumn: 3,
        QuantityColumn: 4,
        MaterialColumn: 5,
        LaborColumn: 6,
        OverheadColumn: 7,
        SectionColumn: null,
        TotalColumn: 8,
        SectionRule: ContractSummarySectionRule.EmptyUnit,
        AliasSheetName: aliasSheet,
        AliasCodeColumn: aliasSheet is null ? null : 1,
        AliasNameColumn: aliasSheet is null ? null : 2);

    /// <summary>Detay sayfası + isteğe bağlı özet sayfası olan dosya.</summary>
    private static MemoryStream Workbook(
        (string Code, string Name)[] detailSections,
        (string Code, string Name)[]? summarySections)
    {
        using var workbook = new XLWorkbook();
        var detail = workbook.Worksheets.Add("Elektrik");

        detail.Cell(1, 1).Value = "Poz";
        detail.Cell(1, 2).Value = "Açıklama";

        var row = 2;

        foreach (var section in detailSections)
        {
            // Kısım: birimi boş.
            detail.Cell(row, 1).Value = section.Code;
            detail.Cell(row, 2).Value = section.Name;
            row++;

            // Altında bir kalem: aktarımın boş kalmaması için.
            detail.Cell(row, 1).Value = section.Code + "01";
            detail.Cell(row, 2).Value = "Kalem";
            detail.Cell(row, 3).Value = "Adet";
            detail.Cell(row, 4).Value = 2;
            detail.Cell(row, 5).Value = 10;
            detail.Cell(row, 6).Value = 0;
            detail.Cell(row, 7).Value = 0;
            detail.Cell(row, 8).Value = 20;
            row++;
        }

        if (summarySections is not null)
        {
            var summary = workbook.Worksheets.Add("İcmal");
            summary.Cell(1, 1).Value = "Poz";
            summary.Cell(1, 2).Value = "Açıklama";

            var summaryRow = 2;

            foreach (var section in summarySections)
            {
                summary.Cell(summaryRow, 1).Value = section.Code;
                summary.Cell(summaryRow, 2).Value = section.Name;
                summaryRow++;
            }

            // Genel toplam satırı: kodu YOK. Kısım sayılmamalı, yoksa
            // sayılar tutmaz ve alias hiç yazılmazdı.
            summary.Cell(summaryRow, 2).Value = "Elektrik İşleri Toplamı";
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void OzetAdlari_SirayaGoreAliasOlarakBaglanir()
    {
        using var stream = Workbook(
            [("01.", "KABLO TAVASI"), ("02.", "BUSBAR SİSTEMİ")],
            [("1", "KABLO KANAL SİSTEMİ"), ("2", "BUSBAR SİSTEMİ")]);

        var result = ContractSummaryMappedParser.Parse(stream, Mapping());
        var sections = result.Lines.Where(x => x.IsSectionHeader).ToList();

        Assert.Null(result.AliasNote);
        Assert.Equal("KABLO KANAL SİSTEMİ", sections[0].AliasName);
        Assert.Equal("BUSBAR SİSTEMİ", sections[1].AliasName);
    }

    [Fact]
    public void SayilarTutmuyorsa_HicEslestirilmez()
    {
        // Özette 3, detayda 2 kısım: sıra eşleştirmesinin dayandığı
        // varsayım çökmüş demektir.
        using var stream = Workbook(
            [("01.", "KABLO TAVASI"), ("02.", "BUSBAR SİSTEMİ")],
            [("1", "A"), ("2", "B"), ("3", "C")]);

        var result = ContractSummaryMappedParser.Parse(stream, Mapping());

        Assert.NotNull(result.AliasNote);
        Assert.All(
            result.Lines.Where(x => x.IsSectionHeader),
            x => Assert.Null(x.AliasName));
    }

    [Fact]
    public void OzetSayfasiSecilmediyse_AliasOkunmaz()
    {
        using var stream = Workbook(
            [("01.", "KABLO TAVASI")],
            [("1", "KABLO KANAL SİSTEMİ")]);

        var result = ContractSummaryMappedParser.Parse(stream, Mapping(aliasSheet: null));

        Assert.Null(result.AliasNote);
        Assert.All(
            result.Lines.Where(x => x.IsSectionHeader),
            x => Assert.Null(x.AliasName));
    }

    [Fact]
    public void OzetSayfasiYoksa_SebepBildirilir()
    {
        using var stream = Workbook([("01.", "KABLO TAVASI")], summarySections: null);

        var result = ContractSummaryMappedParser.Parse(stream, Mapping());

        Assert.NotNull(result.AliasNote);
        Assert.Contains("bulunamadı", result.AliasNote);
    }

    /// <summary>
    /// Boşluk ve "&" farkı gerçek fark değil. Bunları ayrı saymak,
    /// kullanıcıyı hiçbir bilgi taşımayan onaylarla boğar ve gerçek
    /// farkı gözden kaçırtır. NATURA'da ölçüldü: normalize edilmeden
    /// 7, edilerek 5 çift farklı.
    /// </summary>
    [Theory]
    [InlineData("PANOLAR & TABLOLAR", "PANOLAR &TABLOLAR", true)]
    [InlineData("TOPRAKLAMA VE PARATONER  SİSTEMİ", "TOPRAKLAMA &PARATONER SİSTEMİ", true)]
    [InlineData("TV SİSTEMİ", "TV SİSTEMİ", true)]
    [InlineData("KABLO TAVASI", "KABLO KANAL SİSTEMİ", false)]
    [InlineData("YANGIN SİSTEMİ", "YANGIN İHBAR SİSTEMİ", false)]
    [InlineData("KUVVETLİ AKIM ORTAK MEKANLAR", "KUVVETLİ AKIM ORTAK MAHALLER", false)]
    public void AdKarsilastirmasi_BicimFarkiniGercekFarktanAyirir(
        string detail, string summary, bool expected)
    {
        Assert.Equal(expected, ContractSummaryMappedParser.NamesMatch(detail, summary));
    }
}
