# Independent public-readiness checklist

This checklist prepares evidence; it does not approve visibility, publication, deployment, or career claims.

| # | Acceptance criterion | Status | Exact repository evidence |
|---|---|---|---|
| 1 | Product is understandable from README opening | Ready for independent verification | `README.md` opening and “How it works” |
| 2 | Complete system starts through one workflow | Ready for independent verification | `scripts/start-local.sh`, `docker-compose.yaml` |
| 3 | Seeded non-sensitive accounts work | Ready for independent verification | README demo table, `DevelopmentSeed.cs` |
| 4 | Employee submits a request | Ready for independent verification | Playwright primary workflow, employee screenshot |
| 5 | Manager/finance decisions work correctly | Ready for independent verification | domain/API tests, Playwright workflow |
| 6 | Role/resource authorization is meaningful | Ready for independent verification | API integration tests, security review |
| 7 | Complete audit history is visible | Ready for independent verification | Playwright assertions, finance/audit screenshot |
| 8 | OpenAPI is inspectable | Ready for independent verification | `/openapi/v1.json`, `Program.cs` |
| 9 | Async processing/retries/failures are inspectable | Ready for independent verification | async tests, `docs/operations.md`, message diagram |
| 10 | CI passes | Ready for independent verification | PR #5 CI run `30181659860` passed the complete application job |
| 11 | Representative unit/integration/E2E tests exist | Ready for independent verification | `tests/`, web tests, CI workflow |
| 12 | Architecture/tradeoffs/diagram are clear | Ready for independent verification | `docs/architecture.md` |
| 13 | Screenshots/short recording are viewable | Ready for independent verification | `docs/media/` and `docs/media.md` |
| 14 | No Azure account/payment/live deployment required | Ready for independent verification | README, Compose, architecture non-goals |
| 15 | No secrets/private paths/false claims/stale wording | Ready for independent verification | security review; independent final scan remains required |
