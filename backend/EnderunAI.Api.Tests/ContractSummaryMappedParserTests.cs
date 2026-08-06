using ClosedXML.Excel;
using EnderunAI.Api.Services.Hakedis;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Kullanıcının eşlediği sütunlardan icmal okuma.
///
/// Testler gerçek bir NATURA icmalinin düzenini taklit ediyor: antet
/// satırı, başlık satırı, kısım satırında ARA TOPLAMLAR, üç ve dört
/// seviyeli poz kodları, para birimi biçimli METİN hücreler ve binlik
/// ayırıcıyla yazılmış miktarlar.
///
/// Asıl güvence: belirsiz sayı TAHMİN EDİLMEZ. "3.976" hem üç bin dokuz
/// yüz yetmiş altı hem üç virgül dokuz yüz yetmiş altı okunabilir; bin
/// kat yanlış bir miktar icmali sessizce bozar. Dosyanın kendi tutar
/// sütunu hakem yapılır, hakem yoksa satır hata olur.
/// </summary>
public sealed class ContractSummaryMappedParserTests
{
    private const int ColCode = 1;
    private const int ColDescription = 2;
    private const int ColUnit = 5;
    private const int ColQuantity = 6;
    private const int ColMaterial = 7;
    private const int ColLabor = 8;
    private const int ColOverhead = 9;
    private const int ColTotal = 11;

    private static ContractSummaryMapping Mapping(bool withTotalColumn = true) =>
        new(
            SheetName: "Elektrik",
            HeaderRowIndex: 2,
            CodeColumn: ColCode,
            DescriptionColumn: ColDescription,
            UnitColumn: ColUnit,
            QuantityColumn: ColQuantity,
            MaterialColumn: ColMaterial,
            LaborColumn: ColLabor,
            OverheadColumn: ColOverhead,
            SectionColumn: null,
            TotalColumn: withTotalColumn ? ColTotal : null,
            SectionRule: ContractSummarySectionRule.EmptyUnit);

    private sealed record Row(
        string Code,
        string Description,
        string? Unit = null,
        object? Quantity = null,
        object? Material = null,
        object? Labor = null,
        object? Overhead = null,
        object? Total = null);

    private static MemoryStream Build(params Row[] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Elektrik");

        // Gerçek dosyadaki gibi: 1. satır antet, 2. satır başlıklar.
        sheet.Cell(1, 1).Value = "RHH - FİZİK TEDAVİ VE REHABİLİTASYON HASTANESİ";
        sheet.Cell(2, ColCode).Value = "Poz";
        sheet.Cell(2, ColDescription).Value = "AÇIKLAMA";
        sheet.Cell(2, ColUnit).Value = "Birim";
        sheet.Cell(2, ColQuantity).Value = "Keşif Miktarı";
        sheet.Cell(2, ColMaterial).Value = "Malzeme Birim Fiyatı";
        sheet.Cell(2, ColLabor).Value = "İşçilik Birim Fiyatı";
        sheet.Cell(2, ColOverhead).Value = "G.G Kâr Birim Fiyatı";
        sheet.Cell(2, ColTotal).Value = "Tutar";

        var row = 3;

        foreach (var line in rows)
        {
            sheet.Cell(row, ColCode).Value = line.Code;
            sheet.Cell(row, ColDescription).Value = line.Description;

            if (line.Unit is not null) sheet.Cell(row, ColUnit).Value = line.Unit;

            Set(sheet, row, ColQuantity, line.Quantity);
            Set(sheet, row, ColMaterial, line.Material);
            Set(sheet, row, ColLabor, line.Labor);
            Set(sheet, row, ColOverhead, line.Overhead);
            Set(sheet, row, ColTotal, line.Total);

            row++;
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        return stream;
    }

    private static void Set(IXLWorksheet sheet, int row, int column, object? value)
    {
        switch (value)
        {
            case null:
                return;
            case string text:
                sheet.Cell(row, column).Value = text;
                return;
            case decimal number:
                sheet.Cell(row, column).Value = number;
                return;
            case double number:
                sheet.Cell(row, column).Value = number;
                return;
            case int number:
                sheet.Cell(row, column).Value = number;
                return;
        }
    }

    [Fact]
    public void ReadsSectionsAndItems()
    {
        using var file = Build(
            // Kısım satırı: birim BOŞ ama sayı sütunlarında ARA TOPLAM var.
            new Row("01.", "PANOLAR & TABLOLAR",
                Quantity: "1.995.540,00", Material: "₺1.451.250,00", Total: "₺6.384.287,35"),
            new Row("01.01", "A Blok / Adp Panosu", "Adet", 1,
                "₺87.637,90", "₺13.500,00", "₺7.079,66", "₺108.217,56"),
            new Row("01.02", "A Blok / At-Z Panosu", "Adet", 2,
                "₺42.794,88", "₺2.250,00", "₺3.153,14", "₺96.396,04"),
            new Row("02.", "KUVVETLİ AKIM", Total: "₺100,00"),
            new Row("02.01", "Sorti", "Adet", 3, "₺10,00", "₺5,00", "₺1,00", "₺48,00"));

        var result = ContractSummaryMappedParser.Parse(file, Mapping());

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.SectionCount);
        Assert.Equal(3, result.ItemCount);

        var first = result.Lines.Single(x => x.PositionCode == "01.01");

        Assert.Equal("PANOLAR & TABLOLAR", first.SectionName);
        Assert.Equal("Adet", first.Unit);
        Assert.Equal(1m, first.ContractQuantity);
        Assert.Equal(87_637.90m, first.MaterialUnitPrice);
        Assert.Equal(13_500.00m, first.LaborUnitPrice);
        Assert.Equal(7_079.66m, first.OverheadUnitPrice);
        Assert.Equal(108_217.56m, first.TotalAmount);

        // İkinci kısmın satırı birinci kısma yazılmamalı.
        Assert.Equal("KUVVETLİ AKIM",
            result.Lines.Single(x => x.PositionCode == "02.01").SectionName);
    }

