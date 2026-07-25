using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ApprovalFlow.Application;
using ApprovalFlow.Infrastructure;
using Azure.Messaging.ServiceBus;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ApprovalFlow.Api.IntegrationTests;

public sealed class AsyncOperationsTests
{
    private const string ServiceBusConnectionString =
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";
    private const string QueueName = "approvalflow-integration-tests";

    [Fact]
    public async Task Transition_and_outbox_are_atomic_and_preserve_correlation_when_broker_is_unavailable()
    {
        using var factory = new ApprovalFlowApiFactory(
            $"ApprovalFlowIntegrationTests_{Guid.NewGuid():N}",
            "Endpoint=sb://localhost:59999;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=unavailable;");
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);
        var correlationId = $"atomic-{Guid.NewGuid():N}";
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        var cancellationToken = TestContext.Current.CancellationToken;

        var created = await CreateAsync(client, cancellationToken);
        var response = await client.PostAsJsonAsync(
            $"/api/purchase-requests/{created.Id}/submit",
            new { rowVersion = created.RowVersion, reason = "Atomic outbox proof" },
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var db = CreateDbContext(factory.ConnectionString);
        var request = await db.PurchaseRequests.SingleAsync(
            value => value.Id == created.Id,
            cancellationToken);
        var outbox = await db.OutboxMessages.SingleAsync(
            value => value.CorrelationId == correlationId,
            cancellationToken);
        Assert.Equal(ApprovalFlow.Domain.PurchaseRequestStatus.PendingManagerApproval, request.Status);
        Assert.Equal(IntegrationEventNames.PurchaseRequestSubmittedV1, outbox.EventType);
        Assert.Null(outbox.PublishedAt);
        Assert.Null(outbox.FailedAt);

        // Publication is deliberately not part of the API transaction. No worker or broker
        // connection is needed for the committed state and pending outbox row to survive.
        Assert.Equal(correlationId, response.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public void Retry_limit_moves_outbox_message_to_durable_failed_state()
    {
        var message = new OutboxMessage(
            Guid.NewGuid(),
            IntegrationEventNames.PurchaseRequestSubmittedV1,
            "{}",
            "retry-test",
            DateTimeOffset.UtcNow);

        for (var attempt = 1; attempt <= 5; attempt++)
            message.RecordFailure(DateTimeOffset.UtcNow, 5, TimeSpan.Zero, $"failure {attempt}");

        Assert.Equal(5, message.AttemptCount);
        Assert.NotNull(message.FailedAt);
        Assert.Equal("failure 5", message.LastError);
    }

    [Fact]
    public async Task Emulator_dispatches_and_consumes_event_and_duplicate_is_idempotent()
    {
        using var factory = new ApprovalFlowApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);
        var correlationId = $"emulator-{Guid.NewGuid():N}";
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        var cancellationToken = TestContext.Current.CancellationToken;
        var created = await CreateAsync(client, cancellationToken);
        var submitted = await client.PostAsJsonAsync(
            $"/api/purchase-requests/{created.Id}/submit",
            new { rowVersion = created.RowVersion, reason = "Emulator workflow proof" },
            cancellationToken);
        submitted.EnsureSuccessStatusCode();

        using var worker = StartWorker(factory.ConnectionString);
        try
        {
            var projection = await WaitForProjectionAsync(
                factory.ConnectionString,
                correlationId,
                cancellationToken);
            Assert.Equal(created.Id, projection.PurchaseRequestId);
            Assert.Equal(IntegrationEventNames.PurchaseRequestSubmittedV1, projection.EventType);

            await using (var db = CreateDbContext(factory.ConnectionString))
            {
                var outbox = await db.OutboxMessages.SingleAsync(
                    value => value.CorrelationId == correlationId,
                    cancellationToken);
                Assert.NotNull(outbox.PublishedAt);

                await using var bus = new ServiceBusClient(ServiceBusConnectionString);
                await using var sender = bus.CreateSender(QueueName);
                await sender.SendMessageAsync(new ServiceBusMessage(outbox.Payload)
                {
                    MessageId = Guid.NewGuid().ToString("D"),
                    CorrelationId = correlationId,
                    Subject = outbox.EventType,
                    ContentType = "application/json"
                }, cancellationToken);
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            await using var verification = CreateDbContext(factory.ConnectionString);
            Assert.Equal(
                2,
                await verification.ActivityProjections.CountAsync(
                    value => value.CorrelationId == correlationId,
                    cancellationToken));

            var duplicateId = Guid.NewGuid();
            verification.ProcessedMessages.Add(new ProcessedMessage(duplicateId, DateTimeOffset.UtcNow));
            await verification.SaveChangesAsync(cancellationToken);
            await using var duplicateBus = new ServiceBusClient(ServiceBusConnectionString);
            await using var duplicateSender = duplicateBus.CreateSender(QueueName);
            await duplicateSender.SendMessageAsync(new ServiceBusMessage(
                JsonSerializer.Serialize(new PurchaseRequestRevisedV1(
                    created.Id,
                    "employee.demo@local.test",
                    DateTimeOffset.UtcNow)))
            {
                MessageId = duplicateId.ToString("D"),
                CorrelationId = correlationId,
                Subject = IntegrationEventNames.PurchaseRequestRevisedV1,
                ContentType = "application/json"
            }, cancellationToken);

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            Assert.False(await verification.ActivityProjections.AnyAsync(
                value => value.MessageId == duplicateId,
                cancellationToken));
        }
        finally
        {
            await StopWorkerAsync(worker);
        }
    }

    [Fact]
    public async Task Poison_message_is_bounded_recorded_and_dead_lettered_then_cleaned_exactly()
    {
        using var factory = new ApprovalFlowApiFactory();
        using (var client = factory.CreateClient())
            Assert.True(client.BaseAddress is not null);
        var cancellationToken = TestContext.Current.CancellationToken;
        var messageId = Guid.NewGuid().ToString("D");
        var correlationId = $"poison-{Guid.NewGuid():N}";

        await using var bus = new ServiceBusClient(ServiceBusConnectionString);
        await using var sender = bus.CreateSender(QueueName);
        await sender.SendMessageAsync(new ServiceBusMessage("{not-json")
        {
            MessageId = messageId,
            CorrelationId = correlationId,
            Subject = IntegrationEventNames.PurchaseRequestSubmittedV1,
            ContentType = "application/json"
        }, cancellationToken);

        using var worker = StartWorker(factory.ConnectionString);
        try
        {
            await WaitForFailedMessageAsync(
                factory.ConnectionString,
                messageId,
                cancellationToken);
        }
        finally
        {
            await StopWorkerAsync(worker);
        }

        await using var deadLetter = bus.CreateReceiver(
            QueueName,
            new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });
        var found = false;
        var emptyPollsAfterFound = 0;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline && (!found || emptyPollsAfterFound < 2))
        {
            var messages = await deadLetter.ReceiveMessagesAsync(
                10,
                TimeSpan.FromSeconds(1),
                cancellationToken);
            if (messages.Count == 0)
            {
                if (found)
                    emptyPollsAfterFound++;
                continue;
            }
            emptyPollsAfterFound = 0;
            foreach (var message in messages)
            {
                if (message.MessageId == messageId)
                    found = true;
                if (message.CorrelationId?.StartsWith("poison-", StringComparison.Ordinal) == true)
                    await deadLetter.CompleteMessageAsync(message, cancellationToken);
                else
                    await deadLetter.AbandonMessageAsync(message, cancellationToken: cancellationToken);
            }
        }
        Assert.True(found, "The exact poison test message was not found in the dead-letter subqueue.");
    }

