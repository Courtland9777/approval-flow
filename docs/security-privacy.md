# Security and privacy review

Review date: 2026-07-25.

- Identity is derived from the authenticated bearer principal; request bodies cannot select a requester.
- Role and resource authorization is server-side. UI visibility is convenience only.
- The domain prevents self-approval and invalid terminal-state transitions.
- SQL row versions reject stale decisions with `409 Problem Details`; mutations are not automatically retried.
- List endpoints have fixed server scopes, bounded page size, restricted sorting, and deterministic ordering.
- Tokens are kept in `sessionStorage`; refresh tokens are not retained; registration is not exposed.
- Demo identities and passwords are intentionally local, non-sensitive seed data.

Committed media may contain only `.local.test` accounts and generated requests—never personal names, private paths, tokens, browser profiles, or unrelated machine content.

This is a local demonstration, not a production security deployment. It has no external TLS termination, production secret store, account recovery, MFA, cloud identity, internet exposure, vulnerability-scan claim, or penetration-test claim. Local demo credentials must never be reused.

`npm audit` currently reports 13 high-severity development-tool dependency advisories; `npm audit --omit=dev --audit-level=high` reports 0 production dependency vulnerabilities. No forced or breaking automated upgrade is applied merely to silence tooling-tree output.
