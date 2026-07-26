using ApprovalFlow.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace ApprovalFlow.Api.IntegrationTests;

public sealed class TestcontainersSqlIsolationTests
{
    [Fact]
    public async Task Migrations_run_against_an_isolated_sql_server()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("APPROVALFLOW_TESTCONTAINERS"),
                "true",
                StringComparison.OrdinalIgnoreCase))
            return;

        var cancellationToken = TestContext.Current.CancellationToken;
        await using var sqlServer = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2025-CU1-ubuntu-22.04")
            .Build();

        await sqlServer.StartAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<ApprovalFlowDbContext>()
            .UseSqlServer(sqlServer.GetConnectionString())
            .Options;

        await using var dbContext = new ApprovalFlowDbContext(options);
        await dbContext.Database.MigrateAsync(cancellationToken);
        Assert.True((await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken)).Any());
        Assert.True(await dbContext.Database.CanConnectAsync(cancellationToken));
    }
}
