using ApprovalFlow.Domain;

namespace ApprovalFlow.Application;

public sealed class PurchaseRequestService(IPurchaseRequestRepository repository, IClock clock)
{
    public async Task<PurchaseRequestResult> CreateAsync(
        CreatePurchaseRequestCommand command,
        CancellationToken cancellationToken)
    {
        var items = command.LineItems.Select(item =>
            new PurchaseRequestLineItem(item.Description, item.Quantity, item.UnitPrice));
        var request = PurchaseRequest.Create(
            command.Vendor,
            command.CostCenter,
            command.Category,
            command.BusinessJustification,
            command.RequestedDeliveryDate,
            command.Requester,
            items,
            clock.UtcNow);

        await repository.AddAsync(request, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return Map(request);
    }

    public async Task<PurchaseRequestResult?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var request = await repository.GetAsync(id, cancellationToken);
        return request is null ? null : Map(request);
    }

    public async Task<PurchaseRequestResult?> SubmitAsync(
        Guid id,
        SubmitPurchaseRequestCommand command,
        CancellationToken cancellationToken)
    {
        var request = await repository.GetAsync(id, cancellationToken);
        if (request is null)
            return null;

        request.Submit(command.Actor, clock.UtcNow, command.Reason);
        await repository.SaveChangesAsync(cancellationToken);
        return Map(request);
    }

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
            request.CreatedAt,
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
