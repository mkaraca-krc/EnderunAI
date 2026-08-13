using ClosedXML.Excel;
using EnderunAI.Api.Services.Engineering;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Reçete dosyası ayrıştırma. Saf: veritabanı yok, dosya bellekte
/// kuruluyor.
///
/// Ayrıştırıcının asıl işi ELEMEK: bozuk satır sessizce geçmemeli,
/// çünkü reçeteden çıkan miktar doğrudan satın alma talebine dönüşüyor.
/// Yanlış okunan bir miktar yanlış malzeme siparişi demek.
/// </summary>
public sealed class RecipeImportParserTests
{
    private static RecipeImportMapping Mapping() => new(
        SheetName: "Reçete",
        HeaderRowIndex: 1,
        PositionCodeColumn: 1,
        MaterialNameColumn: 3,
        QuantityColumn: 4,
        UnitColumn: 5,
        MaterialCodeColumn: 2,
        WastePercentColumn: 6);

    /// <summary>Satırlar: poz kodu, malzeme kodu, ad, miktar, birim, fire.</summary>
    private static MemoryStream Workbook(params object?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Reçete");

        sheet.Cell(1, 1).Value = "Poz No";
        sheet.Cell(1, 2).Value = "Malzeme Kodu";
        sheet.Cell(1, 3).Value = "Malzeme";
        sheet.Cell(1, 4).Value = "Miktar";
        sheet.Cell(1, 5).Value = "Birim";
        sheet.Cell(1, 6).Value = "Fire %";

        var rowNumber = 2;

        foreach (var row in rows)
        {
            for (var column = 0; column < row.Length; column++)
            {
                var value = row[column];

                if (value is null) continue;

                if (value is decimal number)
                    sheet.Cell(rowNumber, column + 1).Value = number;
                else
                    sheet.Cell(rowNumber, column + 1).Value = value.ToString();
            }

            rowNumber++;
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        return stream;
    }

    [Fact]
    public void GecerliSatir_Okunur()
    {
        using var file = Workbook(
            ["35.100.1001", "KBL-01", "NYA Kablo 2.5mm", 12.5m, "m", 5m]);

        var result = RecipeImportParser.Parse(file, Mapping());

        var row = Assert.Single(result.Rows);

        Assert.True(row.IsValid);
        Assert.Equal("35.100.1001", row.PositionCode);
        Assert.Equal("KBL-01", row.MaterialCode);
        Assert.Equal(12.5m, row.Quantity);
        Assert.Equal("m", row.Unit);
        Assert.Equal(5m, row.WastePercent);
        Assert.False(row.PositionCodeInherited);
    }

    /// <summary>
    /// Gerçek reçete tablolarında poz kodu yalnız bloğun ilk satırında
    /// yazar. Devralma olmasaydı dosyaların neredeyse tamamı "poz kodu
    /// yok" diye reddedilirdi — ama devralma SESSİZ de olmamalı.
    /// </summary>
    [Fact]
    public void PozKodu_BosSatirlarda_UsttenDevralinir()
    {
        using var file = Workbook(
            ["35.100.1001", "KBL-01", "NYA Kablo", 10m, "m", null],
            [null, "BRU-01", "Buat", 2m, "adet", null]);

        var result = RecipeImportParser.Parse(file, Mapping());

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("35.100.1001", result.Rows[1].PositionCode);
        Assert.True(result.Rows[1].PositionCodeInherited);
        Assert.True(result.Rows[1].IsValid);
    }

    /// <summary>
    /// Boş satır blok ayracıdır: sonrasındaki malzeme bir öncekinin
    /// devamı sayılmaz. Sayılsaydı, araya boşluk konmuş bir dosyada
    /// malzemeler yanlış poza yazılırdı.
    /// </summary>
    [Fact]
    public void BosSatirdanSonra_DevralmaBiter()
    {
        using var file = Workbook(
            ["35.100.1001", "KBL-01", "NYA Kablo", 10m, "m", null],
            [null, null, null, null, null, null],
            [null, "BRU-01", "Buat", 2m, "adet", null]);

        var rows = RecipeImportParser.Parse(file, Mapping()).Rows;

        Assert.Equal(2, rows.Count);
        Assert.False(rows[1].IsValid);
        Assert.Equal("Poz kodu yok.", rows[1].Error);
    }

    [Theory]
    [InlineData(0, "Miktar sıfır veya negatif.")]
    [InlineData(-3, "Miktar sıfır veya negatif.")]
    public void GecersizMiktar_Reddedilir(double quantity, string expected)
    {
        using var file = Workbook(
            ["35.100.1001", "KBL-01", "NYA Kablo", (decimal)quantity, "m", null]);

        var row = Assert.Single(RecipeImportParser.Parse(file, Mapping()).Rows);

        Assert.Equal(expected, row.Error);
    }

    [Fact]
    public void MiktarOkunamazsa_Reddedilir()
    {
        using var file = Workbook(
            ["35.100.1001", "KBL-01", "NYA Kablo", "yaklaşık", "m", null]);

        var row = Assert.Single(RecipeImportParser.Parse(file, Mapping()).Rows);

        Assert.Equal("Miktar okunamadı.", row.Error);
    }

    /// <summary>
    /// Fire ihtiyacı doğrudan çarpar: %1000 yazım hatası malzeme
    /// ihtiyacını on bir katına çıkarır ve fark edilmeden satın almaya
    /// gider.
    /// </summary>
    [Fact]
    public void AsiriFire_Reddedilir()
    {
        using var file = Workbook(
            ["35.100.1001", "KBL-01", "NYA Kablo", 10m, "m", 1000m]);

        var row = Assert.Single(RecipeImportParser.Parse(file, Mapping()).Rows);

        Assert.Equal("Fire yüzdesi 0-100 aralığında olmalı.", row.Error);
    }

    [Fact]
    public void BirimYoksa_Reddedilir()
    {
        using var file = Workbook(
            ["35.100.1001", "KBL-01", "NYA Kablo", 10m, null, null]);

        var row = Assert.Single(RecipeImportParser.Parse(file, Mapping()).Rows);

        Assert.Equal("Birim yok.", row.Error);
    }

    /// <summary>
    /// Türkçe ondalık ayracı: poz kitabı aktarımıyla AYNI kural, çünkü
    /// sayı okuma tek yerden (PositionImportParser.ParseNumericText)
    /// geçiyor.
    /// </summary>
    [Fact]
    public void VirgulluMiktar_Okunur()
    {
        using var file = Workbook(
            ["35.100.1001", "KBL-01", "NYA Kablo", "1.234,56", "m", null]);

        var row = Assert.Single(RecipeImportParser.Parse(file, Mapping()).Rows);

        Assert.True(row.IsValid);
        Assert.Equal(1234.56m, row.Quantity);
    }

    /// <summary>Fire sütunu eşlenmemişse fire sıfırdır, satır geçerlidir.</summary>
    [Fact]
    public void FireSutunuEslenmemisse_SifirSayilir()
    {
        using var file = Workbook(
            ["35.100.1001", "KBL-01", "NYA Kablo", 10m, "m", 7m]);

        var mapping = Mapping() with { WastePercentColumn = null };
        var row = Assert.Single(RecipeImportParser.Parse(file, mapping).Rows);

        Assert.True(row.IsValid);
        Assert.Equal(0m, row.WastePercent);
    }
}
