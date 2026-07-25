# ApprovalFlow

ApprovalFlow is a local-first internal purchasing application built as a .NET modular monolith. Its React workflow authenticates employees, managers, and finance administrators against the ASP.NET Core API, enforces ownership and reviewer roles, routes deterministic approvals, records an audit trail, and rejects stale writes with SQL Server optimistic concurrency.

## Quick start

Requirements: .NET 10 SDK, Docker with Docker Compose, and Node.js 24 LTS with npm.

```bash
docker compose up -d sqlserver
dotnet restore ApprovalFlow.slnx
cd src/ApprovalFlow.Web
npm ci
npm run dev
```

The one development command starts the API on `http://localhost:5080` and the SPA on `http://127.0.0.1:5173`; Vite proxies `/api` to the API. OpenAPI is available in Development at `http://localhost:5080/openapi/v1.json`; SQL Server is exposed locally on port `14333`.

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
- `ApprovalFlow.Web`: Vite React/TypeScript SPA, session-scoped authentication, role workspaces, accessible request forms, and the Playwright lifecycle runner.
- `ApprovalFlow.Domain.UnitTests`: focused transition and policy tests.
- `ApprovalFlow.Api.IntegrationTests`: SQL Server-backed authentication, authorization, workflow, audit, and concurrency tests.

The SPA keeps server data in component state and uses a small typed fetch layer; it has no generalized client state/query framework. The bearer access token is stored only in browser `sessionStorage`. Refresh tokens are not retained. Logout clears the token locally, and there is no registration UI.

Three explicit server-scoped list endpoints prevent callers from changing a query parameter to broaden access:

- `GET /api/purchase-requests/mine` derives the owner from the bearer principal and supports status filtering.
- `GET /api/purchase-requests/manager-queue` always returns pending manager work and requires the Manager role.
- `GET /api/purchase-requests/finance-queue` always returns pending finance work and requires the FinanceAdministrator role.

All lists use bounded page sizes (maximum 50) and deterministic sorting. See [`docs/architecture.md`](docs/architecture.md) for the UI/API boundaries and concurrency behavior.

The application remains a modular monolith. Messaging/outbox/worker, Aspire, telemetry, cloud resources, deployment, and screenshots belong to later sequences.

## Frontend and end-to-end tests

```bash
cd src/ApprovalFlow.Web
npm ci
npm run lint
npm test
npm run build
npx playwright install chromium
npm run e2e
```

`npm run e2e` requires the Compose SQL Server to be healthy. It creates a unique database named `ApprovalFlowE2E_<GUID>`, starts the real API and SPA, and runs the primary workflow without API mocks. Its `finally` cleanup validates the exact database name, drops only that database, and verifies removal. On hosts missing Chromium system libraries, the runner uses the version-pinned official Playwright browser-server image; the container is ephemeral and uses host networking only for the local test servers.

## Validation

With SQL Server running:

```bash
dotnet restore ApprovalFlow.slnx
dotnet build ApprovalFlow.slnx --no-restore
dotnet test ApprovalFlow.slnx --no-build
dotnet format ApprovalFlow.slnx --verify-no-changes --no-restore
cd src/ApprovalFlow.Web
npm ci
npm run lint
npm test
npm run build
npx playwright install chromium
npm run e2e
cd ../..
git diff --check
docker compose config --quiet
```