    private static Process StartWorker(string connectionString)
    {
        var repositoryRoot = FindRepositoryRoot();
        var process = new Process
        {
            StartInfo = new ProcessStartInfo(
                "dotnet",
                Path.Combine(
                    repositoryRoot,
                    "src/ApprovalFlow.Worker/bin/Debug/net10.0/ApprovalFlow.Worker.dll"))
            {
                WorkingDirectory = repositoryRoot,
                UseShellExecute = false
            }
        };
        process.StartInfo.Environment["ConnectionStrings__ApprovalFlow"] = connectionString;
        process.StartInfo.Environment["ConnectionStrings__ServiceBus"] = ServiceBusConnectionString;
        process.StartInfo.Environment["Messaging__QueueName"] = QueueName;
        process.StartInfo.Environment["Messaging__PollIntervalMilliseconds"] = "100";
        process.StartInfo.Environment["Messaging__InitialBackoffSeconds"] = "0";
        process.StartInfo.Environment["OTEL_SDK_DISABLED"] = "true";
        Assert.True(process.Start());
        return process;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ApprovalFlow.slnx")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate ApprovalFlow.slnx from the test output path.");
    }

    private static async Task StopWorkerAsync(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        process.Dispose();
    }

    private static async Task<ActivityProjection> WaitForProjectionAsync(
        string connectionString,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(40);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var db = CreateDbContext(connectionString);
            var projection = await db.ActivityProjections.AsNoTracking().FirstOrDefaultAsync(
                value => value.CorrelationId == correlationId,
                cancellationToken);
            if (projection is not null)
                return projection;
            await Task.Delay(250, cancellationToken);
        }
        throw new TimeoutException("Timed out waiting for the asynchronous activity projection.");
    }

    private static async Task WaitForFailedMessageAsync(
        string connectionString,
        string messageId,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(40);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var db = CreateDbContext(connectionString);
            if (await db.FailedBrokerMessages.AnyAsync(
                    value => value.BrokerMessageId == messageId,
                    cancellationToken))
                return;
            await Task.Delay(250, cancellationToken);
        }
        throw new TimeoutException("Timed out waiting for the poison-message failure record.");
    }

    private static ApprovalFlowDbContext CreateDbContext(string connectionString) =>
        new(new DbContextOptionsBuilder<ApprovalFlowDbContext>()
            .UseSqlServer(connectionString)
            .Options);

    private static async Task AuthenticateAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                email = DevelopmentSeed.EmployeeUserName,
                password = DevelopmentSeed.DemoPassword
            },
            TestContext.Current.CancellationToken);
        login.EnsureSuccessStatusCode();
        var payload = await login.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", payload.GetProperty("accessToken").GetString());
    }

    private static async Task<PurchaseRequestResult> CreateAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync(
            "/api/purchase-requests",
            new
            {
                vendor = "Async Test Vendor",
                costCenter = "OPS-400",
                category = "Office",
                businessJustification = "Focused asynchronous operations integration verification.",
                requestedDeliveryDate = "2030-04-01",
                lineItems = new[] { new { description = "Test item", quantity = 1, unitPrice = 50m } }
            },
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PurchaseRequestResult>(cancellationToken))!;
    }
}
