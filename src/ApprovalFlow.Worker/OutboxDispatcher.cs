using System.Diagnostics;
using ApprovalFlow.Infrastructure;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApprovalFlow.Worker;

public sealed class OutboxDispatcher(
    IDbContextFactory<ApprovalFlowDbContext> dbContextFactory,
    ServiceBusClient serviceBusClient,
    IOptions<MessagingOptions> options,
    MessagingTelemetry telemetry,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        await using var sender = serviceBusClient.CreateSender(settings.QueueName);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await DispatchBatchAsync(sender, settings, stoppingToken);
                if (!processed)
                    await Task.Delay(settings.PollIntervalMilliseconds, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                telemetry.DispatchFailures.Add(1);
                logger.LogError(
                    exception,
                    "Outbox dispatcher dependency failure; pending records remain durable and polling will resume");
                await Task.Delay(settings.PollIntervalMilliseconds, stoppingToken);
            }
        }
    }

    internal async Task<bool> DispatchBatchAsync(
        ServiceBusSender sender,
        MessagingOptions settings,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var messages = await db.OutboxMessages
            .Where(message => message.PublishedAt == null
                && message.FailedAt == null
                && message.NextAttemptAt <= now)
            .OrderBy(message => message.OccurredAt)
            .Take(settings.DispatchBatchSize)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            using var activity = telemetry.Activities.StartActivity(
                "outbox.dispatch",
                ActivityKind.Producer,
                parentContext: default,
                tags:
                [
                    new("messaging.message.id", message.Id),
                    new("messaging.operation", "publish"),
                    new("approvalflow.correlation_id", message.CorrelationId),
                    new("approvalflow.event_type", message.EventType)
                ]);
            try
            {
                var brokerMessage = new ServiceBusMessage(message.Payload)
                {
                    MessageId = message.Id.ToString("D"),
                    CorrelationId = message.CorrelationId,
                    Subject = message.EventType,
                    ContentType = "application/json"
                };
                if (activity is not null)
                    brokerMessage.ApplicationProperties["traceparent"] = activity.Id!;
                await sender.SendMessageAsync(brokerMessage, cancellationToken);
                message.MarkPublished(DateTimeOffset.UtcNow);
                telemetry.Published.Add(
                    1,
                    new KeyValuePair<string, object?>("event.type", message.EventType));
                logger.LogInformation(
                    "Published outbox message {MessageId} of type {EventType} with correlation {CorrelationId}",
                    message.Id,
                    message.EventType,
                    message.CorrelationId);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var backoff = TimeSpan.FromSeconds(
                    settings.InitialBackoffSeconds * Math.Pow(2, message.AttemptCount));
                message.RecordFailure(
                    DateTimeOffset.UtcNow,
                    settings.MaximumAttempts,
                    backoff,
                    exception.Message);
                telemetry.DispatchFailures.Add(
                    1,
                    new KeyValuePair<string, object?>("event.type", message.EventType));
                logger.LogWarning(
                    exception,
                    "Outbox dispatch attempt {Attempt}/{MaximumAttempts} failed for {MessageId}; failed permanently: {FailedPermanently}",
                    message.AttemptCount,
                    settings.MaximumAttempts,
                    message.Id,
                    message.FailedAt is not null);
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        return messages.Count > 0;
    }
}
