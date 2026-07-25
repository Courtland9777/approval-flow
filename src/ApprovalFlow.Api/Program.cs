using System.Security.Claims;
using ApprovalFlow.Api;
using ApprovalFlow.Application;
using ApprovalFlow.Domain;
using ApprovalFlow.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("ApprovalFlow")
    ?? throw new InvalidOperationException("Connection string 'ApprovalFlow' is required.");

builder.Services.AddOpenApi();
builder.Services.AddValidation();
builder.Services.AddDbContext<ApprovalFlowDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddAuthorization();
builder.Services.AddIdentityApiEndpoints<IdentityUser>(options =>
    {
        options.User.RequireUniqueEmail = false;
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApprovalFlowDbContext>();
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
        var problem = exception switch
        {
            DomainValidationException validation => (
                StatusCodes.Status400BadRequest, "Validation failed", validation.Message),
            DomainAuthorizationException authorization => (
                StatusCodes.Status403Forbidden, "Authorization denied", authorization.Message),
            DomainConflictException conflict => (
                StatusCodes.Status409Conflict, "Domain transition rejected", conflict.Message),
            DbUpdateConcurrencyException => (
                StatusCodes.Status409Conflict, "Concurrency conflict",
                "The request changed after it was read. Reload it and retry with the latest rowVersion."),
            _ => (
                StatusCodes.Status500InternalServerError, "An unexpected error occurred.",
                app.Environment.IsDevelopment() ? exception?.ToString() : null)
        };

        context.Response.StatusCode = problem.Item1;
        await Results.Problem(
            statusCode: problem.Item1,
            title: problem.Item2,
            detail: problem.Item3).ExecuteAsync(context);
    });
});
app.UseStatusCodePages(async statusCodeContext =>
{
    var response = statusCodeContext.HttpContext.Response;
    if (response.StatusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden)
    {
        await Results.Problem(
            statusCode: response.StatusCode,
            title: response.StatusCode == StatusCodes.Status401Unauthorized
                ? "Authentication required"
                : "Authorization denied").ExecuteAsync(statusCodeContext.HttpContext);
    }
});
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapGroup("/api/auth").WithTags("Authentication").MapIdentityApi<IdentityUser>();

var requests = app.MapGroup("/api/purchase-requests")
    .WithTags("Purchase Requests")
    .RequireAuthorization();

requests.MapPost("/", async (
    CreatePurchaseRequestRequest request,
    ClaimsPrincipal principal,
    PurchaseRequestService service,
    CancellationToken cancellationToken) =>
{
    var dateValidation = ValidateDeliveryDate(request.RequestedDeliveryDate);
    if (dateValidation is not null)
        return dateValidation;
    var created = await service.CreateAsync(request.ToCommand(), ToActor(principal), cancellationToken);
    return Results.Created($"/api/purchase-requests/{created.Id}", created);
}).RequireAuthorization(policy => policy.RequireRole(ApprovalFlowRoles.Employee))
  .Produces<PurchaseRequestResult>(StatusCodes.Status201Created)
  .ProducesValidationProblem()
  .ProducesProblem(StatusCodes.Status401Unauthorized)
  .ProducesProblem(StatusCodes.Status403Forbidden);

requests.MapGet("/{id:guid}", async (
    Guid id,
    ClaimsPrincipal principal,
    PurchaseRequestService service,
    CancellationToken cancellationToken) =>
{
    var result = await service.GetAsync(id, ToActor(principal), cancellationToken);
    return result is null ? Results.NotFound() : Results.Ok(result);
}).Produces<PurchaseRequestResult>()
  .ProducesProblem(StatusCodes.Status401Unauthorized)
  .ProducesProblem(StatusCodes.Status403Forbidden)
  .Produces(StatusCodes.Status404NotFound);

