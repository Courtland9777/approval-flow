using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ApprovalFlow.Application;
using ApprovalFlow.Domain;
using ApprovalFlow.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ApprovalFlow.Api.IntegrationTests;

public sealed class MigrationAndDatabaseLifecycleTests
{
    [Fact]
    public async Task Phase1_submitted_request_is_upgraded_and_manager_can_continue_workflow()
    {
        var databaseName = $"ApprovalFlowIntegrationTests_{Guid.NewGuid():N}";
        using var factory = new ApprovalFlowApiFactory(databaseName);
        var requestId = Guid.NewGuid();
        var auditId = Guid.NewGuid();
        var lineItemId = Guid.NewGuid();
        var cancellationToken = TestContext.Current.CancellationToken;

        await CreatePhase1DatabaseAsync(databaseName, factory.ConnectionString, cancellationToken);

        await InsertPhase1SubmittedRequestAsync(
            factory.ConnectionString,
            requestId,
            auditId,
            lineItemId,
            cancellationToken);

        await using (var dbContext = CreateDbContext(factory.ConnectionString))
            await dbContext.Database.MigrateAsync(cancellationToken);

        await VerifyConvertedRowsAsync(
            factory.ConnectionString,
            requestId,
            auditId,
            cancellationToken);

        using var manager = factory.CreateClient();
        await AuthenticateAsync(manager, DevelopmentSeed.ManagerUserName, cancellationToken);
        var upgraded = await manager.GetFromJsonAsync<PurchaseRequestResult>(
            $"/api/purchase-requests/{requestId}",
            cancellationToken);
        Assert.NotNull(upgraded);
        Assert.Equal(PurchaseRequestStatus.PendingManagerApproval, upgraded.Status);

        var approval = await manager.PostAsJsonAsync(
            $"/api/purchase-requests/{requestId}/approve",
            new { rowVersion = upgraded.RowVersion, reason = "Continued after Phase 2 upgrade" },
            cancellationToken);
        Assert.True(
            approval.IsSuccessStatusCode,
            await approval.Content.ReadAsStringAsync(cancellationToken));
        var approved = await approval.Content.ReadFromJsonAsync<PurchaseRequestResult>(cancellationToken);
        Assert.NotNull(approved);
        Assert.Equal(PurchaseRequestStatus.Approved, approved.Status);
        Assert.Collection(
            approved.AuditEntries,
            entry =>
            {
                Assert.Equal(PurchaseRequestStatus.Draft, entry.FromStatus);
                Assert.Equal(PurchaseRequestStatus.PendingManagerApproval, entry.ToStatus);
            },
            entry =>
            {
                Assert.Equal(PurchaseRequestStatus.PendingManagerApproval, entry.FromStatus);
                Assert.Equal(PurchaseRequestStatus.Approved, entry.ToStatus);
                Assert.Equal(DevelopmentSeed.ManagerUserName, entry.Actor);
            });
    }

    [Fact]
    public void Factory_disposal_removes_only_its_exact_database()
    {
        var factory = new ApprovalFlowApiFactory();
        var databaseName = factory.DatabaseName;
        using (var client = factory.CreateClient())
        {
            Assert.True(ApprovalFlowApiFactory.DatabaseExists(databaseName));
        }

        factory.Dispose();

        Assert.False(ApprovalFlowApiFactory.DatabaseExists(databaseName));
    }

