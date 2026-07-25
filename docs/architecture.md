# ApprovalFlow architecture

## Runtime shape

ApprovalFlow remains one modular monolith: a React/TypeScript SPA, one ASP.NET Core API, and one SQL Server database. During development, Vite serves the SPA and proxies `/api` to the API. The SPA does not connect to SQL Server and does not reproduce workflow authorization rules.

The backend projects retain their existing responsibilities:

- Domain owns request state transitions, finance-routing policy, self-approval prevention, and audit creation.
- Application owns authenticated use cases and role/resource authorization.
- Infrastructure owns Identity and EF Core SQL Server persistence.
- API owns HTTP contracts, bearer authentication, validation, and Problem Details.
- Web owns interaction state, accessible forms and tables, and presentation of server results.

## Authentication and authorization

The login form calls ASP.NET Core Identity's bearer-token endpoint. It stores only `accessToken` in `sessionStorage`, then calls the authenticated `/api/auth/session` endpoint to obtain the current user name and roles. It does not retain the returned refresh token, use local storage, expose registration, or accept a requester identity from the browser.

The SPA hides actions that do not match the active role, but the API remains authoritative. Detail authorization uses the existing resource checks. Lists use three fixed scopes rather than a caller-selected scope:

| Endpoint | Server-enforced scope |
|---|---|
| `/api/purchase-requests/mine` | Authenticated employee's user name |
| `/api/purchase-requests/manager-queue` | `PendingManagerApproval`, Manager role |
| `/api/purchase-requests/finance-queue` | `PendingFinanceApproval`, FinanceAdministrator role |

Pagination is one-based, page size is limited to 1–50, and sorting is restricted to last-modified or total in ascending/descending order with request ID as a deterministic tie-breaker. This is a bounded request-list feature, not a generalized query framework.

## Browser workflow

Employees can filter and page through owned requests, create a draft with one or more line items, inspect details and audit history, submit, and revise/resubmit a returned request. Managers and finance administrators have separate pending queues and can approve, reject, or return eligible requests. Reject and return require a reason in both the UI and API.

Each update sends the latest base64 `rowVersion` returned by the API. A `409` is never retried automatically: the UI presents the Problem Details message and an explicit reload action. Reloading discards the stale representation and obtains a current row version before the user chooses whether to act again.

## Testing boundary

Vitest covers session-only token persistence and typed Problem Details/concurrency handling. The Playwright workflow runs against the real API and a uniquely named SQL Server database. The lifecycle runner validates the `ApprovalFlowE2E_<32 lowercase hex characters>` name before any drop, terminates its exact ephemeral browser container, drops only that database, and queries `master` to verify removal. Traces, videos, and screenshots are retained only on failure.
