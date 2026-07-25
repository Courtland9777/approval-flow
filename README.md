# ApprovalFlow

ApprovalFlow is a local-first internal purchasing API built as a .NET modular monolith. Its current vertical slice authenticates users, enforces employee ownership and reviewer roles, routes deterministic approvals, records an audit trail, and rejects stale writes with SQL Server optimistic concurrency.

## Quick start

Requirements: .NET 10 SDK, Docker, and Docker Compose.

```bash
docker compose up -d sqlserver
dotnet restore ApprovalFlow.slnx
dotnet run --project src/ApprovalFlow.Api
```

The API starts on `http://localhost:5080`. OpenAPI is available in Development at `http://localhost:5080/openapi/v1.json`; SQL Server is exposed locally on port `14333`.

### Local-only demo accounts

These seeded credentials are non-sensitive demonstration data. They must never be reused or deployed:

| Account | Roles | Password |
|---|---|---|
| `employee.demo@local.test` | Employee | `LocalOnly!2026` |
| `employee2.demo@local.test` | Employee | `LocalOnly!2026` |
| `manager.demo@local.test` | Employee, Manager | `LocalOnly!2026` |
| `finance.demo@local.test` | FinanceAdministrator | `LocalOnly!2026` |

The manager has the Employee role as well so the local workflow can demonstrate the defense against approving one's own request.

### Login and authenticated requests

Login uses ASP.NET Core Identity's built-in local bearer-token endpoint:

```bash
curl -X POST 'http://localhost:5080/api/auth/login' \
  -H 'Content-Type: application/json' \
  -d '{"email":"employee.demo@local.test","password":"LocalOnly!2026"}'
```

Copy the returned `accessToken` into a shell variable:

```bash
EMPLOYEE_TOKEN='<accessToken>'
```

Create a request. Requester identity is derived from the bearer token; no requester field is accepted:

```bash
curl -X POST http://localhost:5080/api/purchase-requests \
  -H "Authorization: Bearer $EMPLOYEE_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"vendor":"Northwind Office Supply","costCenter":"ENG-100","category":"Software","businessJustification":"Development tooling for the engineering team.","requestedDeliveryDate":"2030-01-15","lineItems":[{"description":"Tool license","quantity":2,"unitPrice":600.00}]}'
```

Every mutation accepts the current response's base64 `rowVersion`. A stale value returns `409 application/problem+json`:

```bash
curl -X POST http://localhost:5080/api/purchase-requests/{id}/submit \
  -H "Authorization: Bearer $EMPLOYEE_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"rowVersion":"<current-rowVersion>","reason":"Ready for manager review"}'
```

Login as the manager, then approve. A $1,200 Software request routes to finance:

```bash
curl -X POST 'http://localhost:5080/api/auth/login' \
  -H 'Content-Type: application/json' \
  -d '{"email":"manager.demo@local.test","password":"LocalOnly!2026"}'

MANAGER_TOKEN='<accessToken>'

curl -X POST http://localhost:5080/api/purchase-requests/{id}/approve \
  -H "Authorization: Bearer $MANAGER_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"rowVersion":"<current-rowVersion>","reason":"Manager approved"}'
```

The same reviewer endpoint supports `approve`, `reject`, and `return`. Reject and return require a reason. Returned requests must be revised with `PUT /api/purchase-requests/{id}` before the employee can resubmit.

Set `ConnectionStrings__ApprovalFlow` to override the non-secret development connection string. The Compose password and demo-user password are local demonstration values only.

## Authorization and workflow

- Employees create, view, revise, and submit only requests they own.
- Managers can view submitted/reviewed requests and act only during manager review.
- Finance administrators can view finance-relevant requests and act only during finance review.
- Employees cannot approve, reject, or return requests. The aggregate also prohibits any requester from deciding their own request.
- Submission moves `Draft` to `PendingManagerApproval`.
- Manager approval moves to `Approved`, except totals of at least `$1,000` or categories `Software`/`Security`, which move to `PendingFinanceApproval`.
- Finance approval moves `PendingFinanceApproval` to `Approved`.
- An eligible manager or finance administrator can reject or return with a required reason.
- Revision moves `ReturnedForChanges` back to `Draft`; only then can it be resubmitted.
- Each material change records authenticated actor, timestamp, prior state, new state, and reason.

## Architecture

- `ApprovalFlow.Domain`: aggregate, line items, explicit states, transition and self-approval rules, audit entries.
- `ApprovalFlow.Application`: authenticated use-case orchestration, role/resource checks, and API result contracts.
- `ApprovalFlow.Infrastructure`: ASP.NET Core Identity and application persistence in one SQL Server database, migrations, row-version concurrency, and local demo seed.
- `ApprovalFlow.Api`: local bearer authentication, HTTP/OpenAPI endpoints, validation, and RFC Problem Details.
- `ApprovalFlow.Domain.UnitTests`: focused transition and policy tests.
- `ApprovalFlow.Api.IntegrationTests`: SQL Server-backed authentication, authorization, workflow, audit, and concurrency tests.

The application remains a modular monolith. React, messaging/outbox/worker, Aspire, telemetry, cloud resources, deployment, and screenshots belong to later sequences.

## Validation

With SQL Server running:

```bash
dotnet restore ApprovalFlow.slnx
dotnet build ApprovalFlow.slnx --no-restore
dotnet test ApprovalFlow.slnx --no-build
dotnet format ApprovalFlow.slnx --verify-no-changes --no-restore
git diff --check
docker compose config --quiet
```
