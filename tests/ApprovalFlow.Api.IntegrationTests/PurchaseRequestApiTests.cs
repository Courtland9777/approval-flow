using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApprovalFlow.Application;
using ApprovalFlow.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ApprovalFlow.Api.IntegrationTests;

public sealed class PurchaseRequestApiTests : IClassFixture<ApprovalFlowApiFactory>
{
    private readonly HttpClient _client;

    public PurchaseRequestApiTests(ApprovalFlowApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Create_get_and_submit_persists_the_vertical_slice()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var createResponse = await _client.PostAsJsonAsync("/api/purchase-requests", new
        {
            vendor = "Adventure Works",
            costCenter = "IT-200",
            category = "Hardware",
            businessJustification = "Replacement equipment for local integration testing.",
            requestedDeliveryDate = "2030-02-01",
            requester = "integration.employee",
            lineItems = new[]
            {
                new { description = "Monitor", quantity = 2, unitPrice = 249.99m }
            }
        }, cancellationToken);

        Assert.True(
            createResponse.StatusCode == HttpStatusCode.Created,
            await createResponse.Content.ReadAsStringAsync(cancellationToken));
        var created = await createResponse.Content.ReadFromJsonAsync<PurchaseRequestResult>(cancellationToken);
        Assert.NotNull(created);
        Assert.Equal(PurchaseRequestStatus.Draft, created.Status);
        Assert.Equal(499.98m, created.Total);

        var getResponse = await _client.GetAsync($"/api/purchase-requests/{created.Id}", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var submitResponse = await _client.PostAsJsonAsync(
            $"/api/purchase-requests/{created.Id}/submit",
            new { actor = "integration.employee", reason = "Ready for manager review" },
            cancellationToken);

        Assert.True(
            submitResponse.StatusCode == HttpStatusCode.OK,
            await submitResponse.Content.ReadAsStringAsync(cancellationToken));
        var submitted = await submitResponse.Content.ReadFromJsonAsync<PurchaseRequestResult>(cancellationToken);
        Assert.NotNull(submitted);
        Assert.Equal(PurchaseRequestStatus.Submitted, submitted.Status);
        var audit = Assert.Single(submitted.AuditEntries);
        Assert.Equal(PurchaseRequestStatus.Draft, audit.FromStatus);
        Assert.Equal(PurchaseRequestStatus.Submitted, audit.ToStatus);

        var persisted = await _client.GetFromJsonAsync<PurchaseRequestResult>(
            $"/api/purchase-requests/{created.Id}",
            cancellationToken);
        Assert.NotNull(persisted);
        Assert.Equal(PurchaseRequestStatus.Submitted, persisted.Status);
        Assert.Single(persisted.AuditEntries);
    }

    [Fact]
    public async Task Repeated_submit_returns_conflict_problem_details()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var createResponse = await _client.PostAsJsonAsync("/api/purchase-requests", new
        {
            vendor = "Tailspin",
            costCenter = "OPS-100",
            category = "Office",
            businessJustification = "Validate transition conflict behavior.",
            requestedDeliveryDate = "2030-03-01",
            requester = "transition.employee",
            lineItems = new[] { new { description = "Chair", quantity = 1, unitPrice = 300m } }
        }, cancellationToken);
        var created = await createResponse.Content.ReadFromJsonAsync<PurchaseRequestResult>(cancellationToken);
        Assert.NotNull(created);

        await _client.PostAsJsonAsync(
            $"/api/purchase-requests/{created.Id}/submit",
            new { actor = "transition.employee", reason = "First submit" },
            cancellationToken);
        var response = await _client.PostAsJsonAsync(
            $"/api/purchase-requests/{created.Id}/submit",
            new { actor = "transition.employee", reason = "Second submit" },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal("Domain transition rejected", problem.GetProperty("title").GetString());
    }
}

public sealed class ApprovalFlowApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting(
            "ConnectionStrings:ApprovalFlow",
            Environment.GetEnvironmentVariable("APPROVALFLOW_TEST_CONNECTION")
            ?? "Server=localhost,14333;Database=ApprovalFlowIntegrationTests;User Id=sa;Password=LocalOnly_ApprovalFlow_2026!;TrustServerCertificate=True;Encrypt=True");
        builder.UseSetting("SeedDevelopmentData", "false");
    }
}
