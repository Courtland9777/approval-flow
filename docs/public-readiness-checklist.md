# Independent public-readiness checklist

This checklist maps the independently verified recruiter/public-readiness evidence. Visibility, deployment, and career-use authority remain separate decisions.

| # | Acceptance criterion | Status | Exact repository evidence |
|---|---|---|---|
| 1 | Product is understandable from README opening | Verified | `README.md` opening and “How it works” |
| 2 | Complete system starts through one workflow | Verified | `scripts/start-local.sh`, `docker-compose.yaml` |
| 3 | Seeded non-sensitive accounts work | Verified | README demo table, `DevelopmentSeed.cs` |
| 4 | Employee submits a request | Verified | Playwright primary workflow, employee screenshot |
| 5 | Manager/finance decisions work correctly | Verified | domain/API tests, Playwright workflow |
| 6 | Role/resource authorization is meaningful | Verified | API integration tests, security review |
| 7 | Complete audit history is visible | Verified | Playwright assertions, finance/audit screenshot |
| 8 | OpenAPI is inspectable | Verified | `/openapi/v1.json`, `Program.cs` |
| 9 | Async processing/retries/failures are inspectable | Verified | async tests, `docs/operations.md`, message diagram |
| 10 | CI passes | Verified | `.github/workflows/ci.yml`, `scripts/validate.sh`; latest successful applicable default-branch or pull-request run, independently verified |
| 11 | Representative unit/integration/E2E tests exist | Verified | `tests/`, web tests, CI workflow |
| 12 | Architecture/tradeoffs/diagram are clear | Verified | `docs/architecture.md` |
| 13 | Screenshots/short recording are viewable | Verified | `docs/media/` and `docs/media.md` |
| 14 | No Azure account/payment/live deployment required | Verified | README, Compose, architecture non-goals |
| 15 | No secrets/private paths/false claims/stale wording | Verified | security review and final tracked public-surface scan |
