namespace EnderunAI.Api.Contracts.Pricing;

public sealed record CalculateOfferPriceRequest(
    decimal ListPrice,
    decimal DiscountRate,
    decimal FreightRate,
    decimal WasteRate,
    decimal FinanceRate,
    decimal GeneralExpenseRate,
    decimal ProfitRate);

public sealed record CalculateOfferPriceResponse(
    decimal ListPrice,
    decimal DiscountRate,
    decimal NetPurchasePrice,
    decimal FreightAmount,
    decimal WasteAmount,
    decimal FinanceAmount,
    decimal GeneralExpenseAmount,
    decimal CostPrice,
    decimal ProfitAmount,
    decimal SalesPrice);
