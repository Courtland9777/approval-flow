using ApprovalFlow.Infrastructure;
using ApprovalFlow.Worker;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("ApprovalFlow")
    ?? throw new InvalidOperationException("Connection string 'ApprovalFlow' is required.");
var serviceBusConnectionString = builder.Configuration.GetConnectionString("ServiceBus")
    ?? throw new InvalidOperationException("Connection string 'ServiceBus' is required.");

builder.Services.Configure<MessagingOptions>(builder.Configuration.GetSection("Messaging"));
builder.Services.AddDbContextFactory<ApprovalFlowDbContext>(
    options => options.UseSqlServer(connectionString));
builder.Services.AddSingleton(new ServiceBusClient(
    serviceBusConnectionString,
    new ServiceBusClientOptions
    {
        TransportType = ServiceBusTransportType.AmqpTcp,
        RetryOptions = new ServiceBusRetryOptions
        {
            MaxRetries = 0,
            TryTimeout = TimeSpan.FromSeconds(5),
        },
    }));
builder.Services.AddSingleton<MessagingTelemetry>();
builder.Services.AddHostedService<OutboxDispatcher>();
builder.Services.AddHostedService<IntegrationEventConsumer>();
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("ApprovalFlow.Worker"))
    .WithTracing(tracing => tracing
        .AddSource(MessagingTelemetry.ActivitySourceName)
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddMeter(MessagingTelemetry.MeterName)
        .AddRuntimeInstrumentation()
        .AddOtlpExporter());

await builder.Build().RunAsync();
