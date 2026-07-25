namespace ApprovalFlow.Domain;

public sealed class PurchaseRequestAuditEntry
{
    private PurchaseRequestAuditEntry() { }

    internal PurchaseRequestAuditEntry(
        Guid purchaseRequestId,
        string actor,
        DateTimeOffset occurredAt,
        PurchaseRequestStatus fromStatus,
        PurchaseRequestStatus toStatus,
        string? reason)
    {
        PurchaseRequestId = purchaseRequestId;
        Actor = actor;
        OccurredAt = occurredAt;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Reason = reason;
    }

    public Guid Id { get; private set; }
    public Guid PurchaseRequestId { get; private set; }
    public string Actor { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }
    public PurchaseRequestStatus FromStatus { get; private set; }
    public PurchaseRequestStatus ToStatus { get; private set; }
    public string? Reason { get; private set; }
}
