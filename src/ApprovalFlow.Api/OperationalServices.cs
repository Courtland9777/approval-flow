using System.Diagnostics;
using System.Diagnostics.Metrics;
using ApprovalFlow.Application;
using Azure.Messaging.ServiceBus;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ApprovalFlow.Api;

public sealed class HttpCorrelationContext(IHttpContextAccessor accessor) : ICorrelationContext
{
    public string CorrelationId =>
        accessor.HttpContext?.Items[CorrelationMiddleware.ItemName] as string
        ?? Activity.Current?.TraceId.ToString()
        ?? Guid.NewGuid().ToString("N");
}

public sealed class CorrelationMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemName = "ApprovalFlow.CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var candidate = context.Request.Headers[HeaderName].FirstOrDefault();
        var correlationId = IsValid(candidate)
            ? candidate!
            : Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        context.Items[ItemName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;
        using (context.RequestServices.GetRequiredService<ILogger<CorrelationMiddleware>>()
                   .BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
            await next(context);
    }

    private static bool IsValid(string? value) =>
        value is { Length: > 0 and <= 100 }
        && value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.');
}

public sealed class ApiTelemetry : IDisposable
{
    public const string MeterName = "ApprovalFlow.Api";
    private readonly Meter _meter = new(MeterName);
    public Counter<long> TransitionRequests { get; }

    public ApiTelemetry() =>
        TransitionRequests = _meter.CreateCounter<long>("approvalflow.api.transition_requests");

    public void Dispose() => _meter.Dispose();
}

public sealed class SqlReadinessHealthCheck(
    IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var configured = configuration.GetConnectionString("ApprovalFlow")
                ?? throw new InvalidOperationException("Connection string 'ApprovalFlow' is required.");
            var builder = new SqlConnectionStringBuilder(configured)
            {
                ConnectTimeout = 3,
            };
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            return HealthCheckResult.Healthy("SQL Server is reachable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("SQL Server readiness check failed.", exception);
        }
    }
}

public sealed class ServiceBusReadinessHealthCheck(
    ServiceBusClient client,
    IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var queueName = configuration["Messaging:QueueName"] ?? "approvalflow-workflow-events";
            await using var receiver = client.CreateReceiver(queueName);
            await receiver.PeekMessageAsync(cancellationToken: cancellationToken);
            return HealthCheckResult.Healthy("Service Bus queue is reachable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Service Bus readiness check failed.", exception);
        }
    }
}
