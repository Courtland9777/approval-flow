using ApprovalFlow.Domain;

namespace ApprovalFlow.Application;

public interface IPurchaseRequestRepository
{
    Task AddAsync(PurchaseRequest request, CancellationToken cancellationToken);
    Task<PurchaseRequest?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
