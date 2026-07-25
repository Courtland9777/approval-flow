namespace ApprovalFlow.Domain;

public sealed class PurchaseRequest
{
    private readonly List<PurchaseRequestLineItem> _lineItems = [];
    private readonly List<PurchaseRequestAuditEntry> _auditEntries = [];

    private PurchaseRequest() { }

    public static PurchaseRequest Create(
        string vendor,
        string costCenter,
        string category,
        string businessJustification,
        DateOnly requestedDeliveryDate,
        string requester,
        IEnumerable<PurchaseRequestLineItem> lineItems,
        DateTimeOffset createdAt)
    {
        Require(vendor, "Vendor");
        Require(costCenter, "Cost center");
        Require(category, "Category");
        Require(businessJustification, "Business justification");
        Require(requester, "Requester");

        var items = lineItems.ToList();
        if (items.Count == 0)
            throw new DomainValidationException("At least one line item is required.");

        var request = new PurchaseRequest
        {
            Id = Guid.NewGuid(),
            Vendor = vendor.Trim(),
            CostCenter = costCenter.Trim(),
            Category = category.Trim(),
            BusinessJustification = businessJustification.Trim(),
            RequestedDeliveryDate = requestedDeliveryDate,
            Requester = requester.Trim(),
            Status = PurchaseRequestStatus.Draft,
            CreatedAt = createdAt,
            LastModifiedAt = createdAt
        };
        request._lineItems.AddRange(items);
        return request;
    }

    public Guid Id { get; private set; }
    public string Vendor { get; private set; } = string.Empty;
    public string CostCenter { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public string BusinessJustification { get; private set; } = string.Empty;
    public DateOnly RequestedDeliveryDate { get; private set; }
    public string Requester { get; private set; } = string.Empty;
    public PurchaseRequestStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastModifiedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public decimal Total => _lineItems.Sum(item => item.LineTotal);
    public IReadOnlyCollection<PurchaseRequestLineItem> LineItems => _lineItems.AsReadOnly();
    public IReadOnlyCollection<PurchaseRequestAuditEntry> AuditEntries => _auditEntries.AsReadOnly();

    public bool RequiresFinanceApproval =>
        Total >= 1000m
        || Category.Equals("Software", StringComparison.OrdinalIgnoreCase)
        || Category.Equals("Security", StringComparison.OrdinalIgnoreCase);

    public void Submit(string actor, DateTimeOffset occurredAt, string? reason)
    {
        Require(actor, "Actor");
        if (Status != PurchaseRequestStatus.Draft)
            throw new DomainConflictException($"A request in {Status} cannot be submitted.");
        if (!string.Equals(actor.Trim(), Requester, StringComparison.OrdinalIgnoreCase))
            throw new DomainAuthorizationException("Only the requester can submit this request.");

        var previous = Status;
        Status = PurchaseRequestStatus.PendingManagerApproval;
        LastModifiedAt = occurredAt;
        _auditEntries.Add(new PurchaseRequestAuditEntry(Id, actor.Trim(), occurredAt, previous, Status, Normalize(reason)));
    }

    public void ApproveAsManager(string actor, DateTimeOffset occurredAt, string? reason)
    {
        RequireDecisionActor(actor);
        RequireStatus(PurchaseRequestStatus.PendingManagerApproval, "approved by a manager");
        Transition(
            RequiresFinanceApproval ? PurchaseRequestStatus.PendingFinanceApproval : PurchaseRequestStatus.Approved,
            actor,
            occurredAt,
            reason);
    }

    public void ApproveAsFinance(string actor, DateTimeOffset occurredAt, string? reason)
    {
        RequireDecisionActor(actor);
        RequireStatus(PurchaseRequestStatus.PendingFinanceApproval, "approved by finance");
        Transition(PurchaseRequestStatus.Approved, actor, occurredAt, reason);
    }

    public void Reject(string actor, DateTimeOffset occurredAt, string reason)
    {
        RequireDecisionActor(actor);
        RequireReviewState("rejected");
        Require(reason, "Reason");
        Transition(PurchaseRequestStatus.Rejected, actor, occurredAt, reason);
    }

    public void ReturnForChanges(string actor, DateTimeOffset occurredAt, string reason)
    {
        RequireDecisionActor(actor);
        RequireReviewState("returned");
        Require(reason, "Reason");
        Transition(PurchaseRequestStatus.ReturnedForChanges, actor, occurredAt, reason);
    }

    public void Revise(
        string actor,
        string vendor,
        string costCenter,
        string category,
        string businessJustification,
        DateOnly requestedDeliveryDate,
        IEnumerable<PurchaseRequestLineItem> lineItems,
        DateTimeOffset occurredAt,
        string? reason)
    {
        Require(actor, "Actor");
        if (!string.Equals(actor.Trim(), Requester, StringComparison.OrdinalIgnoreCase))
            throw new DomainAuthorizationException("Only the requester can revise this request.");
        if (Status is not PurchaseRequestStatus.Draft and not PurchaseRequestStatus.ReturnedForChanges)
            throw new DomainConflictException($"A request in {Status} cannot be revised.");

        Require(vendor, "Vendor");
        Require(costCenter, "Cost center");
        Require(category, "Category");
        Require(businessJustification, "Business justification");
        var items = lineItems.ToList();
        if (items.Count == 0)
            throw new DomainValidationException("At least one line item is required.");

        var previous = Status;
        Vendor = vendor.Trim();
        CostCenter = costCenter.Trim();
        Category = category.Trim();
        BusinessJustification = businessJustification.Trim();
        RequestedDeliveryDate = requestedDeliveryDate;
        _lineItems.Clear();
        _lineItems.AddRange(items);
        Status = PurchaseRequestStatus.Draft;
        LastModifiedAt = occurredAt;
        _auditEntries.Add(new PurchaseRequestAuditEntry(
            Id, actor.Trim(), occurredAt, previous, Status, Normalize(reason) ?? "Request revised."));
    }

    private void RequireDecisionActor(string actor)
    {
        Require(actor, "Actor");
        if (string.Equals(actor.Trim(), Requester, StringComparison.OrdinalIgnoreCase))
            throw new DomainAuthorizationException("A requester cannot make a decision on their own request.");
    }

    private void RequireReviewState(string action)
    {
        if (Status is not PurchaseRequestStatus.PendingManagerApproval
            and not PurchaseRequestStatus.PendingFinanceApproval)
            throw new DomainConflictException($"A request in {Status} cannot be {action}.");
    }

    private void RequireStatus(PurchaseRequestStatus expected, string action)
    {
        if (Status != expected)
            throw new DomainConflictException($"A request in {Status} cannot be {action}.");
    }

    private void Transition(
        PurchaseRequestStatus next,
        string actor,
        DateTimeOffset occurredAt,
        string? reason)
    {
        var previous = Status;
        Status = next;
        LastModifiedAt = occurredAt;
        _auditEntries.Add(new PurchaseRequestAuditEntry(
            Id, actor.Trim(), occurredAt, previous, next, Normalize(reason)));
    }

    private static void Require(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainValidationException($"{field} is required.");
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
