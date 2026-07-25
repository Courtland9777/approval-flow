using ApprovalFlow.Domain;

namespace ApprovalFlow.Application;

public static class ApprovalFlowRoles
{
    public const string Employee = "Employee";
    public const string Manager = "Manager";
    public const string FinanceAdministrator = "FinanceAdministrator";
}

public sealed record AuthenticatedActor(string UserName, IReadOnlySet<string> Roles)
{
    public bool IsInRole(string role) => Roles.Contains(role);
}

public sealed record CreatePurchaseRequestCommand(
    string Vendor,
    string CostCenter,
    string Category,
    string BusinessJustification,
    DateOnly RequestedDeliveryDate,
    IReadOnlyCollection<CreateLineItem> LineItems);

public sealed record RevisePurchaseRequestCommand(
    string Vendor,
    string CostCenter,
    string Category,
    string BusinessJustification,
    DateOnly RequestedDeliveryDate,
    IReadOnlyCollection<CreateLineItem> LineItems,
    string RowVersion,
    string? Reason);

public sealed record TransitionPurchaseRequestCommand(string RowVersion, string? Reason);
public sealed record RequiredReasonTransitionCommand(string RowVersion, string Reason);
public sealed record CreateLineItem(string Description, int Quantity, decimal UnitPrice);

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
    bool RequiresFinanceApproval,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastModifiedAt,
    string RowVersion,
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
