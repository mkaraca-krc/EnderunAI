using EnderunAI.Api.Contracts.Accounting;

namespace EnderunAI.Api.Services.Accounting;

public interface IAccountingVoucherService
{
    Task<IReadOnlyCollection<AccountingVoucherListItemResponse>> GetAllAsync(
        Guid? companyId,
        int? status,
        int? voucherType,
        DateTime? startDate,
        DateTime? endDate,
        string? search,
        CancellationToken cancellationToken);

    Task<AccountingVoucherDetailResponse> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<AccountingVoucherDetailResponse> CreateAsync(
        CreateAccountingVoucherRequest request,
        CancellationToken cancellationToken);

    Task<AccountingVoucherDetailResponse> UpdateAsync(
        Guid id,
        UpdateAccountingVoucherRequest request,
        CancellationToken cancellationToken);

    Task<AccountingVoucherActionResponse> PostAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<AccountingVoucherActionResponse> CancelAsync(
        Guid id,
        string reason,
        CancellationToken cancellationToken);
}
