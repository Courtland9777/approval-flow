using System.ComponentModel.DataAnnotations;
using ApprovalFlow.Application;

namespace ApprovalFlow.Api;

public sealed record CreatePurchaseRequestRequest(
    [property: Required, StringLength(200)] string Vendor,
    [property: Required, StringLength(50)] string CostCenter,
    [property: Required, StringLength(100)] string Category,
    [property: Required, StringLength(2000, MinimumLength = 10)] string BusinessJustification,
    DateOnly RequestedDeliveryDate,
    [property: Required, MinLength(1)] IReadOnlyCollection<CreateLineItemRequest> LineItems)
{
    public CreatePurchaseRequestCommand ToCommand() =>
        new(Vendor, CostCenter, Category, BusinessJustification, RequestedDeliveryDate,
            LineItems.Select(item => item.ToCommand()).ToArray());
}

public sealed record RevisePurchaseRequestRequest(
    [property: Required, StringLength(200)] string Vendor,
    [property: Required, StringLength(50)] string CostCenter,
    [property: Required, StringLength(100)] string Category,
    [property: Required, StringLength(2000, MinimumLength = 10)] string BusinessJustification,
    DateOnly RequestedDeliveryDate,
    [property: Required, MinLength(1)] IReadOnlyCollection<CreateLineItemRequest> LineItems,
    [property: Required] string RowVersion,
    [property: StringLength(1000)] string? Reason)
{
    public RevisePurchaseRequestCommand ToCommand() =>
        new(Vendor, CostCenter, Category, BusinessJustification, RequestedDeliveryDate,
            LineItems.Select(item => item.ToCommand()).ToArray(), RowVersion, Reason);
}

public sealed record CreateLineItemRequest(
    [property: Required, StringLength(500)] string Description,
    [property: Range(1, int.MaxValue)] int Quantity,
    [property: Range(typeof(decimal), "0.01", "9999999999999999")] decimal UnitPrice)
{
    public CreateLineItem ToCommand() => new(Description, Quantity, UnitPrice);
}

public sealed record TransitionPurchaseRequestRequest(
    [property: Required] string RowVersion,
    [property: StringLength(1000)] string? Reason)
{
    public TransitionPurchaseRequestCommand ToCommand() => new(RowVersion, Reason);
}

public sealed record RequiredReasonTransitionRequest(
    [property: Required] string RowVersion,
    [property: Required, StringLength(1000, MinimumLength = 1)] string Reason)
{
    public RequiredReasonTransitionCommand ToCommand() => new(RowVersion, Reason);
}
