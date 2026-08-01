using System.Data;
using System.Data.Common;
using System.Text.Json;
using EnderunAI.Api.Contracts.Procurement;
using EnderunAI.Api.Contracts.PurchaseOrders;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.PurchaseOrder;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PurchaseOrderEntity = EnderunAI.Api.Models.PurchaseOrder.PurchaseOrder;

namespace EnderunAI.Api.Services.Procurement;

public sealed class ProcurementApprovalService(
    AppDbContext db,
    ICurrentDataScopeService dataScope,
    ICurrentUserService currentUser,
    Func<HttpContext?>? getHttpContext = null) : IProcurementApprovalService
{
    private const string PolicyEntityType = "ProcurementApprovalPolicy";
    private const string PolicyConfiguredAction = "PolicyConfigured";
    private const string BudgetEntityType = "ProcurementBudget";
    private const string BudgetConfiguredAction = "BudgetConfigured";
    private const string ApprovalEntityType = "ProcurementPurchaseOrderApproval";
    private const string ApprovalPlanAction = "ApprovalPlanCreated";
    private const string StageApprovedAction = "ApprovalStageApproved";
    private const string StageRejectedAction = "ApprovalStageRejected";

    private const string PurchasingStageCode = "PURCHASING";
    private const string FinanceStageCode = "FINANCE";
    private const string ExecutiveStageCode = "EXECUTIVE";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<ProcurementApprovalDashboardResponse> GetDashboardAsync(
        Guid companyId,
        Guid? projectId,
        CancellationToken cancellationToken)
    {
        EnsureAnyPermission(
            PermissionCatalog.Keys.PurchasingView,
            PermissionCatalog.Keys.FinanceView);
        var scope = await GetScopeAsync(cancellationToken);
        var company = await scope.Apply(db.Companies.AsNoTracking())
            .Where(x => x.Id == companyId)
            .Select(x => new CompanyProjection(x.Id, x.Code, x.Name))
            .SingleOrDefaultAsync(cancellationToken) ??
            throw new ProcurementNotFoundException("Şirket bulunamadı veya erişim yetkiniz yok.");

        var projectsQuery = scope.Apply(db.Projects.AsNoTracking())
            .Where(x => x.CompanyId == companyId);
        if (projectId.HasValue)
            projectsQuery = projectsQuery.Where(x => x.Id == projectId.Value);

        var projects = await projectsQuery
            .OrderBy(x => x.Code)
            .Select(x => new ProjectProjection(x.Id, x.CompanyId, x.Code, x.Name))
            .ToListAsync(cancellationToken);

        if (projectId.HasValue && projects.Count == 0)
            throw new ProcurementNotFoundException("Proje bulunamadı veya erişim yetkiniz yok.");

        var policy = await LoadPolicyAsync(companyId, cancellationToken);
        var projectIds = projects.Select(x => x.Id).ToArray();
        var budgetStates = await LoadBudgetStatesAsync(projectIds, cancellationToken);

        var commitmentRows = Array.Empty<CommitmentProjection>();
        if (budgetStates.Count > 0)
        {
            var minDate = budgetStates.Min(x => x.PeriodStart);
            var maxDate = budgetStates.Max(x => x.PeriodEnd).Date.AddDays(1);
            commitmentRows = await db.PurchaseOrders
                .AsNoTracking()
                .ApplyScope(scope)
                .Where(x => projectIds.Contains(x.ProjectId) &&
                            x.OrderDate >= minDate &&
                            x.OrderDate < maxDate &&
                            (x.Status == PurchaseOrderStatus.PendingApproval ||
                             x.Status == PurchaseOrderStatus.Approved ||
                             x.Status == PurchaseOrderStatus.PartiallyReceived ||
                             x.Status == PurchaseOrderStatus.Completed))
                .Select(x => new CommitmentProjection(
                    x.ProjectId,
                    x.OrderDate,
                    x.GrandTotal * x.ExchangeRate))
                .ToArrayAsync(cancellationToken);
        }

        var projectById = projects.ToDictionary(x => x.Id);
        var budgetResponses = budgetStates
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.PeriodStart)
            .Select(x => BuildBudgetResponse(
                x,
                projectById[x.ProjectId],
                SumCommitments(commitmentRows, x)))
            .ToArray();

        var pendingOrders = await db.PurchaseOrders
            .AsNoTracking()
            .ApplyScope(scope)
            .Where(x => projectIds.Contains(x.ProjectId) &&
                        x.Status == PurchaseOrderStatus.PendingApproval)
            .OrderBy(x => x.OrderDate)
            .ThenBy(x => x.OrderNumber)
            .Select(x => new PendingOrderProjection(
                x.Id,
                x.OrderNumber,
                x.CompanyId,
                x.ProjectId,
                x.Project.Code,
                x.Project.Name,
                x.SupplierCurrentAccount.Title,
                x.OrderDate,
                x.GrandTotal * x.ExchangeRate))
            .Take(200)
            .ToListAsync(cancellationToken);

        var approvalEvents = await LoadApprovalEventsAsync(
            pendingOrders.Select(x => x.Id).ToArray(),
            cancellationToken);

        var pendingResponses = new List<ProcurementPendingApprovalResponse>();
        foreach (var order in pendingOrders)
        {
            var orderEvents = approvalEvents
                .Where(x => x.EntityId == order.Id)
                .ToArray();
            var plan = ResolvePlan(orderEvents) ??
                       (policy is null
                           ? null
                           : BuildPlanPayload(
                               Guid.NewGuid(),
                               order.CompanyId,
                               order.ProjectId,
                               order.Id,
                               RoundTry(order.OrderAmountTry),
                               policy,
                               null));
            if (plan is null)
                continue;

            var steps = BuildStepResponses(plan, orderEvents);
            var currentStep = steps.FirstOrDefault(x => x.Status == "Pending");
            if (currentStep is null)
                continue;

            var budget = FindActiveBudget(
                budgetStates,
                order.ProjectId,
                order.OrderDate,
                throwOnOverlap: false);
            var committedBefore = budget is null
                ? (decimal?)null
                : SumCommitments(commitmentRows, budget) - RoundTry(order.OrderAmountTry);
            var remainingAfter = budget is null
                ? (decimal?)null
                : RoundTry(budget.AmountTry -
                           Math.Max(0m, committedBefore ?? 0m) -
                           RoundTry(order.OrderAmountTry));

            pendingResponses.Add(new ProcurementPendingApprovalResponse(
                order.Id,
                order.OrderNumber,
                order.ProjectId,
                order.ProjectCode,
                order.ProjectName,
                order.SupplierTitle,
                order.OrderDate,
                RoundTry(order.OrderAmountTry),
                currentStep.Sequence,
                currentStep.Name,
                currentStep.RequiredAuthority,
                CanApprove(currentStep.Code),
                budget is not null &&
                remainingAfter.HasValue &&
                (remainingAfter.Value < 0m ||
                 UtilizationPercent(
                     budget.AmountTry,
                     budget.AmountTry - remainingAfter.Value) >=
                 budget.WarningThresholdPercent),
                remainingAfter));
        }

        var warnings = new List<string>();
        if (policy is null)
            warnings.Add("Şirket için çok kademeli onay politikası henüz yapılandırılmadı.");
        if (policy?.RequireBudget == true && budgetStates.All(x => !x.IsActive))
            warnings.Add("Bütçe kontrolü zorunlu, ancak seçili kapsamda aktif proje bütçesi yok.");
        if (pendingOrders.Count >= 200)
            warnings.Add("Onay kuyruğu ilk 200 siparişle sınırlandı; proje filtresini daraltın.");

        return new ProcurementApprovalDashboardResponse(
            company.Id,
            company.Code,
            company.Name,
            policy,
            budgetResponses,
            pendingResponses,
            pendingResponses.Count,
            pendingResponses.Count(x => x.CanCurrentUserApprove),
            RoundTry(pendingResponses.Sum(x => x.OrderAmountTry)),
            budgetResponses.Count(x => x.IsWarning || x.IsExceeded),
            warnings);
    }

    public async Task<PurchaseOrderApprovalContextResponse> GetOrderContextAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
    {
        EnsureAnyPermission(
            PermissionCatalog.Keys.PurchasingView,
            PermissionCatalog.Keys.FinanceView);
        var scope = await GetScopeAsync(cancellationToken);
        var order = await db.PurchaseOrders
            .AsNoTracking()
            .ApplyScope(scope)
            .Where(x => x.Id == purchaseOrderId)
            .Select(x => new OrderContextProjection(
                x.Id,
                x.OrderNumber,
                x.CompanyId,
                x.ProjectId,
                x.Project.Code,
                x.Project.Name,
                x.OrderDate,
                x.GrandTotal * x.ExchangeRate,
                x.Status))
            .SingleOrDefaultAsync(cancellationToken) ??
            throw new ProcurementNotFoundException("Satın alma siparişi bulunamadı.");

        var policy = await LoadPolicyAsync(order.CompanyId, cancellationToken);
        var budgetStates = await LoadBudgetStatesAsync([order.ProjectId], cancellationToken);
        var budget = FindActiveBudget(
            budgetStates,
            order.ProjectId,
            order.OrderDate,
            throwOnOverlap: true);
        var committedExcludingOrder = budget is null
            ? (decimal?)null
            : await GetCommittedAmountAsync(
                scope,
                budget,
                order.Id,
                cancellationToken);
        var amountTry = RoundTry(order.OrderAmountTry);
        var amountAfter = budget is null
            ? (decimal?)null
            : RoundTry((committedExcludingOrder ?? 0m) + amountTry);
        var remainingAfter = budget is null
            ? (decimal?)null
            : RoundTry(budget.AmountTry - amountAfter!.Value);

        var approvalEvents = await LoadApprovalEventsAsync(
            [order.Id],
            cancellationToken);
        var plan = ResolvePlan(approvalEvents);
        var previewPlan = plan ??
            (policy is null
                ? null
                : BuildPlanPayload(
                    Guid.NewGuid(),
                    order.CompanyId,
                    order.ProjectId,
                    order.Id,
                    amountTry,
                    policy,
                    budget is null
                        ? null
                        : new BudgetSnapshot(
                            budget.BudgetId,
                            budget.VersionId,
                            budget.Name,
                            budget.AmountTry,
                            committedExcludingOrder ?? 0m,
                            amountAfter ?? amountTry,
                            remainingAfter ?? 0m)));
        var steps = previewPlan is null
            ? Array.Empty<PurchaseOrderApprovalStepResponse>()
            : BuildStepResponses(previewPlan, approvalEvents);
        var currentStep = steps.FirstOrDefault(x => x.Status == "Pending");

        var warnings = new List<string>();
        if (policy is null)
            warnings.Add("Şirket onay politikası yapılandırılmadı.");
        if (policy?.RequireBudget == true && budget is null)
            warnings.Add("Sipariş tarihi için aktif proje bütçesi bulunmuyor.");
        if (remainingAfter.HasValue && remainingAfter.Value < 0m)
            warnings.Add("Sipariş proje bütçesini aşıyor.");
        if (budget is not null && amountAfter.HasValue &&
            UtilizationPercent(budget.AmountTry, amountAfter.Value) >=
            budget.WarningThresholdPercent)
        {
            warnings.Add("Bütçe kullanım oranı uyarı eşiğine ulaştı.");
        }
        if (order.Status == PurchaseOrderStatus.PendingApproval && plan is null)
            warnings.Add("Bu eski sipariş için onay planı ilk kararda oluşturulacak.");

        var project = new ProjectProjection(
            order.ProjectId,
            order.CompanyId,
            order.ProjectCode,
            order.ProjectName);
        var budgetResponse = budget is null
            ? null
            : BuildBudgetResponse(
                budget,
                project,
                RoundTry((committedExcludingOrder ?? 0m) +
                         (IsCommittedStatus(order.Status) ? amountTry : 0m)));

        return new PurchaseOrderApprovalContextResponse(
            order.Id,
            order.OrderNumber,
            order.CompanyId,
            order.ProjectId,
            order.ProjectCode,
            order.ProjectName,
            (int)order.Status,
            amountTry,
            policy is not null,
            policy,
            budgetResponse,
            amountAfter,
            remainingAfter,
            policy?.RequireBudget != true ||
            (budget is not null &&
             remainingAfter.HasValue &&
             remainingAfter.Value >= 0m),
            plan?.PlanId,
            currentStep?.Sequence,
            currentStep?.Name,
            currentStep is not null && CanApprove(currentStep.Code),
            steps,
            warnings);
    }

    public async Task<ProcurementApprovalPolicyResponse> ConfigurePolicyAsync(
        Guid companyId,
        ConfigureProcurementApprovalPolicyRequest request,
        CancellationToken cancellationToken)
    {
        EnsureAnyPermission(PermissionCatalog.Keys.SystemUsersManage);
        ValidatePolicy(request);
        var scope = await GetScopeAsync(cancellationToken);
        var accessibleCompanyId = await scope.Apply(db.Companies.AsNoTracking())
            .Where(x => x.Id == companyId)
            .Select(x => x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (accessibleCompanyId == Guid.Empty)
        {
            throw new ProcurementNotFoundException(
                "Şirket bulunamadı veya erişim yetkiniz yok.");
        }

        var now = DateTime.UtcNow;
        var payload = new PolicyPayload(
            Guid.NewGuid(),
            companyId,
            RoundTry(request.PurchasingApprovalLimitTry),
            RoundTry(request.FinanceApprovalLimitTry),
            request.RequireBudget,
            Clean(request.Note, 1000),
            currentUser.Username,
            now);

        await WriteAuditAsync(
            PolicyConfiguredAction,
            PolicyEntityType,
            companyId,
            payload,
            cancellationToken);

        return ToPolicyResponse(payload);
    }

    public Task<ProcurementBudgetResponse> CreateBudgetAsync(
        Guid projectId,
        UpsertProcurementBudgetRequest request,
        CancellationToken cancellationToken) =>
        SaveBudgetAsync(projectId, null, request, cancellationToken);

    public Task<ProcurementBudgetResponse> UpdateBudgetAsync(
        Guid projectId,
        Guid budgetId,
        UpsertProcurementBudgetRequest request,
        CancellationToken cancellationToken) =>
        SaveBudgetAsync(projectId, budgetId, request, cancellationToken);

    public async Task<PurchaseOrderActionResponse> SubmitOrderAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var scope = await GetScopeAsync(cancellationToken);
        var order = await GetTrackedOrderAsync(purchaseOrderId, scope, cancellationToken);
        if (order.Status != PurchaseOrderStatus.Draft)
            throw new ProcurementValidationException(
                "Yalnız taslak sipariş onaya gönderilebilir.");

        var preparation = await PreparePlanAsync(order, scope, cancellationToken);
        order.Status = PurchaseOrderStatus.PendingApproval;
        order.RejectedByUserId = null;
        order.RejectedAtUtc = null;
        order.RejectionReason = null;

        await WriteAuditAsync(
            ApprovalPlanAction,
            ApprovalEntityType,
            order.Id,
            preparation.Plan,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Action(
            order,
            $"Sipariş {preparation.Plan.Stages.Count} kademeli onaya gönderildi.");
    }

    public async Task<PurchaseOrderActionResponse> ApproveOrderAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var scope = await GetScopeAsync(cancellationToken);
        var order = await GetTrackedOrderAsync(purchaseOrderId, scope, cancellationToken);
        if (order.Status != PurchaseOrderStatus.PendingApproval)
            throw new ProcurementValidationException(
                "Yalnız onay bekleyen sipariş onaylanabilir.");

        var events = await LoadApprovalEventsAsync([order.Id], cancellationToken);
        var plan = ResolvePlan(events);
        if (plan is null)
        {
            var preparation = await PreparePlanAsync(order, scope, cancellationToken);
            plan = preparation.Plan;
            await WriteAuditAsync(
                ApprovalPlanAction,
                ApprovalEntityType,
                order.Id,
                plan,
                cancellationToken);
        }

        var steps = BuildStepResponses(plan, events);
        var currentStep = steps.FirstOrDefault(x => x.Status == "Pending") ??
            throw new ProcurementValidationException(
                "Siparişin bekleyen onay adımı bulunamadı.");
        EnsureCanApprove(currentStep.Code);

        var isFinal = currentStep.Sequence == plan.Stages.Count;
        if (isFinal)
        {
            order.Status = PurchaseOrderStatus.Approved;
            order.ApprovedByUserId = currentUser.UserId;
            order.ApprovedAtUtc = DateTime.UtcNow;
            order.RejectedByUserId = null;
            order.RejectedAtUtc = null;
            order.RejectionReason = null;
        }

        var decision = new ApprovalDecisionPayload(
            Guid.NewGuid(),
            plan.PlanId,
            currentStep.Sequence,
            currentStep.Code,
            currentStep.Name,
            null,
            DateTime.UtcNow);
        await WriteAuditAsync(
            StageApprovedAction,
            ApprovalEntityType,
            order.Id,
            decision,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var message = isFinal
            ? "Satın alma siparişinin tüm onayları tamamlandı."
            : $"{currentStep.Name} tamamlandı; sonraki onay bekleniyor.";
        return Action(order, message);
    }

    public async Task<PurchaseOrderActionResponse> RejectOrderAsync(
        Guid purchaseOrderId,
        string reason,
        CancellationToken cancellationToken)
    {
        var cleanReason = Required(reason, 1000, "Ret nedeni zorunludur.");
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var scope = await GetScopeAsync(cancellationToken);
        var order = await GetTrackedOrderAsync(purchaseOrderId, scope, cancellationToken);
        if (order.Status != PurchaseOrderStatus.PendingApproval)
            throw new ProcurementValidationException(
                "Yalnız onay bekleyen sipariş reddedilebilir.");

        var events = await LoadApprovalEventsAsync([order.Id], cancellationToken);
        var plan = ResolvePlan(events);
        if (plan is null)
        {
            var preparation = await PreparePlanAsync(order, scope, cancellationToken);
            plan = preparation.Plan;
            await WriteAuditAsync(
                ApprovalPlanAction,
                ApprovalEntityType,
                order.Id,
                plan,
                cancellationToken);
        }

        var steps = BuildStepResponses(plan, events);
        var currentStep = steps.FirstOrDefault(x => x.Status == "Pending") ??
            throw new ProcurementValidationException(
                "Siparişin bekleyen onay adımı bulunamadı.");
        EnsureCanApprove(currentStep.Code);

        order.Status = PurchaseOrderStatus.Rejected;
        order.RejectedByUserId = currentUser.UserId;
        order.RejectedAtUtc = DateTime.UtcNow;
        order.RejectionReason = cleanReason;

        var decision = new ApprovalDecisionPayload(
            Guid.NewGuid(),
            plan.PlanId,
            currentStep.Sequence,
            currentStep.Code,
            currentStep.Name,
            cleanReason,
            DateTime.UtcNow);
        await WriteAuditAsync(
            StageRejectedAction,
            ApprovalEntityType,
            order.Id,
            decision,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Action(order, $"Sipariş {currentStep.Name} aşamasında reddedildi.");
    }

    private async Task<ProcurementBudgetResponse> SaveBudgetAsync(
        Guid projectId,
        Guid? budgetId,
        UpsertProcurementBudgetRequest request,
        CancellationToken cancellationToken)
    {
        EnsureAnyPermission(
            PermissionCatalog.Keys.PurchasingApprove,
            PermissionCatalog.Keys.FinanceApprove);
        ValidateBudget(request);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var scope = await GetScopeAsync(cancellationToken);
        var project = await scope.Apply(db.Projects.AsNoTracking())
            .Where(x => x.Id == projectId &&
                        x.IsActive &&
                        x.Status == ProjectStatus.Active)
            .Select(x => new ProjectProjection(x.Id, x.CompanyId, x.Code, x.Name))
            .SingleOrDefaultAsync(cancellationToken) ??
            throw new ProcurementNotFoundException(
                "Proje bulunamadı, aktif değil veya erişim yetkiniz yok.");

        var existing = await LoadBudgetStatesAsync([projectId], cancellationToken);
        if (budgetId.HasValue && existing.All(x => x.BudgetId != budgetId.Value))
            throw new ProcurementNotFoundException("Bütçe kaydı bulunamadı.");

        var start = request.PeriodStart.AsUtc().Date;
        var end = request.PeriodEnd.AsUtc().Date;
        if (request.IsActive && existing.Any(x =>
                x.BudgetId != budgetId &&
                x.IsActive &&
                start <= x.PeriodEnd &&
                end >= x.PeriodStart))
        {
            throw new ProcurementValidationException(
                "Aynı proje için tarihleri çakışan iki aktif bütçe oluşturulamaz.");
        }

        var state = new BudgetState(
            budgetId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            project.CompanyId,
            project.Id,
            Required(request.Name, 200, "Bütçe adı zorunludur."),
            start,
            end,
            RoundTry(request.AmountTry),
            decimal.Round(request.WarningThresholdPercent, 2),
            request.IsActive,
            Clean(request.Note, 1000),
            currentUser.Username,
            DateTime.UtcNow);

        var committed = await GetCommittedAmountAsync(
            scope,
            state,
            null,
            cancellationToken);
        if (state.IsActive && state.AmountTry < committed)
            throw new ProcurementValidationException(
                $"Bütçe tutarı mevcut {committed:N2} TL taahhüdün altına indirilemez.");

        await WriteAuditAsync(
            BudgetConfiguredAction,
            BudgetEntityType,
            project.Id,
            ToBudgetPayload(state),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return BuildBudgetResponse(state, project, committed);
    }

    private async Task<PlanPreparation> PreparePlanAsync(
        PurchaseOrderEntity order,
        CurrentDataScopeSnapshot scope,
        CancellationToken cancellationToken)
    {
        var policy = await LoadPolicyAsync(order.CompanyId, cancellationToken) ??
            throw new ProcurementValidationException(
                "Şirket için satın alma onay politikası yapılandırılmadan sipariş onaya gönderilemez.");
        var amountTry = RoundTry(order.GrandTotal * order.ExchangeRate);
        if (amountTry <= 0m)
            throw new ProcurementValidationException(
                "Siparişin TRY karşılığı sıfırdan büyük olmalıdır.");

        var budgetStates = await LoadBudgetStatesAsync([order.ProjectId], cancellationToken);
        var budget = FindActiveBudget(
            budgetStates,
            order.ProjectId,
            order.OrderDate,
            throwOnOverlap: true);
        if (policy.RequireBudget && budget is null)
            throw new ProcurementValidationException(
                "Sipariş tarihi için aktif proje bütçesi bulunmadan onaya gönderilemez.");

        decimal? committedBefore = null;
        if (budget is not null)
        {
            committedBefore = await GetCommittedAmountAsync(
                scope,
                budget,
                order.Id,
                cancellationToken);
            var amountAfter = RoundTry(committedBefore.Value + amountTry);
            if (amountAfter > budget.AmountTry)
            {
                throw new ProcurementValidationException(
                    $"Sipariş proje bütçesini {amountAfter - budget.AmountTry:N2} TL aşıyor.");
            }
        }

        var plan = BuildPlanPayload(
            Guid.NewGuid(),
            order.CompanyId,
            order.ProjectId,
            order.Id,
            amountTry,
            policy,
            budget is null
                ? null
                : new BudgetSnapshot(
                    budget.BudgetId,
                    budget.VersionId,
                    budget.Name,
                    budget.AmountTry,
                    committedBefore ?? 0m,
                    RoundTry((committedBefore ?? 0m) + amountTry),
                    RoundTry(budget.AmountTry -
                             (committedBefore ?? 0m) -
                             amountTry)));
        return new PlanPreparation(plan);
    }

    private static ApprovalPlanPayload BuildPlanPayload(
        Guid planId,
        Guid companyId,
        Guid projectId,
        Guid purchaseOrderId,
        decimal orderAmountTry,
        ProcurementApprovalPolicyResponse policy,
        BudgetSnapshot? budget)
    {
        var stages = new List<ApprovalPlanStage>
        {
            new(1, PurchasingStageCode, "Satın Alma Onayı", "purchasing.approve")
        };
        if (orderAmountTry > policy.PurchasingApprovalLimitTry)
        {
            stages.Add(new ApprovalPlanStage(
                stages.Count + 1,
                FinanceStageCode,
                "Finans Onayı",
                "finance.approve"));
        }
        if (orderAmountTry > policy.FinanceApprovalLimitTry)
        {
            stages.Add(new ApprovalPlanStage(
                stages.Count + 1,
                ExecutiveStageCode,
                "Genel Müdür Onayı",
                "role:Admin|Genel Müdür"));
        }

        return new ApprovalPlanPayload(
            planId,
            companyId,
            projectId,
            purchaseOrderId,
            orderAmountTry,
            policy.VersionId,
            policy.PurchasingApprovalLimitTry,
            policy.FinanceApprovalLimitTry,
            policy.RequireBudget,
            budget,
            stages,
            DateTime.UtcNow);
    }

    private async Task<ProcurementApprovalPolicyResponse?> LoadPolicyAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var ledger = (await ReadLedgerEventsAsync(
                PolicyEntityType,
                [companyId],
                [PolicyConfiguredAction],
                cancellationToken))
            .OrderByDescending(x => x.OccurredAtUtc)
            .FirstOrDefault();
        if (ledger is null)
            return null;

        var payload = DeserializeRequired<PolicyPayload>(
            ledger.DetailsJson,
            "Onay politikası kaydı okunamadı.");
        return ToPolicyResponse(payload);
    }

    private async Task<List<BudgetState>> LoadBudgetStatesAsync(
        IReadOnlyCollection<Guid> projectIds,
        CancellationToken cancellationToken)
    {
        if (projectIds.Count == 0)
            return [];

        var events = await ReadLedgerEventsAsync(
            BudgetEntityType,
            projectIds,
            [BudgetConfiguredAction],
            cancellationToken);

        var states = new Dictionary<Guid, BudgetState>();
        foreach (var ledger in events)
        {
            var payload = DeserializeRequired<BudgetPayload>(
                ledger.DetailsJson,
                "Bütçe olay kaydı okunamadı.");
            states[payload.BudgetId] = new BudgetState(
                payload.BudgetId,
                payload.VersionId,
                payload.CompanyId,
                payload.ProjectId,
                payload.Name,
                payload.PeriodStart,
                payload.PeriodEnd,
                payload.AmountTry,
                payload.WarningThresholdPercent,
                payload.IsActive,
                payload.Note,
                payload.UpdatedBy ?? ledger.ActorUsername,
                payload.UpdatedAtUtc == default
                    ? ledger.OccurredAtUtc
                    : payload.UpdatedAtUtc);
        }

        return states.Values.ToList();
    }

    private async Task<List<LedgerEvent>> LoadApprovalEventsAsync(
        IReadOnlyCollection<Guid> orderIds,
        CancellationToken cancellationToken)
    {
        if (orderIds.Count == 0)
            return [];

        return await ReadLedgerEventsAsync(
            ApprovalEntityType,
            orderIds,
            [ApprovalPlanAction, StageApprovedAction, StageRejectedAction],
            cancellationToken);
    }

    private async Task<List<LedgerEvent>> ReadLedgerEventsAsync(
        string entityType,
        IReadOnlyCollection<Guid> entityIds,
        IReadOnlyCollection<string> actions,
        CancellationToken cancellationToken)
    {
        var ids = entityIds.Distinct().ToArray();
        var actionNames = actions.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0 || actionNames.Length == 0)
            return [];

        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State == ConnectionState.Closed;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            var entityTypeParameter = AddParameter(
                command,
                "entity_type",
                entityType,
                DbType.String);
            var idParameters = ids
                .Select((id, index) => AddParameter(
                    command,
                    $"entity_id_{index}",
                    id,
                    DbType.Guid))
                .ToArray();
            var actionParameters = actionNames
                .Select((action, index) => AddParameter(
                    command,
                    $"action_{index}",
                    action,
                    DbType.String))
                .ToArray();

            command.CommandText = $"""
                SELECT
                    "Id",
                    "EntityId",
                    "Action",
                    "ActorUserId",
                    "ActorUsername",
                    "DetailsJson"::text,
                    "OccurredAtUtc"
                FROM security_audit_events
                WHERE "EntityType" = {entityTypeParameter}
                  AND "EntityId" IN ({string.Join(", ", idParameters)})
                  AND "Action" IN ({string.Join(", ", actionParameters)})
                ORDER BY "OccurredAtUtc", "Id";
                """;

            var result = new List<LedgerEvent>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(new LedgerEvent(
                    reader.GetGuid(0),
                    reader.IsDBNull(1) ? null : reader.GetGuid(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetGuid(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetDateTime(6)));
            }

            return result;
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private async Task WriteAuditAsync(
        string action,
        string entityType,
        Guid entityId,
        object details,
        CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken);

        var context = getHttpContext?.Invoke();
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State == ConnectionState.Closed;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            var idParameter = AddParameter(command, "id", Guid.NewGuid(), DbType.Guid);
            var actorIdParameter = AddParameter(
                command,
                "actor_user_id",
                currentUser.UserId,
                DbType.Guid);
            var actorNameParameter = AddParameter(
                command,
                "actor_username",
                Truncate(currentUser.Username, 100),
                DbType.String);
            var actionParameter = AddParameter(command, "action", action, DbType.String);
            var entityTypeParameter = AddParameter(
                command,
                "entity_type",
                entityType,
                DbType.String);
            var entityIdParameter = AddParameter(
                command,
                "entity_id",
                entityId,
                DbType.Guid);
            var detailsParameter = AddParameter(
                command,
                "details_json",
                JsonSerializer.Serialize(details),
                DbType.String);
            var ipParameter = AddParameter(
                command,
                "ip_address",
                Truncate(context?.Connection.RemoteIpAddress?.ToString(), 64),
                DbType.String);
            var userAgentParameter = AddParameter(
                command,
                "user_agent",
                Truncate(context?.Request.Headers.UserAgent.ToString(), 500),
                DbType.String);
            var occurredAtParameter = AddParameter(
                command,
                "occurred_at_utc",
                DateTime.UtcNow,
                DbType.DateTime);

            command.CommandText = $"""
                INSERT INTO security_audit_events
                    ("Id", "ActorUserId", "ActorUsername", "Action",
                     "EntityType", "EntityId", "DetailsJson", "IpAddress",
                     "UserAgent", "OccurredAtUtc")
                VALUES
                    ({idParameter}, {actorIdParameter}, {actorNameParameter},
                     {actionParameter}, {entityTypeParameter}, {entityIdParameter},
                     CAST({detailsParameter} AS jsonb), {ipParameter},
                     {userAgentParameter}, {occurredAtParameter});
                """;

            var affected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affected != 1)
                throw new InvalidOperationException("Satın alma denetim olayı yazılamadı.");
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private static string AddParameter(
        DbCommand command,
        string name,
        object? value,
        DbType dbType)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = dbType;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
        return $"@{name}";
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= maxLength
                ? value
                : value[..maxLength];

    private static ApprovalPlanPayload? ResolvePlan(
        IReadOnlyCollection<LedgerEvent> events)
    {
        var ledger = events
            .Where(x => x.Action == ApprovalPlanAction)
            .OrderByDescending(x => x.OccurredAtUtc)
            .FirstOrDefault();
        return ledger is null
            ? null
            : DeserializeRequired<ApprovalPlanPayload>(
                ledger.DetailsJson,
                "Sipariş onay planı okunamadı.");
    }

    private static IReadOnlyList<PurchaseOrderApprovalStepResponse> BuildStepResponses(
        ApprovalPlanPayload plan,
        IReadOnlyCollection<LedgerEvent> events)
    {
        var decisions = events
            .Where(x => x.Action == StageApprovedAction ||
                        x.Action == StageRejectedAction)
            .Select(x => new
            {
                Event = x,
                Payload = DeserializeRequired<ApprovalDecisionPayload>(
                    x.DetailsJson,
                    "Sipariş onay kararı okunamadı.")
            })
            .Where(x => x.Payload.PlanId == plan.PlanId)
            .GroupBy(x => x.Payload.Sequence)
            .ToDictionary(
                x => x.Key,
                x => x.OrderByDescending(item => item.Event.OccurredAtUtc).First());

        return plan.Stages
            .OrderBy(x => x.Sequence)
            .Select(stage =>
            {
                decisions.TryGetValue(stage.Sequence, out var decision);
                var status = decision is null
                    ? "Pending"
                    : decision.Event.Action == StageApprovedAction
                        ? "Approved"
                        : "Rejected";
                return new PurchaseOrderApprovalStepResponse(
                    stage.Sequence,
                    stage.Code,
                    stage.Name,
                    AuthorityLabel(stage.Code),
                    status,
                    decision?.Event.ActorUserId,
                    decision?.Event.ActorUsername,
                    decision?.Event.OccurredAtUtc,
                    decision?.Payload.Note);
            })
            .ToArray();
    }

    private async Task<decimal> GetCommittedAmountAsync(
        CurrentDataScopeSnapshot scope,
        BudgetState budget,
        Guid? excludedOrderId,
        CancellationToken cancellationToken)
    {
        var endExclusive = budget.PeriodEnd.Date.AddDays(1);
        var query = db.PurchaseOrders
            .AsNoTracking()
            .ApplyScope(scope)
            .Where(x => x.ProjectId == budget.ProjectId &&
                        x.OrderDate >= budget.PeriodStart &&
                        x.OrderDate < endExclusive &&
                        (x.Status == PurchaseOrderStatus.PendingApproval ||
                         x.Status == PurchaseOrderStatus.Approved ||
                         x.Status == PurchaseOrderStatus.PartiallyReceived ||
                         x.Status == PurchaseOrderStatus.Completed));
        if (excludedOrderId.HasValue)
            query = query.Where(x => x.Id != excludedOrderId.Value);

        return RoundTry(await query
            .Select(x => x.GrandTotal * x.ExchangeRate)
            .SumAsync(cancellationToken));
    }

    private static BudgetState? FindActiveBudget(
        IEnumerable<BudgetState> budgets,
        Guid projectId,
        DateTime orderDate,
        bool throwOnOverlap)
    {
        var date = orderDate.AsUtc().Date;
        var matches = budgets
            .Where(x => x.ProjectId == projectId &&
                        x.IsActive &&
                        date >= x.PeriodStart.Date &&
                        date <= x.PeriodEnd.Date)
            .ToArray();
        if (throwOnOverlap && matches.Length > 1)
            throw new ProcurementValidationException(
                "Sipariş tarihi için birden fazla aktif bütçe bulundu; bütçe dönemlerini düzeltin.");
        return matches.OrderByDescending(x => x.UpdatedAtUtc).FirstOrDefault();
    }

    private static ProcurementBudgetResponse BuildBudgetResponse(
        BudgetState budget,
        ProjectProjection project,
        decimal committed)
    {
        committed = RoundTry(committed);
        var remaining = RoundTry(budget.AmountTry - committed);
        var utilization = UtilizationPercent(budget.AmountTry, committed);
        return new ProcurementBudgetResponse(
            budget.BudgetId,
            budget.VersionId,
            budget.CompanyId,
            budget.ProjectId,
            project.Code,
            project.Name,
            budget.Name,
            budget.PeriodStart,
            budget.PeriodEnd,
            budget.AmountTry,
            budget.WarningThresholdPercent,
            budget.IsActive,
            budget.Note,
            budget.UpdatedBy,
            budget.UpdatedAtUtc,
            committed,
            remaining,
            utilization,
            budget.IsActive && utilization >= budget.WarningThresholdPercent,
            budget.IsActive && remaining < 0m);
    }

    private static decimal SumCommitments(
        IEnumerable<CommitmentProjection> rows,
        BudgetState budget) =>
        RoundTry(rows
            .Where(x => x.ProjectId == budget.ProjectId &&
                        x.OrderDate.Date >= budget.PeriodStart.Date &&
                        x.OrderDate.Date <= budget.PeriodEnd.Date)
            .Sum(x => x.AmountTry));

    private async Task<PurchaseOrderEntity> GetTrackedOrderAsync(
        Guid purchaseOrderId,
        CurrentDataScopeSnapshot scope,
        CancellationToken cancellationToken) =>
        await db.PurchaseOrders
            .ApplyScope(scope)
            .SingleOrDefaultAsync(x => x.Id == purchaseOrderId, cancellationToken) ??
        throw new ProcurementNotFoundException("Satın alma siparişi bulunamadı.");

    private static bool IsCommittedStatus(PurchaseOrderStatus status) =>
        status == PurchaseOrderStatus.PendingApproval ||
        status == PurchaseOrderStatus.Approved ||
        status == PurchaseOrderStatus.PartiallyReceived ||
        status == PurchaseOrderStatus.Completed;

    private void EnsureAnyPermission(params string[] permissions)
    {
        if (!permissions.Any(currentUser.HasPermission))
            throw new UnauthorizedAccessException(
                "Bu satın alma işlemi için gerekli yetkiniz bulunmuyor.");
    }

    private bool CanApprove(string stageCode) => stageCode switch
    {
        PurchasingStageCode => currentUser.HasPermission(
            PermissionCatalog.Keys.PurchasingApprove),
        FinanceStageCode => currentUser.HasPermission(
            PermissionCatalog.Keys.FinanceApprove),
        ExecutiveStageCode => currentUser.IsInRole("Admin") ||
                              currentUser.IsInRole("Genel Müdür"),
        _ => false
    };

    private void EnsureCanApprove(string stageCode)
    {
        if (!CanApprove(stageCode))
            throw new UnauthorizedAccessException(
                $"Bu adım için {AuthorityLabel(stageCode)} gereklidir.");
    }

    private static string AuthorityLabel(string stageCode) => stageCode switch
    {
        PurchasingStageCode => "Satın alma onay yetkisi",
        FinanceStageCode => "Finans onay yetkisi",
        ExecutiveStageCode => "Admin veya Genel Müdür rolü",
        _ => "Tanımsız yetki"
    };

    private static void ValidatePolicy(
        ConfigureProcurementApprovalPolicyRequest request)
    {
        if (request.PurchasingApprovalLimitTry <= 0m)
            throw new ProcurementValidationException(
                "Satın alma onay limiti sıfırdan büyük olmalıdır.");
        if (request.FinanceApprovalLimitTry <= request.PurchasingApprovalLimitTry)
            throw new ProcurementValidationException(
                "Finans onay limiti satın alma limitinden büyük olmalıdır.");
        _ = Clean(request.Note, 1000);
    }

    private static void ValidateBudget(UpsertProcurementBudgetRequest request)
    {
        _ = Required(request.Name, 200, "Bütçe adı zorunludur.");
        if (request.PeriodEnd.AsUtc().Date < request.PeriodStart.AsUtc().Date)
            throw new ProcurementValidationException(
                "Bütçe bitiş tarihi başlangıç tarihinden önce olamaz.");
        if (request.AmountTry <= 0m)
            throw new ProcurementValidationException(
                "Bütçe tutarı sıfırdan büyük olmalıdır.");
        if (request.WarningThresholdPercent <= 0m ||
            request.WarningThresholdPercent > 100m)
        {
            throw new ProcurementValidationException(
                "Bütçe uyarı eşiği 0 ile 100 arasında olmalıdır.");
        }
        _ = Clean(request.Note, 1000);
    }

    private async Task<CurrentDataScopeSnapshot> GetScopeAsync(
        CancellationToken cancellationToken) =>
        await dataScope.GetAsync(cancellationToken) ??
        throw new UnauthorizedAccessException("Kullanıcı veri kapsamı bulunamadı.");

    private static ProcurementApprovalPolicyResponse ToPolicyResponse(
        PolicyPayload payload) =>
        new(
            payload.VersionId,
            payload.CompanyId,
            payload.PurchasingApprovalLimitTry,
            payload.FinanceApprovalLimitTry,
            payload.RequireBudget,
            payload.Note,
            payload.UpdatedBy,
            payload.UpdatedAtUtc);

    private static BudgetPayload ToBudgetPayload(BudgetState state) =>
        new(
            state.BudgetId,
            state.VersionId,
            state.CompanyId,
            state.ProjectId,
            state.Name,
            state.PeriodStart,
            state.PeriodEnd,
            state.AmountTry,
            state.WarningThresholdPercent,
            state.IsActive,
            state.Note,
            state.UpdatedBy,
            state.UpdatedAtUtc);

    private static T DeserializeRequired<T>(string? json, string message)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ProcurementValidationException(message);
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ??
                   throw new ProcurementValidationException(message);
        }
        catch (JsonException)
        {
            throw new ProcurementValidationException(message);
        }
    }

    private static string Required(
        string? value,
        int maxLength,
        string message)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ProcurementValidationException(message);
        var clean = value.Trim();
        if (clean.Length > maxLength)
            throw new ProcurementValidationException(
                $"Metin en fazla {maxLength} karakter olabilir.");
        return clean;
    }

    private static string? Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var clean = value.Trim();
        if (clean.Length > maxLength)
            throw new ProcurementValidationException(
                $"Metin en fazla {maxLength} karakter olabilir.");
        return clean;
    }

    private static decimal RoundTry(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal UtilizationPercent(decimal budget, decimal used) =>
        budget <= 0m
            ? 0m
            : decimal.Round(used / budget * 100m, 2);

    private static PurchaseOrderActionResponse Action(
        PurchaseOrderEntity order,
        string message) =>
        new(order.Id, order.OrderNumber, (int)order.Status, message);

    private sealed record CompanyProjection(Guid Id, string Code, string Name);

    private sealed record ProjectProjection(
        Guid Id,
        Guid CompanyId,
        string Code,
        string Name);

    private sealed record CommitmentProjection(
        Guid ProjectId,
        DateTime OrderDate,
        decimal AmountTry);

    private sealed record PendingOrderProjection(
        Guid Id,
        string OrderNumber,
        Guid CompanyId,
        Guid ProjectId,
        string ProjectCode,
        string ProjectName,
        string SupplierTitle,
        DateTime OrderDate,
        decimal OrderAmountTry);

    private sealed record OrderContextProjection(
        Guid Id,
        string OrderNumber,
        Guid CompanyId,
        Guid ProjectId,
        string ProjectCode,
        string ProjectName,
        DateTime OrderDate,
        decimal OrderAmountTry,
        PurchaseOrderStatus Status);

    private sealed record LedgerEvent(
        Guid Id,
        Guid? EntityId,
        string Action,
        Guid? ActorUserId,
        string? ActorUsername,
        string? DetailsJson,
        DateTime OccurredAtUtc);

    private sealed record PolicyPayload(
        Guid VersionId,
        Guid CompanyId,
        decimal PurchasingApprovalLimitTry,
        decimal FinanceApprovalLimitTry,
        bool RequireBudget,
        string? Note,
        string? UpdatedBy,
        DateTime UpdatedAtUtc);

    private sealed record BudgetPayload(
        Guid BudgetId,
        Guid VersionId,
        Guid CompanyId,
        Guid ProjectId,
        string Name,
        DateTime PeriodStart,
        DateTime PeriodEnd,
        decimal AmountTry,
        decimal WarningThresholdPercent,
        bool IsActive,
        string? Note,
        string? UpdatedBy,
        DateTime UpdatedAtUtc);

    private sealed record BudgetState(
        Guid BudgetId,
        Guid VersionId,
        Guid CompanyId,
        Guid ProjectId,
        string Name,
        DateTime PeriodStart,
        DateTime PeriodEnd,
        decimal AmountTry,
        decimal WarningThresholdPercent,
        bool IsActive,
        string? Note,
        string? UpdatedBy,
        DateTime UpdatedAtUtc);

    private sealed record BudgetSnapshot(
        Guid BudgetId,
        Guid BudgetVersionId,
        string Name,
        decimal AmountTry,
        decimal CommittedBeforeTry,
        decimal CommittedAfterTry,
        decimal RemainingAfterTry);

    private sealed record ApprovalPlanStage(
        int Sequence,
        string Code,
        string Name,
        string RequiredAuthority);

    private sealed record ApprovalPlanPayload(
        Guid PlanId,
        Guid CompanyId,
        Guid ProjectId,
        Guid PurchaseOrderId,
        decimal OrderAmountTry,
        Guid PolicyVersionId,
        decimal PurchasingApprovalLimitTry,
        decimal FinanceApprovalLimitTry,
        bool BudgetRequired,
        BudgetSnapshot? Budget,
        IReadOnlyList<ApprovalPlanStage> Stages,
        DateTime CreatedAtUtc);

    private sealed record ApprovalDecisionPayload(
        Guid DecisionId,
        Guid PlanId,
        int Sequence,
        string Code,
        string Name,
        string? Note,
        DateTime DecidedAtUtc);

    private sealed record PlanPreparation(ApprovalPlanPayload Plan);
}
