using EnderunAI.Api.Services.EInvoice;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Anahtar kelimeden gider/hesap önerisi. Veritabanına dokunmaz.
///
/// Buradaki asıl risk yanlış pozitif: malzeme faturasının gider
/// sanılması, faturanın stoğa hiç girmemesine ve maliyetin yanlış
/// hesaba yazılmasına yol açar. Bu yüzden testlerin yarısı
/// "eşleşmemeli" tarafındadır.
/// </summary>
public sealed class EInvoiceExpenseSuggesterTests
{
    [Theory]
    [InlineData("Elektrik Enerjisi Tuketim Bedeli", "770.03.10")]
    [InlineData("ELEKTRİK TÜKETİM BEDELİ", "770.03.10")]
    [InlineData("Doğalgaz tüketim bedeli", "770.03.12")]
    [InlineData("Doğal gaz satışı", "770.03.12")]
    [InlineData("İnternet hizmet bedeli", "770.03.13")]
    [InlineData("Ofis temizliği hizmet bedeli", "770.03.14")]
    [InlineData("OSGB hizmet bedeli", "770.03.15")]
    [InlineData("İşyeri hekimi hizmeti", "770.03.15")]
    [InlineData("Mart ayı kirası", "770.04.13")]
    [InlineData("Mali müşavirlik ücreti", "770.03.05")]
    [InlineData("Kasko poliçesi", "770.04.10")]
    public void Suggest_UtilityLines_MapToExpenseAccount(
        string lineDescription, string expectedCode)
    {
        var suggestion = EInvoiceExpenseSuggester.Suggest([lineDescription]);

        Assert.True(suggestion.IsExpense);
        Assert.Equal(expectedCode, suggestion.AccountCode);
        Assert.False(string.IsNullOrWhiteSpace(suggestion.Reason));
    }

    [Theory]
    [InlineData("NYAF Kablo 3x2.5")]
    [InlineData("Kofra 12 Modul")]
    [InlineData("C25 Hazır Beton")]
    [InlineData("Q221 Hasır Çelik")]
    public void Suggest_MaterialLines_StayAsStock(string lineDescription)
    {
        var suggestion = EInvoiceExpenseSuggester.Suggest([lineDescription]);

        Assert.False(suggestion.IsExpense);
        Assert.Null(suggestion.AccountCode);
    }

    /// <summary>
    /// Anahtar kelime kelime BAŞINDA aranır. "kusur" içindeki "su"
    /// eşleşseydi tamir faturası su gideri sanılırdı; buna karşılık
    /// Türkçe ek alan "kirası" eşleşmeye devam etmeli.
    /// </summary>
    [Fact]
    public void Suggest_MatchesWordStartOnly()
    {
        Assert.False(EInvoiceExpenseSuggester
            .Suggest(["Kusurlu imalat bedeli"]).IsExpense);

        var withSuffix = EInvoiceExpenseSuggester.Suggest(["Ofis kirası"]);

        Assert.True(withSuffix.IsExpense);
        Assert.Equal("770.04.13", withSuffix.AccountCode);
    }

    [Fact]
    public void Suggest_EmptyInvoice_SuggestsNothing()
    {
        var suggestion = EInvoiceExpenseSuggester.Suggest([]);

        Assert.False(suggestion.IsExpense);
        Assert.Null(suggestion.AccountCode);
        Assert.Null(suggestion.Reason);
    }
}