    [Fact]
    public void SectionSubtotalRow_IsNotReadAsItem()
    {
        // Kısım satırının sayı sütunlarında ara toplam var; poz sanılırsa
        // icmale 1.995.540 adetlik hayalet bir kalem girer.
        using var file = Build(
            new Row("01.", "PANOLAR", Quantity: "1.995.540,00", Total: "₺6.384.287,35"),
            new Row("01.01", "Pano", "Adet", 1, "₺100,00", "₺0,00", "₺0,00", "₺100,00"));

        var result = ContractSummaryMappedParser.Parse(file, Mapping());

        var item = Assert.Single(result.Lines.Where(x => !x.IsSectionHeader));

        Assert.Equal("01.01", item.PositionCode);
        Assert.Equal(1m, item.ContractQuantity);
    }

    [Fact]
    public void SubGroupHeader_GoesToCategory_NotNewSection()
    {
        using var file = Build(
            new Row("12.", "İLAVE İŞLER"),
            new Row("12.06", "SOSYAL TESİS", Total: "₺756.155,44"),
            new Row("12.06.01.01", "200 Mm Tava", "Metre", 10,
                "₺50,00", "₺30,00", "₺5,00", "₺850,00"));

        var result = ContractSummaryMappedParser.Parse(file, Mapping());

        Assert.Empty(result.Errors);

        // Alt grup kısım açmamalı: hakediş 12 kısım üzerinden düzenleniyor.
        Assert.Equal(1, result.SectionCount);

        var item = Assert.Single(result.Lines.Where(x => !x.IsSectionHeader));

        Assert.Equal("İLAVE İŞLER", item.SectionName);
        Assert.Equal("SOSYAL TESİS", item.Category);
    }

    [Fact]
    public void AmbiguousQuantity_IsResolvedByFileTotal()
    {
        // "3.976" binlik mi ondalık mı? Dosyanın kendi tutarı hakem:
        // 3976 × 276,80 = 1.100.556,80 ≈ 1.100.544,58.
        using var file = Build(
            new Row("10.", "KABLO TAVASI"),
            new Row("10.01", "Kablo tavası", "Metre", "3.976",
                "₺180,00", "₺90,00", "₺6,80", "₺1.100.544,58"));

        var result = ContractSummaryMappedParser.Parse(file, Mapping());

        Assert.Empty(result.Errors);
        Assert.Equal(3976m, result.Lines.Single(x => !x.IsSectionHeader).ContractQuantity);
    }

    [Fact]
    public void AmbiguousQuantity_WithoutTotalColumn_IsError()
    {
        // Hakem yoksa tahmin edilmez.
        using var file = Build(
            new Row("10.", "KABLO TAVASI"),
            new Row("10.01", "Kablo tavası", "Metre", "3.976",
                "₺180,00", "₺90,00", "₺6,80", "₺1.100.544,58"));

        var result = ContractSummaryMappedParser.Parse(
            file, Mapping(withTotalColumn: false));

        Assert.Empty(result.Lines.Where(x => !x.IsSectionHeader));

        var error = Assert.Single(result.Errors);
        Assert.Contains("belirsiz", error.Message);
    }

    [Fact]
    public void QuantityWrittenAsFourDigitGroup_IsResolved()
    {
        // "5.6854" gerçek dosyada 56854 demek; düz ondalık okuma 5,6854
        // verir ve satırı on binde bire düşürürdü.
        using var file = Build(
            new Row("10.", "KABLO TAVASI"),
            new Row("10.08", "Tava askısı", "Metre", "5.6854",
                "₺0,30", "₺0,15", "₺0,02", "₺26.721,38"));

        var result = ContractSummaryMappedParser.Parse(file, Mapping());

        Assert.Empty(result.Errors);
        Assert.Equal(56854m, result.Lines.Single(x => !x.IsSectionHeader).ContractQuantity);
    }

