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
            CreatedAt = createdAt
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
    public decimal Total => _lineItems.Sum(item => item.LineTotal);
    public IReadOnlyCollection<PurchaseRequestLineItem> LineItems => _lineItems.AsReadOnly();
    public IReadOnlyCollection<PurchaseRequestAuditEntry> AuditEntries => _auditEntries.AsReadOnly();

    public void Submit(string actor, DateTimeOffset occurredAt, string? reason)
    {
        Require(actor, "Actor");
        if (Status != PurchaseRequestStatus.Draft)
            throw new DomainValidationException($"A request in {Status} cannot be submitted.");
        if (!string.Equals(actor.Trim(), Requester, StringComparison.OrdinalIgnoreCase))
            throw new DomainValidationException("Only the requester can submit this request.");

        var previous = Status;
        Status = PurchaseRequestStatus.Submitted;
        _auditEntries.Add(new PurchaseRequestAuditEntry(Id, actor.Trim(), occurredAt, previous, Status, Normalize(reason)));
    }

    private static void Require(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainValidationException($"{field} is required.");
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
