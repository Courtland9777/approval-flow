namespace ApprovalFlow.Worker;

public sealed class MessagingOptions
{
    public string QueueName { get; init; } = "approvalflow-workflow-events";
    public int DispatchBatchSize { get; init; } = 20;
    public int MaximumAttempts { get; init; } = 5;
    public int PollIntervalMilliseconds { get; init; } = 500;
    public int InitialBackoffSeconds { get; init; } = 2;
}
