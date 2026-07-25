using ApprovalFlow.Application;
using ApprovalFlow.Domain;
using Microsoft.EntityFrameworkCore;

namespace ApprovalFlow.Infrastructure;

public sealed class PurchaseRequestRepository(ApprovalFlowDbContext dbContext) : IPurchaseRequestRepository
{
    public Task AddAsync(PurchaseRequest request, CancellationToken cancellationToken) =>
        dbContext.PurchaseRequests.AddAsync(request, cancellationToken).AsTask();

    public Task<PurchaseRequest?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.PurchaseRequests
            .Include(x => x.LineItems)
            .Include(x => x.AuditEntries)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<PurchaseRequestPage> ListAsync(
        PurchaseRequestListQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<PurchaseRequest> requests = dbContext.PurchaseRequests
            .AsNoTracking()
            .Include(x => x.LineItems);

        requests = query.Scope switch
        {
            PurchaseRequestListScope.Owned => requests.Where(x => x.Requester == query.Requester),
            PurchaseRequestListScope.ManagerQueue =>
                requests.Where(x => x.Status == PurchaseRequestStatus.PendingManagerApproval),
            PurchaseRequestListScope.FinanceQueue =>
                requests.Where(x => x.Status == PurchaseRequestStatus.PendingFinanceApproval),
            _ => throw new ArgumentOutOfRangeException(nameof(query))
        };

        if (query.Status is { } status)
            requests = requests.Where(x => x.Status == status);

        var totalCount = await requests.CountAsync(cancellationToken);
        requests = query.Sort switch
        {
            PurchaseRequestSort.LastModifiedAsc =>
                requests.OrderBy(x => x.LastModifiedAt).ThenBy(x => x.Id),
            PurchaseRequestSort.TotalDesc =>
                requests.OrderByDescending(x => x.LineItems.Sum(item => item.Quantity * item.UnitPrice))
                    .ThenBy(x => x.Id),
            PurchaseRequestSort.TotalAsc =>
                requests.OrderBy(x => x.LineItems.Sum(item => item.Quantity * item.UnitPrice))
                    .ThenBy(x => x.Id),
            _ => requests.OrderByDescending(x => x.LastModifiedAt).ThenBy(x => x.Id)
        };

        var entities = await requests
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PurchaseRequestPage(
            entities.Select(MapSummary).ToArray(),
            query.Page,
            query.PageSize,
            totalCount);
    }

    public void AddLineItems(IEnumerable<PurchaseRequestLineItem> lineItems) =>
        dbContext.Set<PurchaseRequestLineItem>().AddRange(lineItems);

    public void AddOutboxMessage(PendingIntegrationEvent integrationEvent) =>
        dbContext.OutboxMessages.Add(new OutboxMessage(
            integrationEvent.MessageId,
            integrationEvent.EventType,
            integrationEvent.Payload,
            integrationEvent.CorrelationId,
            integrationEvent.OccurredAt));

    public void SetExpectedRowVersion(PurchaseRequest request, byte[] rowVersion) =>
        dbContext.Entry(request).Property(x => x.RowVersion).OriginalValue = rowVersion;

    public Task RefreshRowVersionAsync(PurchaseRequest request, CancellationToken cancellationToken) =>
        dbContext.Entry(request).ReloadAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

    private static PurchaseRequestSummaryResult MapSummary(PurchaseRequest request) =>
        new(
            request.Id,
            request.Vendor,
            request.Category,
            request.Requester,
            request.Status,
            request.Total,
            request.RequiresFinanceApproval,
            request.RequestedDeliveryDate,
            request.LastModifiedAt);
}
