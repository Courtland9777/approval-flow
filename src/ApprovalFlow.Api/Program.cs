using ApprovalFlow.Api;
using ApprovalFlow.Application;
using ApprovalFlow.Domain;
using ApprovalFlow.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("ApprovalFlow")
    ?? throw new InvalidOperationException("Connection string 'ApprovalFlow' is required.");

builder.Services.AddOpenApi();
builder.Services.AddValidation();
builder.Services.AddDbContext<ApprovalFlowDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddScoped<IPurchaseRequestRepository, PurchaseRequestRepository>();
builder.Services.AddScoped<PurchaseRequestService>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
});

var app = builder.Build();
app.UseExceptionHandler(handler =>
{
    handler.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        if (exception is DomainValidationException validation)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Domain transition rejected",
                detail: validation.Message).ExecuteAsync(context);
            return;
        }

        await Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "An unexpected error occurred.",
            detail: app.Environment.IsDevelopment() ? exception?.ToString() : null).ExecuteAsync(context);
    });
});

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

var requests = app.MapGroup("/api/purchase-requests").WithTags("Purchase Requests");

requests.MapPost("/", async (
    CreatePurchaseRequestRequest request,
    PurchaseRequestService service,
    CancellationToken cancellationToken) =>
{
    if (request.RequestedDeliveryDate <= DateOnly.FromDateTime(DateTime.UtcNow))
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [nameof(request.RequestedDeliveryDate)] = ["Requested delivery date must be in the future."]
        });

    var created = await service.CreateAsync(request.ToCommand(), cancellationToken);
    return Results.Created($"/api/purchase-requests/{created.Id}", created);
}).Produces<PurchaseRequestResult>(StatusCodes.Status201Created)
  .ProducesValidationProblem();

requests.MapGet("/{id:guid}", async (
    Guid id,
    PurchaseRequestService service,
    CancellationToken cancellationToken) =>
{
    var result = await service.GetAsync(id, cancellationToken);
    return result is null ? Results.NotFound() : Results.Ok(result);
}).Produces<PurchaseRequestResult>()
  .Produces(StatusCodes.Status404NotFound);

requests.MapPost("/{id:guid}/submit", async (
    Guid id,
    SubmitPurchaseRequestRequest request,
    PurchaseRequestService service,
    CancellationToken cancellationToken) =>
{
    var result = await service.SubmitAsync(id, request.ToCommand(), cancellationToken);
    return result is null ? Results.NotFound() : Results.Ok(result);
}).Produces<PurchaseRequestResult>()
  .ProducesProblem(StatusCodes.Status409Conflict)
  .Produces(StatusCodes.Status404NotFound);

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ApprovalFlowDbContext>();
    if (builder.Configuration.GetValue("SeedDevelopmentData", true))
        await DevelopmentSeed.SeedAsync(dbContext);
    else
        await dbContext.Database.MigrateAsync();
}

app.Run();

public partial class Program;
