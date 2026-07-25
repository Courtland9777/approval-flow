using ApprovalFlow.Domain;

namespace ApprovalFlow.Application;

public sealed record CreatePurchaseRequestCommand(
    string Vendor,
    string CostCenter,
    string Category,
    string BusinessJustification,
    DateOnly RequestedDeliveryDate,
    string Requester,
    IReadOnlyCollection<CreateLineItem> LineItems);

public sealed record CreateLineItem(string Description, int Quantity, decimal UnitPrice);
public sealed record SubmitPurchaseRequestCommand(string Actor, string? Reason);

public sealed record PurchaseRequestResult(
    Guid Id,
    string Vendor,
    string CostCenter,
    string Category,
    string BusinessJustification,
    DateOnly RequestedDeliveryDate,
    string Requester,
    PurchaseRequestStatus Status,
    decimal Total,
    DateTimeOffset CreatedAt,
    IReadOnlyCollection<LineItemResult> LineItems,
    IReadOnlyCollection<AuditEntryResult> AuditEntries);

public sealed record LineItemResult(Guid Id, string Description, int Quantity, decimal UnitPrice, decimal LineTotal);
public sealed record AuditEntryResult(
    Guid Id,
    string Actor,
    DateTimeOffset OccurredAt,
    PurchaseRequestStatus FromStatus,
    PurchaseRequestStatus ToStatus,
    string? Reason);
