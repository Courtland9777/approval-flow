using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ApprovalFlow.Application;
using ApprovalFlow.Domain;
using ApprovalFlow.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;

namespace ApprovalFlow.Api.IntegrationTests;

public sealed class PurchaseRequestApiTests : IClassFixture<ApprovalFlowApiFactory>
{
    private readonly ApprovalFlowApiFactory _factory;

    public PurchaseRequestApiTests(ApprovalFlowApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Endpoints_require_authentication()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/api/purchase-requests/{Guid.NewGuid()}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Requester_comes_from_principal_and_ownership_is_enforced()
    {
        using var employee = await CreateAuthenticatedClientAsync(DevelopmentSeed.EmployeeUserName);
        var created = await CreateAsync(employee, "Office", 100m);

        Assert.Equal(DevelopmentSeed.EmployeeUserName, created.Requester);

        using var otherEmployee = await CreateAuthenticatedClientAsync(DevelopmentSeed.SecondEmployeeUserName);
        var response = await otherEmployee.GetAsync(
            $"/api/purchase-requests/{created.Id}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Lists_are_role_scoped_bounded_and_do_not_expose_other_employee_requests()
    {
        using var employee = await CreateAuthenticatedClientAsync(DevelopmentSeed.EmployeeUserName);
        using var otherEmployee = await CreateAuthenticatedClientAsync(DevelopmentSeed.SecondEmployeeUserName);
        using var manager = await CreateAuthenticatedClientAsync(DevelopmentSeed.ManagerUserName);
        using var finance = await CreateAuthenticatedClientAsync(DevelopmentSeed.FinanceUserName);

        var owned = await CreateAsync(employee, "Software", 1500m);
        var otherOwned = await CreateAsync(otherEmployee, "Software", 1600m);
        await SubmitAsync(employee, owned);
        await SubmitAsync(otherEmployee, otherOwned);

        var mine = await employee.GetFromJsonAsync<PurchaseRequestPage>(
            "/api/purchase-requests/mine?page=1&pageSize=1&sort=TotalDesc",
            TestContext.Current.CancellationToken);
        Assert.NotNull(mine);
        Assert.All(mine.Items, item =>
            Assert.Equal(DevelopmentSeed.EmployeeUserName, item.Requester));
        Assert.Equal(1, mine.PageSize);

        var managerQueue = await manager.GetFromJsonAsync<PurchaseRequestPage>(
            "/api/purchase-requests/manager-queue?page=1&pageSize=20",
            TestContext.Current.CancellationToken);
        Assert.NotNull(managerQueue);
        Assert.Contains(managerQueue.Items, item => item.Id == owned.Id);
        Assert.Contains(managerQueue.Items, item => item.Id == otherOwned.Id);
        Assert.All(managerQueue.Items, item =>
            Assert.Equal(PurchaseRequestStatus.PendingManagerApproval, item.Status));

        var forbiddenManagerQueue = await employee.GetAsync(
            "/api/purchase-requests/manager-queue?page=1&pageSize=20",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenManagerQueue.StatusCode);

        var emptyFinanceQueue = await finance.GetFromJsonAsync<PurchaseRequestPage>(
            "/api/purchase-requests/finance-queue?page=1&pageSize=20&status=Draft",
            TestContext.Current.CancellationToken);
        Assert.NotNull(emptyFinanceQueue);
        Assert.Empty(emptyFinanceQueue.Items);
    }

    [Fact]
    public async Task List_rejects_unbounded_page_sizes()
    {
        using var employee = await CreateAuthenticatedClientAsync(DevelopmentSeed.EmployeeUserName);
        var response = await employee.GetAsync(
            "/api/purchase-requests/mine?page=1&pageSize=51",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Employee_cannot_approve_and_finance_cannot_take_manager_step()
    {
        using var employee = await CreateAuthenticatedClientAsync(DevelopmentSeed.EmployeeUserName);
        var created = await CreateAsync(employee, "Security", 100m);
        var submitted = await SubmitAsync(employee, created);

        var employeeResponse = await employee.PostAsJsonAsync(
            $"/api/purchase-requests/{created.Id}/approve",
            new { rowVersion = submitted.RowVersion, reason = "Self approve" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, employeeResponse.StatusCode);

        using var finance = await CreateAuthenticatedClientAsync(DevelopmentSeed.FinanceUserName);
        var financeResponse = await finance.PostAsJsonAsync(
            $"/api/purchase-requests/{created.Id}/approve",
            new { rowVersion = submitted.RowVersion, reason = "Wrong step" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, financeResponse.StatusCode);
    }

    [Fact]
    public async Task No_user_can_approve_their_own_request()
    {
        using var manager = await CreateAuthenticatedClientAsync(DevelopmentSeed.ManagerUserName);
        var created = await CreateAsync(manager, "Office", 100m);
        var submitted = await SubmitAsync(manager, created);

        var response = await manager.PostAsJsonAsync(
            $"/api/purchase-requests/{created.Id}/approve",
            new { rowVersion = submitted.RowVersion, reason = "Own request" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        Assert.Equal("Authorization denied", problem.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("Office", 999, PurchaseRequestStatus.Approved)]
    [InlineData("Office", 1000, PurchaseRequestStatus.PendingFinanceApproval)]
    [InlineData("Software", 10, PurchaseRequestStatus.PendingFinanceApproval)]
    [InlineData("Security", 10, PurchaseRequestStatus.PendingFinanceApproval)]
    public async Task Manager_approval_routes_by_finance_policy(
        string category,
        decimal total,
        PurchaseRequestStatus expected)
    {
        using var employee = await CreateAuthenticatedClientAsync(DevelopmentSeed.EmployeeUserName);
        using var manager = await CreateAuthenticatedClientAsync(DevelopmentSeed.ManagerUserName);
        var created = await CreateAsync(employee, category, total);
        var submitted = await SubmitAsync(employee, created);

        var managerApproved = await TransitionAsync(
            manager, created.Id, "approve", submitted.RowVersion, "Manager approved");
        Assert.Equal(expected, managerApproved.Status);

        if (expected == PurchaseRequestStatus.PendingFinanceApproval)
        {
            using var finance = await CreateAuthenticatedClientAsync(DevelopmentSeed.FinanceUserName);
            var approved = await TransitionAsync(
                finance, created.Id, "approve", managerApproved.RowVersion, "Finance approved");
            Assert.Equal(PurchaseRequestStatus.Approved, approved.Status);
            Assert.Equal(3, approved.AuditEntries.Count);
            Assert.All(approved.AuditEntries, audit => Assert.False(string.IsNullOrWhiteSpace(audit.Actor)));
        }
    }

    [Fact]
    public async Task Manager_can_reject_with_a_reason()
    {
        using var employee = await CreateAuthenticatedClientAsync(DevelopmentSeed.EmployeeUserName);
        using var manager = await CreateAuthenticatedClientAsync(DevelopmentSeed.ManagerUserName);
        var created = await CreateAsync(employee, "Office", 100m);
        var submitted = await SubmitAsync(employee, created);

        var rejected = await TransitionAsync(
            manager, created.Id, "reject", submitted.RowVersion, "Insufficient justification");

        Assert.Equal(PurchaseRequestStatus.Rejected, rejected.Status);
        Assert.Equal("Insufficient justification", rejected.AuditEntries.Last().Reason);
        Assert.Equal(DevelopmentSeed.ManagerUserName, rejected.AuditEntries.Last().Actor);
    }

    [Fact]
    public async Task Return_requires_revision_before_resubmission_and_preserves_audit()
    {
        using var employee = await CreateAuthenticatedClientAsync(DevelopmentSeed.EmployeeUserName);
        using var manager = await CreateAuthenticatedClientAsync(DevelopmentSeed.ManagerUserName);
        var created = await CreateAsync(employee, "Office", 100m);
        var submitted = await SubmitAsync(employee, created);
        var returned = await TransitionAsync(
            manager, created.Id, "return", submitted.RowVersion, "Add vendor detail");

        var premature = await employee.PostAsJsonAsync(
            $"/api/purchase-requests/{created.Id}/submit",
            new { rowVersion = returned.RowVersion, reason = "No revision" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, premature.StatusCode);

        var revisedResponse = await employee.PutAsJsonAsync(
            $"/api/purchase-requests/{created.Id}",
            PurchaseBody("Office", 125m, returned.RowVersion, "Added requested detail"),
            TestContext.Current.CancellationToken);
        Assert.True(
            revisedResponse.IsSuccessStatusCode,
            await revisedResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var revised = await revisedResponse.Content.ReadFromJsonAsync<PurchaseRequestResult>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(revised);
        Assert.Equal(PurchaseRequestStatus.Draft, revised.Status);

        var resubmitted = await SubmitAsync(employee, revised);
        Assert.Equal(PurchaseRequestStatus.PendingManagerApproval, resubmitted.Status);
        Assert.Equal(4, resubmitted.AuditEntries.Count);
        Assert.Collection(
            resubmitted.AuditEntries,
            audit => Assert.Equal(PurchaseRequestStatus.PendingManagerApproval, audit.ToStatus),
            audit => Assert.Equal(PurchaseRequestStatus.ReturnedForChanges, audit.ToStatus),
            audit =>
            {
                Assert.Equal(PurchaseRequestStatus.ReturnedForChanges, audit.FromStatus);
                Assert.Equal(PurchaseRequestStatus.Draft, audit.ToStatus);
            },
            audit => Assert.Equal(PurchaseRequestStatus.PendingManagerApproval, audit.ToStatus));
    }

    [Fact]
    public async Task Stale_revision_returns_concurrency_problem_details()
    {
        using var employee = await CreateAuthenticatedClientAsync(DevelopmentSeed.EmployeeUserName);
        var created = await CreateAsync(employee, "Office", 100m);
        var staleVersion = created.RowVersion;

        var first = await employee.PutAsJsonAsync(
            $"/api/purchase-requests/{created.Id}",
            PurchaseBody("Office", 110m, staleVersion, "First revision"),
            TestContext.Current.CancellationToken);
        Assert.True(
            first.IsSuccessStatusCode,
            await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var stale = await employee.PutAsJsonAsync(
            $"/api/purchase-requests/{created.Id}",
            PurchaseBody("Office", 120m, staleVersion, "Stale revision"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var problem = await stale.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        Assert.Equal("Concurrency conflict", problem.GetProperty("title").GetString());
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email, password = DevelopmentSeed.DemoPassword },
            TestContext.Current.CancellationToken);
        login.EnsureSuccessStatusCode();
        var payload = await login.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", payload.GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<PurchaseRequestResult> CreateAsync(
        HttpClient client,
        string category,
        decimal total)
    {
        var response = await client.PostAsJsonAsync(
            "/api/purchase-requests",
            PurchaseBody(category, total),
            TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (await response.Content.ReadFromJsonAsync<PurchaseRequestResult>(
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<PurchaseRequestResult> SubmitAsync(
        HttpClient client,
        PurchaseRequestResult request) =>
        await TransitionAsync(client, request.Id, "submit", request.RowVersion, "Ready for review");

    private static async Task<PurchaseRequestResult> TransitionAsync(
        HttpClient client,
        Guid id,
        string action,
        string rowVersion,
        string reason)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/purchase-requests/{id}/{action}",
            new { rowVersion, reason },
            TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (await response.Content.ReadFromJsonAsync<PurchaseRequestResult>(
            TestContext.Current.CancellationToken))!;
    }

    private static object PurchaseBody(
        string category,
        decimal total,
        string? rowVersion = null,
        string? reason = null) =>
        new
        {
            vendor = "Adventure Works",
            costCenter = "IT-200",
            category,
            businessJustification = "A sufficiently detailed integration-test business justification.",
            requestedDeliveryDate = "2030-02-01",
            lineItems = new[] { new { description = "Item", quantity = 1, unitPrice = total } },
            rowVersion,
            reason
        };
}

public sealed class ApprovalFlowApiFactory : WebApplicationFactory<Program>
{
    private const string DatabasePrefix = "ApprovalFlowIntegrationTests_";
    private const string MasterConnectionString =
        "Server=localhost,14333;Database=master;User Id=sa;Password=LocalOnly_ApprovalFlow_2026!;TrustServerCertificate=True;Encrypt=True";
    private readonly string _databaseName;
    private readonly string? _serviceBusConnectionString;
    private bool _databaseDropped;

    public ApprovalFlowApiFactory()
        : this($"{DatabasePrefix}{Guid.NewGuid():N}")
    {
    }

    internal ApprovalFlowApiFactory(string databaseName, string? serviceBusConnectionString = null)
    {
        ValidateDatabaseName(databaseName);
        _databaseName = databaseName;
        _serviceBusConnectionString = serviceBusConnectionString;
    }

    public string DatabaseName => _databaseName;

    public string ConnectionString =>
        $"Server=localhost,14333;Database={_databaseName};User Id=sa;Password=LocalOnly_ApprovalFlow_2026!;TrustServerCertificate=True;Encrypt=True";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting(
            "ConnectionStrings:ApprovalFlow",
            ConnectionString);
        if (_serviceBusConnectionString is not null)
            builder.UseSetting("ConnectionStrings:ServiceBus", _serviceBusConnectionString);
        builder.UseSetting("SeedDevelopmentData", "true");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && !_databaseDropped)
        {
            DropDatabase(_databaseName);
            _databaseDropped = true;
        }
    }

    public static bool DatabaseExists(string databaseName)
    {
        ValidateDatabaseName(databaseName);
        using var connection = new SqlConnection(MasterConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE [name] = @databaseName;";
        command.Parameters.AddWithValue("@databaseName", databaseName);
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }

    private static void DropDatabase(string databaseName)
    {
        ValidateDatabaseName(databaseName);
        using var connection = new SqlConnection(MasterConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        var quotedName = new SqlCommandBuilder().QuoteIdentifier(databaseName);
        command.CommandText = $"""
            IF DB_ID(@databaseName) IS NOT NULL
            BEGIN
                ALTER DATABASE {quotedName} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE {quotedName};
            END
            """;
        command.Parameters.AddWithValue("@databaseName", databaseName);
        command.ExecuteNonQuery();
    }

    private static void ValidateDatabaseName(string databaseName)
    {
        if (!databaseName.StartsWith(DatabasePrefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(databaseName[DatabasePrefix.Length..], "N", out _))
            throw new ArgumentException("Integration database name is outside the allowed exact-name pattern.");
    }
}
