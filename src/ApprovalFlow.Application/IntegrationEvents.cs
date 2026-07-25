using System.Text.Json;
using ApprovalFlow.Domain;

namespace ApprovalFlow.Application;

public static class IntegrationEventNames
{
    public const string PurchaseRequestSubmittedV1 = "approvalflow.purchase-request.submitted.v1";
    public const string PurchaseRequestReviewedV1 = "approvalflow.purchase-request.reviewed.v1";
    public const string PurchaseRequestReturnedV1 = "approvalflow.purchase-request.returned.v1";
    public const string PurchaseRequestRevisedV1 = "approvalflow.purchase-request.revised.v1";
}

public sealed record PurchaseRequestSubmittedV1(
    Guid PurchaseRequestId,
    string Requester,
    decimal Total,
    bool RequiresFinanceApproval,
    DateTimeOffset OccurredAt);

public sealed record PurchaseRequestReviewedV1(
    Guid PurchaseRequestId,
    string Actor,
    PurchaseRequestStatus FromStatus,
    PurchaseRequestStatus ToStatus,
    string? Reason,
    DateTimeOffset OccurredAt);

public sealed record PurchaseRequestReturnedV1(
    Guid PurchaseRequestId,
    string Actor,
    string Reason,
    DateTimeOffset OccurredAt);

public sealed record PurchaseRequestRevisedV1(
    Guid PurchaseRequestId,
    string Requester,
    DateTimeOffset OccurredAt);

public sealed record PendingIntegrationEvent(
    Guid MessageId,
    string EventType,
    string Payload,
    string CorrelationId,
    DateTimeOffset OccurredAt)
{
    public static PendingIntegrationEvent Create<T>(
        string eventType,
        T payload,
        string correlationId,
        DateTimeOffset occurredAt) =>
        new(Guid.NewGuid(), eventType, JsonSerializer.Serialize(payload), correlationId, occurredAt);
}
