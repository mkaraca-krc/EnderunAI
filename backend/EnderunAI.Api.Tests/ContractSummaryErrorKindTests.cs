using ClosedXML.Excel;
using EnderunAI.Api.Services.Hakedis;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// İcmal aktarımında hata SINIFLANDIRMASI.
///
/// Neden ayrı bir sınıf: eksik alan hatası sütun eşlemesi düzeltilerek
/// çözülür; tutar uyuşmazlığı çözülmez — o, kaynak Excel'deki bozuk
/// değerdir. İkisini aynı listede aynı görünümde vermek kullanıcıyı
/// yanlış işe yönlendirir. Ekran ayrımı bu alana dayandığı için alan
/// testle kilitleniyor.
///
/// Gerçek NATURA icmalinde ölçülen durum: 389 satırda 5 hata — 2 boş
/// miktar (eksik alan), 3 tutar uyuşmazlığı. Üçünde de Excel miktarı
/// zaten bozmuştu ("1.000" → 1,0), yani hücreden kurtarılamaz; tutar
/// sütunu olmasa sessizce yanlış aktarılırdı.
/// </summary>
public sealed class ContractSummaryErrorKindTests
{
    private static ContractSummaryMapping Mapping() => new(
        SheetName: "Sayfa1",
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
        SectionRule: ContractSummarySectionRule.EmptyUnit);

    /// <summary>Tek satırlık icmal üretir; hücreler olduğu gibi yazılır.</summary>
    private static MemoryStream Workbook(params object?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Sayfa1");

        var headers = new[]
        {
            "Poz", "Açıklama", "Birim", "Miktar",
            "Malzeme B.F.", "İşçilik B.F.", "GG&K B.F.", "Tutar"
        };

        for (var column = 0; column < headers.Length; column++)
            sheet.Cell(1, column + 1).Value = headers[column];

        for (var row = 0; row < rows.Length; row++)
        {
            for (var column = 0; column < rows[row].Length; column++)
            {
                var value = rows[row][column];
                if (value is null) continue;

                var cell = sheet.Cell(row + 2, column + 1);

                if (value is string text) cell.Value = text;
                else if (value is decimal number) cell.Value = number;
                else if (value is int integer) cell.Value = integer;
            }
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void TutarUyusmayanSatir_ChecksumOlarakIsaretlenir()
    {
        // Miktar 1 ama dosya 95.155 diyor: gerçekte 1.000 adet.
        // NATURA satır 276'nın birebir aynısı.
        using var stream = Workbook(
            ["01.01", "Bozuk satır", "Adet", 1m, 95m, 0m, 0.16m, 95155.13m]);

        var result = ContractSummaryMappedParser.Parse(stream, Mapping());

        var error = Assert.Single(result.Errors);

        Assert.Equal(ContractSummaryErrorKind.Checksum, error.Kind);
        Assert.Equal("01.01", error.PositionCode);
        Assert.Equal("Bozuk satır", error.Description);
        Assert.Equal(95155.13m, error.FileTotal);
        Assert.NotNull(error.ComputedTotal);

        // Satır SESSİZCE düşürülmüyor: aktarılmıyor ama raporlanıyor.
        Assert.Empty(result.Lines.Where(x => !x.IsSectionHeader));
    }

    [Fact]
    public void EksikAlanHatasi_ChecksumDegildir()
    {
        // Miktar hücresi boş — NATURA satır 161'in durumu.
        using var stream = Workbook(
            ["01.01", "Miktarsız satır", "Adet", null, 100m, 0m, 0m, null]);

        var result = ContractSummaryMappedParser.Parse(stream, Mapping());

        var error = Assert.Single(result.Errors);

        Assert.Equal(ContractSummaryErrorKind.Missing, error.Kind);

        // Bağlam alanları yalnızca tutar uyuşmazlığında doldurulur;
        // burada boş olmaları beklenen davranış.
        Assert.Null(error.FileTotal);
        Assert.Null(error.ComputedTotal);
    }

    [Fact]
    public void TutariTutanSatir_HatasizAktarilir()
    {
        using var stream = Workbook(
            ["01.01", "Sağlam satır", "Adet", 14m, 20000m, 3600m, 1583.4m, 352567.60m]);

        var result = ContractSummaryMappedParser.Parse(stream, Mapping());

        Assert.Empty(result.Errors);

        var line = Assert.Single(result.Lines.Where(x => !x.IsSectionHeader));
        Assert.Equal("01.01", line.PositionCode);
        Assert.Equal(14m, line.ContractQuantity);
    }

    [Fact]
    public void KisimBasligi_BirimiBosSatirdanTaninir()
    {
        // NATURA düzeni: "01." kısım, "01.01" kalem. Kısım satırında
        // birim yok ama ara toplamlar sayı sütunlarında duruyor.
        using var stream = Workbook(
            ["01.", "PANOLAR & TABLOLAR", null, null, 1995536m, 1451250m, 417663m, 6384287m],
            ["01.01", "A Blok Panosu", "Adet", 1m, 87637.93m, 13500m, 7079.66m, 108217.59m]);

        var result = ContractSummaryMappedParser.Parse(stream, Mapping());

        Assert.Empty(result.Errors);
        Assert.Equal(1, result.SectionCount);
        Assert.Equal(1, result.ItemCount);

        var section = Assert.Single(result.Lines.Where(x => x.IsSectionHeader));
        Assert.Equal("PANOLAR & TABLOLAR", section.SectionName);
    }
}
