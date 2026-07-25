using ApprovalFlow.Domain;

namespace ApprovalFlow.Application;

public sealed class PurchaseRequestService(IPurchaseRequestRepository repository, IClock clock)
{
    public async Task<PurchaseRequestResult> CreateAsync(
        CreatePurchaseRequestCommand command,
        AuthenticatedActor actor,
        CancellationToken cancellationToken)
    {
        RequireRole(actor, ApprovalFlowRoles.Employee);
        var request = PurchaseRequest.Create(
            command.Vendor,
            command.CostCenter,
            command.Category,
            command.BusinessJustification,
            command.RequestedDeliveryDate,
            actor.UserName,
            CreateItems(command.LineItems),
            clock.UtcNow);

        await repository.AddAsync(request, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        await repository.RefreshRowVersionAsync(request, cancellationToken);
        return Map(request);
    }

    public async Task<PurchaseRequestResult?> GetAsync(
        Guid id,
        AuthenticatedActor actor,
        CancellationToken cancellationToken)
    {
        var request = await repository.GetAsync(id, cancellationToken);
        if (request is null)
            return null;
        AuthorizeView(request, actor);
        return Map(request);
    }

    public Task<PurchaseRequestPage> ListOwnedAsync(
        AuthenticatedActor actor,
        PurchaseRequestStatus? status,
        int page,
        int pageSize,
        PurchaseRequestSort sort,
        CancellationToken cancellationToken)
    {
        RequireRole(actor, ApprovalFlowRoles.Employee);
        return repository.ListAsync(
            new PurchaseRequestListQuery(
                PurchaseRequestListScope.Owned,
                actor.UserName,
                status,
                ValidatePage(page),
                ValidatePageSize(pageSize),
                sort),
            cancellationToken);
    }

    public Task<PurchaseRequestPage> ListManagerQueueAsync(
        AuthenticatedActor actor,
        int page,
        int pageSize,
        PurchaseRequestSort sort,
        CancellationToken cancellationToken)
    {
        RequireRole(actor, ApprovalFlowRoles.Manager);
        return repository.ListAsync(
            new PurchaseRequestListQuery(
                PurchaseRequestListScope.ManagerQueue,
                null,
                PurchaseRequestStatus.PendingManagerApproval,
                ValidatePage(page),
                ValidatePageSize(pageSize),
                sort),
            cancellationToken);
    }

    public Task<PurchaseRequestPage> ListFinanceQueueAsync(
        AuthenticatedActor actor,
        int page,
        int pageSize,
        PurchaseRequestSort sort,
        CancellationToken cancellationToken)
    {
        RequireRole(actor, ApprovalFlowRoles.FinanceAdministrator);
        return repository.ListAsync(
            new PurchaseRequestListQuery(
                PurchaseRequestListScope.FinanceQueue,
                null,
                PurchaseRequestStatus.PendingFinanceApproval,
                ValidatePage(page),
                ValidatePageSize(pageSize),
                sort),
            cancellationToken);
    }

    public Task<PurchaseRequestResult?> SubmitAsync(
        Guid id,
        TransitionPurchaseRequestCommand command,
        AuthenticatedActor actor,
        CancellationToken cancellationToken) =>
        MutateAsync(id, command.RowVersion, actor, cancellationToken,
            request =>
            {
                RequireOwner(request, actor);
                request.Submit(actor.UserName, clock.UtcNow, command.Reason);
            });

    public Task<PurchaseRequestResult?> ReviseAsync(
        Guid id,
        RevisePurchaseRequestCommand command,
        AuthenticatedActor actor,
        CancellationToken cancellationToken) =>
        MutateAsync(id, command.RowVersion, actor, cancellationToken,
            request =>
            {
                RequireOwner(request, actor);
                var lineItems = CreateItems(command.LineItems).ToArray();
                request.Revise(
                    actor.UserName,
                    command.Vendor,
                    command.CostCenter,
                    command.Category,
                    command.BusinessJustification,
                    command.RequestedDeliveryDate,
                    lineItems,
                    clock.UtcNow,
                    command.Reason);
                repository.AddLineItems(lineItems);
            });

    public Task<PurchaseRequestResult?> ApproveAsync(
        Guid id,
        TransitionPurchaseRequestCommand command,
        AuthenticatedActor actor,
        CancellationToken cancellationToken) =>
        MutateAsync(id, command.RowVersion, actor, cancellationToken,
            request =>
            {
                if (request.Status == PurchaseRequestStatus.PendingManagerApproval)
                {
                    RequireRole(actor, ApprovalFlowRoles.Manager);
                    request.ApproveAsManager(actor.UserName, clock.UtcNow, command.Reason);
                }
                else if (request.Status == PurchaseRequestStatus.PendingFinanceApproval)
                {
                    RequireRole(actor, ApprovalFlowRoles.FinanceAdministrator);
                    request.ApproveAsFinance(actor.UserName, clock.UtcNow, command.Reason);
                }
                else
                {
                    throw new DomainConflictException($"A request in {request.Status} cannot be approved.");
                }
            });

    public Task<PurchaseRequestResult?> RejectAsync(
        Guid id,
        RequiredReasonTransitionCommand command,
        AuthenticatedActor actor,
        CancellationToken cancellationToken) =>
        ReviewDecisionAsync(id, command.RowVersion, command.Reason, actor, cancellationToken,
            (request, name, now, reason) => request.Reject(name, now, reason));

    public Task<PurchaseRequestResult?> ReturnAsync(
        Guid id,
        RequiredReasonTransitionCommand command,
        AuthenticatedActor actor,
        CancellationToken cancellationToken) =>
        ReviewDecisionAsync(id, command.RowVersion, command.Reason, actor, cancellationToken,
            (request, name, now, reason) => request.ReturnForChanges(name, now, reason));

    private Task<PurchaseRequestResult?> ReviewDecisionAsync(
        Guid id,
        string rowVersion,
        string reason,
        AuthenticatedActor actor,
        CancellationToken cancellationToken,
        Action<PurchaseRequest, string, DateTimeOffset, string> decision) =>
        MutateAsync(id, rowVersion, actor, cancellationToken,
            request =>
            {
                RequireRoleForCurrentReview(request, actor);
                decision(request, actor.UserName, clock.UtcNow, reason);
            });

    private async Task<PurchaseRequestResult?> MutateAsync(
        Guid id,
        string rowVersion,
        AuthenticatedActor actor,
        CancellationToken cancellationToken,
        Action<PurchaseRequest> mutation)
    {
        var request = await repository.GetAsync(id, cancellationToken);
        if (request is null)
            return null;
        var expected = ParseRowVersion(rowVersion);
        repository.SetExpectedRowVersion(request, expected);
        mutation(request);
        await repository.SaveChangesAsync(cancellationToken);
        await repository.RefreshRowVersionAsync(request, cancellationToken);
        return Map(request);
    }

    private static void AuthorizeView(PurchaseRequest request, AuthenticatedActor actor)
    {
        if (actor.IsInRole(ApprovalFlowRoles.Employee)
            && string.Equals(request.Requester, actor.UserName, StringComparison.OrdinalIgnoreCase))
            return;
        if (actor.IsInRole(ApprovalFlowRoles.Manager)
            && request.Status is not PurchaseRequestStatus.Draft)
            return;
        if (actor.IsInRole(ApprovalFlowRoles.FinanceAdministrator)
            && request.RequiresFinanceApproval
            && request.Status is not PurchaseRequestStatus.Draft)
            return;
        throw new DomainAuthorizationException("You are not authorized to view this request.");
    }

    private static void RequireOwner(PurchaseRequest request, AuthenticatedActor actor)
    {
        RequireRole(actor, ApprovalFlowRoles.Employee);
        if (!string.Equals(request.Requester, actor.UserName, StringComparison.OrdinalIgnoreCase))
            throw new DomainAuthorizationException("Employees may act only on their own requests.");
    }

    private static void RequireRoleForCurrentReview(PurchaseRequest request, AuthenticatedActor actor)
    {
        if (request.Status == PurchaseRequestStatus.PendingManagerApproval)
            RequireRole(actor, ApprovalFlowRoles.Manager);
        else if (request.Status == PurchaseRequestStatus.PendingFinanceApproval)
            RequireRole(actor, ApprovalFlowRoles.FinanceAdministrator);
        else
            throw new DomainConflictException($"A request in {request.Status} is not eligible for review.");
    }

    private static void RequireRole(AuthenticatedActor actor, string role)
    {
        if (!actor.IsInRole(role))
            throw new DomainAuthorizationException($"The {role} role is required.");
    }

    private static byte[] ParseRowVersion(string value)
    {
        try
        {
            var parsed = Convert.FromBase64String(value);
            if (parsed.Length == 0)
                throw new FormatException();
            return parsed;
        }
        catch (FormatException)
        {
            throw new DomainValidationException("RowVersion must be a non-empty base64 value.");
        }
    }

    private static int ValidatePage(int page)
    {
        if (page < 1)
            throw new DomainValidationException("Page must be at least 1.");
        return page;
    }

    private static int ValidatePageSize(int pageSize)
    {
        if (pageSize is < 1 or > 50)
            throw new DomainValidationException("PageSize must be between 1 and 50.");
        return pageSize;
    }

    private static IEnumerable<PurchaseRequestLineItem> CreateItems(IEnumerable<CreateLineItem> items) =>
        items.Select(item => new PurchaseRequestLineItem(item.Description, item.Quantity, item.UnitPrice));

    private static PurchaseRequestResult Map(PurchaseRequest request) =>
        new(
            request.Id,
            request.Vendor,
            request.CostCenter,
            request.Category,
            request.BusinessJustification,
            request.RequestedDeliveryDate,
            request.Requester,
            request.Status,
            request.Total,
            request.RequiresFinanceApproval,
            request.CreatedAt,
            request.LastModifiedAt,
            Convert.ToBase64String(request.RowVersion),
            request.LineItems.Select(item => new LineItemResult(
                item.Id, item.Description, item.Quantity, item.UnitPrice, item.LineTotal)).ToArray(),
            request.AuditEntries.OrderBy(entry => entry.OccurredAt).Select(entry => new AuditEntryResult(
                entry.Id,
                entry.Actor,
                entry.OccurredAt,
                entry.FromStatus,
                entry.ToStatus,
                entry.Reason)).ToArray());
}
