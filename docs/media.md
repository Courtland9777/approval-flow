# Demonstration media

Committed media is generated locally from seeded `.local.test` accounts and a generated sample request:

- `docs/media/employee-request.png`
- `docs/media/manager-review.png`
- `docs/media/finance-audit.png`
- `docs/media/local-observability.png`

To refresh, start the canonical stack and run `npm run media` in `src/ApprovalFlow.Web`. Inspect every artifact before committing: no access token, private path, personal information, browser chrome, terminal, desktop, or unrelated content. The refresh workflow does not upload artifacts; committed media is distributed with the repository.
