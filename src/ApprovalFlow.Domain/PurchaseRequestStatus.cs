using System.Text.Json.Serialization;

namespace ApprovalFlow.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
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
