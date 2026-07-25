using System.Diagnostics;
using System.Text.Json;
using ApprovalFlow.Application;
using ApprovalFlow.Infrastructure;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApprovalFlow.Worker;

public sealed class IntegrationEventConsumer(
    IDbContextFactory<ApprovalFlowDbContext> dbContextFactory,
    ServiceBusClient serviceBusClient,
    IOptions<MessagingOptions> options,
    MessagingTelemetry telemetry,
    ILogger<IntegrationEventConsumer> logger) : BackgroundService
{
    private ServiceBusProcessor? _processor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        _processor = serviceBusClient.CreateProcessor(
            settings.QueueName,
            new ServiceBusProcessorOptions
            {
                AutoCompleteMessages = false,
                MaxConcurrentCalls = 1,
                MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(2)
            });
        _processor.ProcessMessageAsync += ProcessMessageAsync;
        _processor.ProcessErrorAsync += args =>
        {
            logger.LogError(
                args.Exception,
                "Service Bus receiver error from {ErrorSource} for {EntityPath}",
                args.ErrorSource,
                args.EntityPath);
            return Task.CompletedTask;
        };
        await _processor.StartProcessingAsync(stoppingToken);
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        await _processor.StopProcessingAsync(CancellationToken.None);
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var message = args.Message;
        var parent = TryParseParent(message);
        using var activity = telemetry.Activities.StartActivity(
            "integration-event.consume",
            ActivityKind.Consumer,
            parent,
            tags:
            [
                new("messaging.message.id", message.MessageId),
                new("messaging.operation", "process"),
                new("approvalflow.correlation_id", message.CorrelationId),
                new("approvalflow.event_type", message.Subject)
            ]);
        try
        {
            if (!Guid.TryParse(message.MessageId, out var messageId))
                throw new InvalidDataException("MessageId must be a GUID.");

            await using var db = await dbContextFactory.CreateDbContextAsync(args.CancellationToken);
            if (await db.ProcessedMessages.AnyAsync(
                    processed => processed.MessageId == messageId,
                    args.CancellationToken))
            {
                telemetry.Duplicates.Add(1);
                logger.LogInformation("Ignored duplicate message {MessageId}", message.MessageId);
                await args.CompleteMessageAsync(message, args.CancellationToken);
                return;
            }

            var projection = CreateProjection(messageId, message);
            db.ProcessedMessages.Add(new ProcessedMessage(messageId, DateTimeOffset.UtcNow));
            db.ActivityProjections.Add(projection);
            await db.SaveChangesAsync(args.CancellationToken);
            await args.CompleteMessageAsync(message, args.CancellationToken);
            telemetry.Consumed.Add(
                1,
                new KeyValuePair<string, object?>("event.type", message.Subject));
            logger.LogInformation(
                "Projected message {MessageId} with correlation {CorrelationId}",
                message.MessageId,
                message.CorrelationId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            await HandleFailureAsync(args, exception);
        }
    }

    private async Task HandleFailureAsync(ProcessMessageEventArgs args, Exception exception)
    {
        var maximumAttempts = options.Value.MaximumAttempts;
        if (args.Message.DeliveryCount >= maximumAttempts)
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(args.CancellationToken);
            db.FailedBrokerMessages.Add(new FailedBrokerMessage(
                Guid.NewGuid(),
                args.Message.MessageId,
                args.Message.CorrelationId ?? string.Empty,
                exception.Message,
                DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(args.CancellationToken);
            await args.DeadLetterMessageAsync(
                args.Message,
                "MaximumProcessingAttemptsExceeded",
                exception.Message,
                args.CancellationToken);
            telemetry.DeadLettered.Add(1);
            logger.LogError(
                exception,
                "Dead-lettered poison message {MessageId} after {DeliveryCount} deliveries",
                args.Message.MessageId,
                args.Message.DeliveryCount);
        }
        else
        {
            logger.LogWarning(
                exception,
                "Message {MessageId} processing attempt {DeliveryCount}/{MaximumAttempts} failed",
                args.Message.MessageId,
                args.Message.DeliveryCount,
                maximumAttempts);
            await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
        }
    }

    private static ActivityContext TryParseParent(ServiceBusReceivedMessage message)
    {
        if (message.ApplicationProperties.TryGetValue("traceparent", out var value)
            && value is string traceParent
            && ActivityContext.TryParse(traceParent, null, out var parent))
            return parent;
        return default;
    }

    private static ActivityProjection CreateProjection(Guid messageId, ServiceBusReceivedMessage message)
    {
        var payload = message.Body.ToString();
        var (purchaseRequestId, summary) = message.Subject switch
        {
            IntegrationEventNames.PurchaseRequestSubmittedV1 => Submitted(payload),
            IntegrationEventNames.PurchaseRequestReviewedV1 => Reviewed(payload),
            IntegrationEventNames.PurchaseRequestReturnedV1 => Returned(payload),
            IntegrationEventNames.PurchaseRequestRevisedV1 => Revised(payload),
            _ => throw new InvalidDataException($"Unsupported event type '{message.Subject}'.")
        };
        return new ActivityProjection(
            Guid.NewGuid(),
            messageId,
            purchaseRequestId,
            message.Subject,
            message.CorrelationId ?? string.Empty,
            summary,
            DateTimeOffset.UtcNow);
    }

    private static (Guid, string) Submitted(string payload)
    {
        var value = JsonSerializer.Deserialize<PurchaseRequestSubmittedV1>(payload)
            ?? throw new InvalidDataException("Submitted event payload is empty.");
        return (value.PurchaseRequestId, $"Request submitted by {value.Requester} for {value.Total:C}.");
    }

    private static (Guid, string) Reviewed(string payload)
    {
        var value = JsonSerializer.Deserialize<PurchaseRequestReviewedV1>(payload)
            ?? throw new InvalidDataException("Reviewed event payload is empty.");
        return (value.PurchaseRequestId, $"Request moved from {value.FromStatus} to {value.ToStatus} by {value.Actor}.");
    }

    private static (Guid, string) Returned(string payload)
    {
        var value = JsonSerializer.Deserialize<PurchaseRequestReturnedV1>(payload)
            ?? throw new InvalidDataException("Returned event payload is empty.");
        return (value.PurchaseRequestId, $"Request returned for changes by {value.Actor}.");
    }

    private static (Guid, string) Revised(string payload)
    {
        var value = JsonSerializer.Deserialize<PurchaseRequestRevisedV1>(payload)
            ?? throw new InvalidDataException("Revised event payload is empty.");
        return (value.PurchaseRequestId, $"Request revised by {value.Requester}.");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_processor is not null)
            await _processor.DisposeAsync();
    }
}