    [Fact]
    public void RowWhoseTotalDisagrees_IsRejected()
    {
        // Miktarı bin katını kaybetmiş satır: dosyada 95.155,13 yazıyor
        // ama 1 × 95,15 hesaplanıyor. Sessizce aktarılırsa icmal eksik
        // kalır ve bunu sonradan fark etmek çok zor.
        using var file = Build(
            new Row("12.", "İLAVE İŞLER"),
            new Row("12.01.15", "Kablo", "Metre", 1,
                "₺39,21", "₺55,00", "₺0,94", "₺95.155,13"));

        var result = ContractSummaryMappedParser.Parse(file, Mapping());

        Assert.Empty(result.Lines.Where(x => !x.IsSectionHeader));

        var error = Assert.Single(result.Errors);
        Assert.Contains("uyuşmuyor", error.Message);
        Assert.Equal(4, error.RowNumber);
    }

    [Fact]
    public void RoundingDifference_IsAccepted()
    {
        // Birim fiyatlar dosyada iki haneye yuvarlanmış; büyük metrajda
        // kuruş farkı yüzlerce liraya çıkıyor ama satır sağlam.
        using var file = Build(
            new Row("04.", "TV SİSTEMİ"),
            new Row("04.01.01", "Rg6-U4 Tv Kablosu", "Metre", 15246,
                "₺23,40", "₺27,00", "₺3,53", "₺822.186,29"));

        var result = ContractSummaryMappedParser.Parse(file, Mapping());

        Assert.Empty(result.Errors);

        var item = Assert.Single(result.Lines.Where(x => !x.IsSectionHeader));
        Assert.Equal(822_216.78m, item.TotalAmount);
    }

    [Fact]
    public void EmptyPriceComponent_CountsAsZero()
    {
        // Gerçek dosyada işçiliği olmayan kalemin hücresi boş; dosyanın
        // kendi toplamı da bunu sıfır sayıyor.
        using var file = Build(
            new Row("12.", "İLAVE İŞLER"),
            new Row("12.06.02.04", "Fan modülü", "Adet", 1,
                "₺1.215,00", null, "₺12,15", "₺1.227,15"));

        var result = ContractSummaryMappedParser.Parse(file, Mapping());

        Assert.Empty(result.Errors);

        var item = Assert.Single(result.Lines.Where(x => !x.IsSectionHeader));
        Assert.Equal(0m, item.LaborUnitPrice);
        Assert.Equal(1_227.15m, item.TotalAmount);
    }

    [Fact]
    public void MissingQuantity_IsError()
    {
        using var file = Build(
            new Row("08.", "TOPRAKLAMA"),
            new Row("08.01.01", "30X3,5 Galvaniz Lama", "Metre", null,
                "₺2.745,00", "₺40,00", "₺194,95"));

        var result = ContractSummaryMappedParser.Parse(file, Mapping());

        Assert.Empty(result.Lines.Where(x => !x.IsSectionHeader));
        Assert.Contains(result.Errors, x => x.Message.Contains("Miktar boş"));
    }

    [Fact]
    public void SignatureAndTotalRows_AreIgnored()
    {
        // Dosyanın altındaki imza bloğunda birim sütununa isim düşmüş
        // olabiliyor; kodu ve tanımı olmayan satır poz sayılmamalı.
        using var file = Build(
            new Row("01.", "PANOLAR"),
            new Row("01.01", "Pano", "Adet", 1, "₺100,00", "₺0,00", "₺0,00", "₺100,00"),
            new Row("", "", "ENDERUN ENERJİ"),
            new Row("", "Elektrik İşleri Toplamı", Total: 87_000_000m));

        var result = ContractSummaryMappedParser.Parse(file, Mapping());

        Assert.Empty(result.Errors);
        Assert.Equal(1, result.ItemCount);
        Assert.Equal(100m, result.TotalAmount);
    }

    [Fact]
    public void SectionColumnRule_StillWorks()
    {
        // Eski ENDERUN şablonu: ayrı kısım sütunu, kod boş.
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Elektrik");

        sheet.Cell(1, 1).Value = "Kısım";
        sheet.Cell(2, 1).Value = "Panolar";
        sheet.Cell(3, 2).Value = "P.01";
        sheet.Cell(3, 3).Value = "Ana pano";
        sheet.Cell(3, 4).Value = "Adet";
        sheet.Cell(3, 5).Value = 2;
        sheet.Cell(3, 6).Value = 100;
        sheet.Cell(3, 7).Value = 50;
        sheet.Cell(3, 8).Value = 10;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var mapping = new ContractSummaryMapping(
            SheetName: "Elektrik",
            HeaderRowIndex: 1,
            CodeColumn: 2,
            DescriptionColumn: 3,
            UnitColumn: 4,
            QuantityColumn: 5,
            MaterialColumn: 6,
            LaborColumn: 7,
            OverheadColumn: 8,
            SectionColumn: 1,
            TotalColumn: null,
            SectionRule: ContractSummarySectionRule.SectionColumn);

        var result = ContractSummaryMappedParser.Parse(stream, mapping);

        Assert.Empty(result.Errors);
        Assert.Equal(1, result.SectionCount);

        var item = Assert.Single(result.Lines.Where(x => !x.IsSectionHeader));
        Assert.Equal("Panolar", item.SectionName);
        Assert.Equal(320m, item.TotalAmount);
    }
}
