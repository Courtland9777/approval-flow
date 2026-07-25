using ApprovalFlow.Domain;

namespace ApprovalFlow.Application;

public interface IPurchaseRequestRepository
{
    Task AddAsync(PurchaseRequest request, CancellationToken cancellationToken);
    Task<PurchaseRequest?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<PurchaseRequestPage> ListAsync(
        PurchaseRequestListQuery query,
        CancellationToken cancellationToken);
    void AddLineItems(IEnumerable<PurchaseRequestLineItem> lineItems);
    void SetExpectedRowVersion(PurchaseRequest request, byte[] rowVersion);
    Task RefreshRowVersionAsync(PurchaseRequest request, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
