namespace EnderunAI.Api.Contracts;

public sealed class UpdateCompanySettingsRequest
{
    public string Name { get; set; } = string.Empty;
    public string? TradeName { get; set; }
    public string? TaxOffice { get; set; }
    public string? TaxNumber { get; set; }
    public string? MersisNumber { get; set; }
    public string? TradeRegistryNumber { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? Address { get; set; }
}

public sealed class CreateCompanyBankAccountRequest
{
    public string BankName { get; set; } = string.Empty;
    public string Iban { get; set; } = string.Empty;
    public string? AccountHolder { get; set; }
    public string? CurrencyCode { get; set; }
}
