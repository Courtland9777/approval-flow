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

        Assert.Equal(PurchaseRequestStatus.PendingManagerApproval, request.Status);
        var audit = Assert.Single(request.AuditEntries);
        Assert.Equal(PurchaseRequestStatus.Draft, audit.FromStatus);
        Assert.Equal(PurchaseRequestStatus.PendingManagerApproval, audit.ToStatus);
        Assert.Equal("employee.demo", audit.Actor);
        Assert.Equal("Ready", audit.Reason);
    }

    [Fact]
    public void Submit_rejects_repeated_transition()
    {
        var request = CreateDraft();
        request.Submit("employee.demo", DateTimeOffset.UtcNow, null);

        var exception = Assert.Throws<DomainConflictException>(
            () => request.Submit("employee.demo", DateTimeOffset.UtcNow, null));

        Assert.Contains("cannot be submitted", exception.Message);
        Assert.Single(request.AuditEntries);
    }

    [Fact]
    public void Submit_rejects_an_actor_other_than_requester()
    {
        var request = CreateDraft();

        var exception = Assert.Throws<DomainAuthorizationException>(
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

    [Theory]
    [InlineData("Office", 999, PurchaseRequestStatus.Approved)]
    [InlineData("Office", 1000, PurchaseRequestStatus.PendingFinanceApproval)]
    [InlineData("Software", 10, PurchaseRequestStatus.PendingFinanceApproval)]
    [InlineData("Security", 10, PurchaseRequestStatus.PendingFinanceApproval)]
    public void Manager_approval_applies_deterministic_finance_policy(
        string category,
        decimal total,
        PurchaseRequestStatus expected)
    {
        var request = CreateDraft(category, total);
        request.Submit("employee.demo", DateTimeOffset.UtcNow, null);

        request.ApproveAsManager("manager.demo", DateTimeOffset.UtcNow, "Reviewed");

        Assert.Equal(expected, request.Status);
    }

    [Fact]
    public void Requester_cannot_approve_own_request()
    {
        var request = CreateDraft();
        request.Submit("employee.demo", DateTimeOffset.UtcNow, null);

        Assert.Throws<DomainAuthorizationException>(
            () => request.ApproveAsManager("employee.demo", DateTimeOffset.UtcNow, null));
    }

    [Fact]
    public void Returned_request_must_be_revised_before_resubmission()
    {
        var request = CreateDraft();
        request.Submit("employee.demo", DateTimeOffset.UtcNow, null);
        request.ReturnForChanges("manager.demo", DateTimeOffset.UtcNow, "Clarify need");

        Assert.Throws<DomainConflictException>(
            () => request.Submit("employee.demo", DateTimeOffset.UtcNow, null));

        request.Revise(
            "employee.demo", "Vendor 2", "CC-2", "Office", "Updated business need",
            new DateOnly(2031, 1, 1), [new PurchaseRequestLineItem("Mouse", 1, 30m)],
            DateTimeOffset.UtcNow, "Updated");
        request.Submit("employee.demo", DateTimeOffset.UtcNow, "Resubmitted");

        Assert.Equal(PurchaseRequestStatus.PendingManagerApproval, request.Status);
        Assert.Equal(4, request.AuditEntries.Count);
    }

    private static PurchaseRequest CreateDraft(string category = "Office", decimal total = 100m) =>
        PurchaseRequest.Create(
            "Vendor",
            "CC-1",
            category,
            "Business need",
            new DateOnly(2030, 1, 1),
            "employee.demo",
            [new PurchaseRequestLineItem("Keyboard", 1, total)],
            DateTimeOffset.UtcNow);
}
