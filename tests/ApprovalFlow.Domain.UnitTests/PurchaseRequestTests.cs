using ApprovalFlow.Domain;

namespace ApprovalFlow.Domain.UnitTests;

public sealed class PurchaseRequestTests
{
    [Fact]
    public void Submit_moves_draft_to_submitted_and_records_audit()
    {
        var request = CreateDraft();
        var occurredAt = new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

        request.Submit("employee.demo", occurredAt, "Ready");

        Assert.Equal(PurchaseRequestStatus.Submitted, request.Status);
        var audit = Assert.Single(request.AuditEntries);
        Assert.Equal(PurchaseRequestStatus.Draft, audit.FromStatus);
        Assert.Equal(PurchaseRequestStatus.Submitted, audit.ToStatus);
        Assert.Equal("employee.demo", audit.Actor);
        Assert.Equal("Ready", audit.Reason);
    }

    [Fact]
    public void Submit_rejects_repeated_transition()
    {
        var request = CreateDraft();
        request.Submit("employee.demo", DateTimeOffset.UtcNow, null);

        var exception = Assert.Throws<DomainValidationException>(
            () => request.Submit("employee.demo", DateTimeOffset.UtcNow, null));

        Assert.Contains("cannot be submitted", exception.Message);
        Assert.Single(request.AuditEntries);
    }

    [Fact]
    public void Submit_rejects_an_actor_other_than_requester()
    {
        var request = CreateDraft();

        var exception = Assert.Throws<DomainValidationException>(
            () => request.Submit("another.employee", DateTimeOffset.UtcNow, null));

        Assert.Equal("Only the requester can submit this request.", exception.Message);
        Assert.Equal(PurchaseRequestStatus.Draft, request.Status);
        Assert.Empty(request.AuditEntries);
    }

    [Fact]
    public void Create_rejects_an_empty_line_item_collection()
    {
        Assert.Throws<DomainValidationException>(() => PurchaseRequest.Create(
            "Vendor", "CC-1", "Office", "Business need", new DateOnly(2030, 1, 1),
            "employee.demo", [], DateTimeOffset.UtcNow));
    }

    private static PurchaseRequest CreateDraft() =>
        PurchaseRequest.Create(
            "Vendor",
            "CC-1",
            "Office",
            "Business need",
            new DateOnly(2030, 1, 1),
            "employee.demo",
            [new PurchaseRequestLineItem("Keyboard", 2, 50m)],
            DateTimeOffset.UtcNow);
}
