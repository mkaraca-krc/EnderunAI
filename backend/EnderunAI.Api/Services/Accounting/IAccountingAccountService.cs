using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Contracts.Core;

namespace EnderunAI.Api.Services.Accounting;

public interface IAccountingAccountService
{
    Task<IReadOnlyCollection<AccountingAccountListItemResponse>> GetAllAsync(
        Guid? companyId,
        Guid? parentAccountId,
        bool? isActive,
        string? search,
        CancellationToken cancellationToken);

    /// <summary>Aranabilir seçici için: sınırlı satır + toplam sayı.</summary>
    Task<PagedResult<AccountingAccountListItemResponse>> SearchAsync(
        Guid? companyId,
        bool? isActive,
        string? search,
        int limit,
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

    /// <summary>K3 — pasife almanın geri alınması.</summary>
    Task ActivateAsync(
        Guid id,
        CancellationToken cancellationToken);
}
