namespace ApprovalFlow.Infrastructure;

public sealed class OutboxMessage
{
    private OutboxMessage() { }

    public OutboxMessage(
        Guid id,
        string eventType,
        string payload,
        string correlationId,
        DateTimeOffset occurredAt)
    {
        Id = id;
        EventType = eventType;
        Payload = payload;
        CorrelationId = correlationId;
        OccurredAt = occurredAt;
        NextAttemptAt = occurredAt;
    }

    public Guid Id { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public string CorrelationId { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public DateTimeOffset? FailedAt { get; private set; }
    public int AttemptCount { get; private set; }
    public string? LastError { get; private set; }

    public void MarkPublished(DateTimeOffset publishedAt)
    {
        PublishedAt = publishedAt;
        LastError = null;
    }

    public void RecordFailure(DateTimeOffset now, int maximumAttempts, TimeSpan backoff, string error)
    {
        AttemptCount++;
        LastError = error.Length <= 2000 ? error : error[..2000];
        if (AttemptCount >= maximumAttempts)
            FailedAt = now;
        else
            NextAttemptAt = now.Add(backoff);
    }
}

public sealed class ProcessedMessage
{
    private ProcessedMessage() { }

    public ProcessedMessage(Guid messageId, DateTimeOffset processedAt)
    {
        MessageId = messageId;
        ProcessedAt = processedAt;
    }

    public Guid MessageId { get; private set; }
    public DateTimeOffset ProcessedAt { get; private set; }
}

public sealed class ActivityProjection
{
    private ActivityProjection() { }

    public ActivityProjection(
        Guid id,
        Guid messageId,
        Guid purchaseRequestId,
        string eventType,
        string correlationId,
        string summary,
        DateTimeOffset recordedAt)
    {
        Id = id;
        MessageId = messageId;
        PurchaseRequestId = purchaseRequestId;
        EventType = eventType;
        CorrelationId = correlationId;
        Summary = summary;
        RecordedAt = recordedAt;
    }

    public Guid Id { get; private set; }
    public Guid MessageId { get; private set; }
    public Guid PurchaseRequestId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string CorrelationId { get; private set; } = string.Empty;
    public string Summary { get; private set; } = string.Empty;
    public DateTimeOffset RecordedAt { get; private set; }
}

public sealed class FailedBrokerMessage
{
    private FailedBrokerMessage() { }

    public FailedBrokerMessage(
        Guid id,
        string brokerMessageId,
        string correlationId,
        string reason,
        DateTimeOffset failedAt)
    {
        Id = id;
        BrokerMessageId = brokerMessageId;
        CorrelationId = correlationId;
        Reason = reason;
        FailedAt = failedAt;
    }

    public Guid Id { get; private set; }
    public string BrokerMessageId { get; private set; } = string.Empty;
    public string CorrelationId { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset FailedAt { get; private set; }
}
