namespace ApprovalFlow.Domain;

public enum PurchaseRequestStatus
{
    Draft,
    Submitted,
    PendingManagerApproval,
    PendingFinanceApproval,
    Approved,
    Rejected,
    ReturnedForChanges,
    Cancelled,
    Completed
}
