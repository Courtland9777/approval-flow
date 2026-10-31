using System.Text.Json;
using ApprovalFlow.Application;
using ApprovalFlow.Domain;

namespace ApprovalFlow.Api.IntegrationTests;

public sealed class PurchaseRequestTransitionEventTests
{
    [Fact]
    public async Task Submission_then_manager_approval_with_equal_timestamps_emits_current_transition_events()
    {
        await AssertSubmissionAndApprovalEventsAsync(
            new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Submission_then_manager_approval_with_backward_clock_emits_current_transition_events()
    {
        await AssertSubmissionAndApprovalEventsAsync(
            new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 30, 11, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Submission_then_manager_approval_chronologically_emits_current_transition_events()
    {
        await AssertSubmissionAndApprovalEventsAsync(
            new DateTimeOffset(2026, 10, 30, 11, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Rejected_decision_keeps_reviewed_event_contract()
    {
        var repository = new FakePurchaseRequestRepository();
        var clock = new MutableClock(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));
        var service = CreateService(repository, clock);
        var cancellationToken = TestContext.Current.CancellationToken;
        var created = await service.CreateAsync(CreateCommand(), Employee, cancellationToken);
        clock.UtcNow = new DateTimeOffset(2026, 10, 30, 11, 0, 0, TimeSpan.Zero);
        var submitted = await service.SubmitAsync(created.Id, Transition(created.RowVersion, "Ready"), Employee, cancellationToken);
        clock.UtcNow = new DateTimeOffset(2026, 10, 30, 10, 0, 0, TimeSpan.Zero);

        await service.RejectAsync(
            submitted!.Id,
            new RequiredReasonTransitionCommand(submitted.RowVersion, "Not approved"),
            Manager,
            cancellationToken);

        Assert.Equal(2, repository.OutboxMessages.Count);
        Assert.Equal(IntegrationEventNames.PurchaseRequestSubmittedV1, repository.OutboxMessages[0].EventType);
        var message = repository.OutboxMessages.Last();
        Assert.Equal(IntegrationEventNames.PurchaseRequestReviewedV1, message.EventType);
        var payload = JsonSerializer.Deserialize<PurchaseRequestReviewedV1>(message.Payload)!;
        Assert.Equal(created.Id, payload.PurchaseRequestId);
        Assert.Equal("manager@example.test", payload.Actor);
        Assert.Equal(PurchaseRequestStatus.PendingManagerApproval, payload.FromStatus);
        Assert.Equal(PurchaseRequestStatus.Rejected, payload.ToStatus);
        Assert.Equal("Not approved", payload.Reason);
        Assert.Equal(clock.UtcNow, payload.OccurredAt);
    }

    private static async Task AssertSubmissionAndApprovalEventsAsync(DateTimeOffset submittedAt, DateTimeOffset approvedAt)
    {
        var repository = new FakePurchaseRequestRepository();
        var clock = new MutableClock(submittedAt);
        var service = CreateService(repository, clock);
        var cancellationToken = TestContext.Current.CancellationToken;
        var created = await service.CreateAsync(CreateCommand(), Employee, cancellationToken);

        var submitted = await service.SubmitAsync(
            created.Id,
            Transition(created.RowVersion, "Submitted"),
            Employee,
            cancellationToken);
        clock.UtcNow = approvedAt;
        await service.ApproveAsync(
            created.Id,
            Transition(submitted!.RowVersion, "Approved"),
            Manager,
            cancellationToken);

        Assert.Equal(2, repository.OutboxMessages.Count);
        var submittedMessage = repository.OutboxMessages[0];
        Assert.Equal(IntegrationEventNames.PurchaseRequestSubmittedV1, submittedMessage.EventType);
        var submittedPayload = JsonSerializer.Deserialize<PurchaseRequestSubmittedV1>(submittedMessage.Payload)!;
        Assert.Equal(created.Id, submittedPayload.PurchaseRequestId);
        Assert.Equal("employee@example.test", submittedPayload.Requester);
        Assert.Equal(125m, submittedPayload.Total);
        Assert.False(submittedPayload.RequiresFinanceApproval);
        Assert.Equal(submittedAt, submittedPayload.OccurredAt);

        var reviewedMessage = repository.OutboxMessages[1];
        Assert.Equal(IntegrationEventNames.PurchaseRequestReviewedV1, reviewedMessage.EventType);
        var reviewedPayload = JsonSerializer.Deserialize<PurchaseRequestReviewedV1>(reviewedMessage.Payload)!;
        Assert.Equal(created.Id, reviewedPayload.PurchaseRequestId);
        Assert.Equal("manager@example.test", reviewedPayload.Actor);
        Assert.Equal(PurchaseRequestStatus.PendingManagerApproval, reviewedPayload.FromStatus);
        Assert.Equal(PurchaseRequestStatus.Approved, reviewedPayload.ToStatus);
        Assert.Equal("Approved", reviewedPayload.Reason);
        Assert.Equal(approvedAt, reviewedPayload.OccurredAt);
    }

    private static PurchaseRequestService CreateService(FakePurchaseRequestRepository repository, MutableClock clock) =>
        new(repository, clock, new FakeCorrelationContext());

    private static CreatePurchaseRequestCommand CreateCommand() => new(
        "Vendor", "IT", "Office", "Business justification with enough detail.", new DateOnly(2030, 1, 1),
        [new CreateLineItem("Equipment", 1, 125m)]);

    private static TransitionPurchaseRequestCommand Transition(string rowVersion, string reason) => new(rowVersion, reason);

    private static readonly AuthenticatedActor Employee = new("employee@example.test", new HashSet<string> { ApprovalFlowRoles.Employee });
    private static readonly AuthenticatedActor Manager = new("manager@example.test", new HashSet<string> { ApprovalFlowRoles.Manager });

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class FakeCorrelationContext : ICorrelationContext
    {
        public string CorrelationId => "test-correlation";
    }

    private sealed class FakePurchaseRequestRepository : IPurchaseRequestRepository
    {
        private readonly Dictionary<Guid, PurchaseRequest> _requests = [];
        public List<PendingIntegrationEvent> OutboxMessages { get; } = [];

        public Task AddAsync(PurchaseRequest request, CancellationToken cancellationToken)
        {
            _requests.Add(request.Id, request);
            return Task.CompletedTask;
        }

        public Task<PurchaseRequest?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_requests.GetValueOrDefault(id));

        public Task<PurchaseRequestPage> ListAsync(PurchaseRequestListQuery query, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public void AddLineItems(IEnumerable<PurchaseRequestLineItem> lineItems) { }
        public void AddOutboxMessage(PendingIntegrationEvent integrationEvent) => OutboxMessages.Add(integrationEvent);
        public void SetExpectedRowVersion(PurchaseRequest request, byte[] rowVersion) { }

        public Task RefreshRowVersionAsync(PurchaseRequest request, CancellationToken cancellationToken)
        {
            typeof(PurchaseRequest).GetProperty(nameof(PurchaseRequest.RowVersion))!
                .SetValue(request, request.RowVersion.Append((byte)(request.RowVersion.Length + 1)).ToArray());
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