    private static ApprovalFlowDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApprovalFlowDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new ApprovalFlowDbContext(options);
    }

    private static async Task CreatePhase1DatabaseAsync(
        string databaseName,
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using (var master = new SqlConnection(
                         "Server=localhost,14333;Database=master;User Id=sa;Password=LocalOnly_ApprovalFlow_2026!;TrustServerCertificate=True;Encrypt=True"))
        {
            await master.OpenAsync(cancellationToken);
            await using var createDatabase = master.CreateCommand();
            var quotedName = new SqlCommandBuilder().QuoteIdentifier(databaseName);
            createDatabase.CommandText = $"CREATE DATABASE {quotedName};";
            await createDatabase.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE [__EFMigrationsHistory] (
                [MigrationId] nvarchar(150) NOT NULL,
                [ProductVersion] nvarchar(32) NOT NULL,
                CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
            );

            CREATE TABLE [PurchaseRequests] (
                [Id] uniqueidentifier NOT NULL,
                [Vendor] nvarchar(200) NOT NULL,
                [CostCenter] nvarchar(50) NOT NULL,
                [Category] nvarchar(100) NOT NULL,
                [BusinessJustification] nvarchar(2000) NOT NULL,
                [RequestedDeliveryDate] date NOT NULL,
                [Requester] nvarchar(100) NOT NULL,
                [Status] nvarchar(40) NOT NULL,
                [CreatedAt] datetimeoffset NOT NULL,
                CONSTRAINT [PK_PurchaseRequests] PRIMARY KEY ([Id])
            );

            CREATE TABLE [PurchaseRequestAuditEntries] (
                [Id] uniqueidentifier NOT NULL,
                [PurchaseRequestId] uniqueidentifier NOT NULL,
                [Actor] nvarchar(100) NOT NULL,
                [OccurredAt] datetimeoffset NOT NULL,
                [FromStatus] nvarchar(40) NOT NULL,
                [ToStatus] nvarchar(40) NOT NULL,
                [Reason] nvarchar(1000) NULL,
                CONSTRAINT [PK_PurchaseRequestAuditEntries] PRIMARY KEY ([Id]),
                CONSTRAINT [FK_PurchaseRequestAuditEntries_PurchaseRequests_PurchaseRequestId]
                    FOREIGN KEY ([PurchaseRequestId]) REFERENCES [PurchaseRequests] ([Id]) ON DELETE CASCADE
            );

            CREATE TABLE [PurchaseRequestLineItems] (
                [Id] uniqueidentifier NOT NULL,
                [PurchaseRequestId] uniqueidentifier NOT NULL,
                [Description] nvarchar(500) NOT NULL,
                [Quantity] int NOT NULL,
                [UnitPrice] decimal(18,2) NOT NULL,
                CONSTRAINT [PK_PurchaseRequestLineItems] PRIMARY KEY ([Id]),
                CONSTRAINT [FK_PurchaseRequestLineItems_PurchaseRequests_PurchaseRequestId]
                    FOREIGN KEY ([PurchaseRequestId]) REFERENCES [PurchaseRequests] ([Id]) ON DELETE CASCADE
            );

            CREATE INDEX [IX_PurchaseRequestAuditEntries_PurchaseRequestId_OccurredAt]
                ON [PurchaseRequestAuditEntries] ([PurchaseRequestId], [OccurredAt]);
            CREATE INDEX [IX_PurchaseRequestLineItems_PurchaseRequestId]
                ON [PurchaseRequestLineItems] ([PurchaseRequestId]);

            INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                VALUES (N'202607240001_InitialCreate', N'10.0.10');
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertPhase1SubmittedRequestAsync(
        string connectionString,
        Guid requestId,
        Guid auditId,
        Guid lineItemId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO [PurchaseRequests]
                ([Id], [Vendor], [CostCenter], [Category], [BusinessJustification],
                 [RequestedDeliveryDate], [Requester], [Status], [CreatedAt])
            VALUES
                (@requestId, N'Phase 1 Vendor', N'OPS-100', N'Office',
                 N'Valid submitted Phase 1 request for migration verification.',
                 '2030-05-01', @requester, N'Submitted', @occurredAt);

            INSERT INTO [PurchaseRequestLineItems]
                ([Id], [PurchaseRequestId], [Description], [Quantity], [UnitPrice])
            VALUES
                (@lineItemId, @requestId, N'Phase 1 item', 1, 100.00);

            INSERT INTO [PurchaseRequestAuditEntries]
                ([Id], [PurchaseRequestId], [Actor], [OccurredAt], [FromStatus], [ToStatus], [Reason])
            VALUES
                (@auditId, @requestId, @requester, @occurredAt, N'Draft', N'Submitted', N'Ready');
            """;
        command.Parameters.AddWithValue("@requestId", requestId);
        command.Parameters.AddWithValue("@lineItemId", lineItemId);
        command.Parameters.AddWithValue("@auditId", auditId);
        command.Parameters.AddWithValue("@requester", DevelopmentSeed.EmployeeUserName);
        command.Parameters.AddWithValue("@occurredAt", new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task VerifyConvertedRowsAsync(
        string connectionString,
        Guid requestId,
        Guid auditId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var requestCommand = connection.CreateCommand();
        requestCommand.CommandText = "SELECT [Status] FROM [PurchaseRequests] WHERE [Id] = @id;";
        requestCommand.Parameters.AddWithValue("@id", requestId);
        Assert.Equal(
            "PendingManagerApproval",
            Convert.ToString(await requestCommand.ExecuteScalarAsync(cancellationToken)));

        await using var auditCommand = connection.CreateCommand();
        auditCommand.CommandText =
            "SELECT CONCAT([FromStatus], N'->', [ToStatus]) FROM [PurchaseRequestAuditEntries] WHERE [Id] = @id;";
        auditCommand.Parameters.AddWithValue("@id", auditId);
        Assert.Equal(
            "Draft->PendingManagerApproval",
            Convert.ToString(await auditCommand.ExecuteScalarAsync(cancellationToken)));
    }

    private static async Task AuthenticateAsync(
        HttpClient client,
        string email,
        CancellationToken cancellationToken)
    {
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email, password = DevelopmentSeed.DemoPassword },
            cancellationToken);
        login.EnsureSuccessStatusCode();
        var payload = await login.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", payload.GetProperty("accessToken").GetString());
    }
}
