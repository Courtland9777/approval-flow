using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ApprovalFlow.Worker;

public sealed class MessagingTelemetry : IDisposable
{
    public const string ActivitySourceName = "ApprovalFlow.Messaging";
    public const string MeterName = "ApprovalFlow.Messaging";
    public ActivitySource Activities { get; } = new(ActivitySourceName);
    private readonly Meter _meter = new(MeterName);

    public Counter<long> Published { get; }
    public Counter<long> DispatchFailures { get; }
    public Counter<long> Consumed { get; }
    public Counter<long> Duplicates { get; }
    public Counter<long> DeadLettered { get; }

    public MessagingTelemetry()
    {
        Published = _meter.CreateCounter<long>("approvalflow.outbox.published");
        DispatchFailures = _meter.CreateCounter<long>("approvalflow.outbox.dispatch_failures");
        Consumed = _meter.CreateCounter<long>("approvalflow.messages.consumed");
        Duplicates = _meter.CreateCounter<long>("approvalflow.messages.duplicates");
        DeadLettered = _meter.CreateCounter<long>("approvalflow.messages.dead_lettered");
    }

    public void Dispose()
    {
        Activities.Dispose();
        _meter.Dispose();
    }
}
