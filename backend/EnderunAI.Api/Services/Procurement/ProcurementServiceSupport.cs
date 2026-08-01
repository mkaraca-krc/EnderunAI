using EnderunAI.Api.Models;
using EnderunAI.Api.Models.GoodsReceipt;
using EnderunAI.Api.Models.PurchaseOrder;
using EnderunAI.Api.Models.Rfq;
using EnderunAI.Api.Security;
using RfqEntity = EnderunAI.Api.Models.Rfq.Rfq;

namespace EnderunAI.Api.Services.Procurement;

public sealed class ProcurementNotFoundException(string message) : Exception(message)
{
}

public sealed class ProcurementValidationException(string message) : Exception(message)
{
}

internal static class ProcurementServiceSupport
{
    public static IQueryable<PurchaseRequest> ApplyScope(
        this IQueryable<PurchaseRequest> query,
        CurrentDataScopeSnapshot scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(x =>
                scope.CompanyIds.Contains(x.CompanyId) ||
                scope.BranchIds.Contains(x.Project.BranchId) ||
                scope.ProjectIds.Contains(x.ProjectId));

    public static IQueryable<RfqEntity> ApplyScope(
        this IQueryable<RfqEntity> query,
        CurrentDataScopeSnapshot scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(x =>
                scope.CompanyIds.Contains(x.CompanyId) ||
                scope.BranchIds.Contains(x.PurchaseRequest.Project.BranchId) ||
                scope.ProjectIds.Contains(x.PurchaseRequest.ProjectId));

    public static IQueryable<PurchaseOrder> ApplyScope(
        this IQueryable<PurchaseOrder> query,
        CurrentDataScopeSnapshot scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(x =>
                scope.CompanyIds.Contains(x.CompanyId) ||
                scope.BranchIds.Contains(x.Project.BranchId) ||
                scope.ProjectIds.Contains(x.ProjectId));

    public static IQueryable<GoodsReceipt> ApplyScope(
        this IQueryable<GoodsReceipt> query,
        CurrentDataScopeSnapshot scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(x =>
                scope.CompanyIds.Contains(x.CompanyId) ||
                scope.BranchIds.Contains(x.PurchaseOrder.Project.BranchId) ||
                scope.ProjectIds.Contains(x.PurchaseOrder.ProjectId));

    public static DateTime AsUtc(this DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    public static DateTime? AsUtc(this DateTime? value) =>
        value.HasValue ? value.Value.AsUtc() : null;

    public static string CurrencyOrTry(string? value, int maxLength)
    {
        var currency = string.IsNullOrWhiteSpace(value)
            ? "TRY"
            : value.Trim().ToUpperInvariant();

        if (currency.Length > maxLength)
            throw new ProcurementValidationException("Para birimi kodu geçersiz.");

        return currency;
    }
}
