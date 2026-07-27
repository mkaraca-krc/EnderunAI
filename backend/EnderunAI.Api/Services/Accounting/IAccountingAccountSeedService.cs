namespace EnderunAI.Api.Services.Accounting;

public interface IAccountingAccountSeedService
{
    Task<AccountingAccountSeedResult> SeedAsync(
        Guid companyId,
        CancellationToken cancellationToken);
}

public sealed record AccountingAccountSeedResult(
    int CreatedCount,
    int ExistingCount,
    int TotalCount,
    string Message);
