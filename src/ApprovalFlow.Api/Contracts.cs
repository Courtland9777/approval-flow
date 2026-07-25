using System.ComponentModel.DataAnnotations;
using ApprovalFlow.Application;

namespace ApprovalFlow.Api;

public sealed record CreatePurchaseRequestRequest(
    [property: Required, StringLength(200)] string Vendor,
    [property: Required, StringLength(50)] string CostCenter,
    [property: Required, StringLength(100)] string Category,
    [property: Required, StringLength(2000, MinimumLength = 10)] string BusinessJustification,
    DateOnly RequestedDeliveryDate,
    [property: Required, StringLength(100)] string Requester,
    [property: Required, MinLength(1)] IReadOnlyCollection<CreateLineItemRequest> LineItems)
{
    public CreatePurchaseRequestCommand ToCommand() =>
        new(Vendor, CostCenter, Category, BusinessJustification, RequestedDeliveryDate, Requester,
            LineItems.Select(item => new CreateLineItem(item.Description, item.Quantity, item.UnitPrice)).ToArray());
}

public sealed record CreateLineItemRequest(
    [property: Required, StringLength(500)] string Description,
    [property: Range(1, int.MaxValue)] int Quantity,
    [property: Range(typeof(decimal), "0.01", "9999999999999999")] decimal UnitPrice);

public sealed record SubmitPurchaseRequestRequest(
    [property: Required, StringLength(100)] string Actor,
    [property: StringLength(1000)] string? Reason)
{
    public SubmitPurchaseRequestCommand ToCommand() => new(Actor, Reason);
}
