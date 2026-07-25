# ApprovalFlow

ApprovalFlow is a small internal purchasing API that demonstrates an executable .NET modular-monolith vertical slice. Sequence 1 supports creating a draft purchase request, retrieving it, and submitting it through a validated domain transition with an audit record.

## Quick start

Requirements: .NET 10 SDK, Docker, and Docker Compose.

```bash
docker compose up -d sqlserver
dotnet restore ApprovalFlow.slnx
dotnet run --project src/ApprovalFlow.Api
```

OpenAPI is available in Development at `http://localhost:5080/openapi/v1.json`. The API starts on `http://localhost:5080`; SQL Server is exposed locally on port `14333`.

Create and submit a request:

```bash
curl -X POST http://localhost:5080/api/purchase-requests \
  -H 'Content-Type: application/json' \
  -d '{"vendor":"Northwind Office Supply","costCenter":"ENG-100","category":"OfficeSupplies","businessJustification":"Ergonomic equipment for the development team.","requestedDeliveryDate":"2030-01-15","requester":"employee.demo","lineItems":[{"description":"Ergonomic keyboard","quantity":2,"unitPrice":89.50}]}'

curl -X POST http://localhost:5080/api/purchase-requests/{id}/submit \
  -H 'Content-Type: application/json' \
  -d '{"actor":"employee.demo","reason":"Ready for review"}'
```

Set `ConnectionStrings__ApprovalFlow` to override the non-secret development connection string. The Compose password is local demonstration data only; do not reuse it.

## Architecture

- `ApprovalFlow.Domain`: purchase-request aggregate, line items, states, transition validation, and audit entries.
- `ApprovalFlow.Application`: use-case contracts and orchestration.
- `ApprovalFlow.Infrastructure`: EF Core SQL Server persistence, migration, and development seed.
- `ApprovalFlow.Api`: HTTP/OpenAPI presentation and validation.
- `ApprovalFlow.Domain.UnitTests`: transition-rule tests.
- `ApprovalFlow.Api.IntegrationTests`: SQL Server-backed create/get/submit verification.

The application is intentionally a modular monolith. Authentication, approval roles, UI, messaging/outbox/worker, Aspire, telemetry, and cloud deployment belong to later sequences.

## Validation

With SQL Server running:

```bash
dotnet restore ApprovalFlow.slnx
dotnet build ApprovalFlow.slnx --no-restore
dotnet test ApprovalFlow.slnx --no-build
dotnet format ApprovalFlow.slnx --verify-no-changes --no-restore
docker compose config
```
