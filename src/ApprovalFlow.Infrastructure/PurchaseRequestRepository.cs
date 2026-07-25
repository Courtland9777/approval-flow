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

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