requests.MapPut("/{id:guid}", async (
    Guid id,
    RevisePurchaseRequestRequest request,
    ClaimsPrincipal principal,
    PurchaseRequestService service,
    CancellationToken cancellationToken) =>
{
    var dateValidation = ValidateDeliveryDate(request.RequestedDeliveryDate);
    if (dateValidation is not null)
        return dateValidation;
    var result = await service.ReviseAsync(id, request.ToCommand(), ToActor(principal), cancellationToken);
    return result is null ? Results.NotFound() : Results.Ok(result);
}).RequireAuthorization(policy => policy.RequireRole(ApprovalFlowRoles.Employee))
  .Produces<PurchaseRequestResult>()
  .ProducesValidationProblem()
  .ProducesProblem(StatusCodes.Status409Conflict);

requests.MapPost("/{id:guid}/submit", async (
    Guid id,
    TransitionPurchaseRequestRequest request,
    ClaimsPrincipal principal,
    PurchaseRequestService service,
    CancellationToken cancellationToken) =>
{
    var result = await service.SubmitAsync(id, request.ToCommand(), ToActor(principal), cancellationToken);
    return result is null ? Results.NotFound() : Results.Ok(result);
}).RequireAuthorization(policy => policy.RequireRole(ApprovalFlowRoles.Employee))
  .Produces<PurchaseRequestResult>()
  .ProducesProblem(StatusCodes.Status409Conflict)
  .Produces(StatusCodes.Status404NotFound);

requests.MapPost("/{id:guid}/approve", async (
    Guid id,
    TransitionPurchaseRequestRequest request,
    ClaimsPrincipal principal,
    PurchaseRequestService service,
    CancellationToken cancellationToken) =>
{
    var result = await service.ApproveAsync(id, request.ToCommand(), ToActor(principal), cancellationToken);
    return result is null ? Results.NotFound() : Results.Ok(result);
}).RequireAuthorization(policy => policy.RequireRole(
    ApprovalFlowRoles.Manager, ApprovalFlowRoles.FinanceAdministrator))
  .Produces<PurchaseRequestResult>()
  .ProducesProblem(StatusCodes.Status403Forbidden)
  .ProducesProblem(StatusCodes.Status409Conflict);

requests.MapPost("/{id:guid}/reject", async (
    Guid id,
    RequiredReasonTransitionRequest request,
    ClaimsPrincipal principal,
    PurchaseRequestService service,
    CancellationToken cancellationToken) =>
{
    var result = await service.RejectAsync(id, request.ToCommand(), ToActor(principal), cancellationToken);
    return result is null ? Results.NotFound() : Results.Ok(result);
}).RequireAuthorization(policy => policy.RequireRole(
    ApprovalFlowRoles.Manager, ApprovalFlowRoles.FinanceAdministrator))
  .Produces<PurchaseRequestResult>()
  .ProducesValidationProblem()
  .ProducesProblem(StatusCodes.Status409Conflict);

requests.MapPost("/{id:guid}/return", async (
    Guid id,
    RequiredReasonTransitionRequest request,
    ClaimsPrincipal principal,
    PurchaseRequestService service,
    CancellationToken cancellationToken) =>
{
    var result = await service.ReturnAsync(id, request.ToCommand(), ToActor(principal), cancellationToken);
    return result is null ? Results.NotFound() : Results.Ok(result);
}).RequireAuthorization(policy => policy.RequireRole(
    ApprovalFlowRoles.Manager, ApprovalFlowRoles.FinanceAdministrator))
  .Produces<PurchaseRequestResult>()
  .ProducesValidationProblem()
  .ProducesProblem(StatusCodes.Status409Conflict);

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var services = scope.ServiceProvider;
    var dbContext = services.GetRequiredService<ApprovalFlowDbContext>();
    await dbContext.Database.MigrateAsync();
    if (builder.Configuration.GetValue("SeedDevelopmentData", true))
        await DevelopmentSeed.SeedAsync(services);
}

app.Run();

static AuthenticatedActor ToActor(ClaimsPrincipal principal)
{
    var userName = principal.Identity?.Name
        ?? throw new DomainAuthorizationException("The authenticated principal has no user name.");
    return new AuthenticatedActor(
        userName,
        principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToHashSet(StringComparer.Ordinal));
}

static IResult? ValidateDeliveryDate(DateOnly value) =>
    value <= DateOnly.FromDateTime(DateTime.UtcNow)
        ? Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [nameof(value)] = ["Requested delivery date must be in the future."]
        })
        : null;

public partial class Program;
