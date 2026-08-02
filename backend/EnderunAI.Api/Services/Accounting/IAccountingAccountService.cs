using EnderunAI.Api.Contracts.Accounting;

namespace EnderunAI.Api.Services.Accounting;

public interface IAccountingAccountService
{
    Task<IReadOnlyCollection<AccountingAccountListItemResponse>> GetAllAsync(
        Guid? companyId,
        Guid? parentAccountId,
        bool? isActive,
        string? search,
        CancellationToken cancellationToken);

    Task<AccountingAccountDetailResponse> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<AccountingAccountDetailResponse> CreateAsync(
        CreateAccountingAccountRequest request,
        CancellationToken cancellationToken);

    Task<AccountingAccountDetailResponse> UpdateAsync(
        Guid id,
        UpdateAccountingAccountRequest request,
        CancellationToken cancellationToken);

    Task DeactivateAsync(
        Guid id,
        CancellationToken cancellationToken);
}
