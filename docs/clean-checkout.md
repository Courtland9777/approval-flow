# Clean-checkout validation

Use a temporary clone and a unique Compose project name so validation cannot affect a developer stack:

```bash
git clone git@github.com:Courtland9777/approval-flow.git /tmp/approvalflow-phase5-clean
cd /tmp/approvalflow-phase5-clean
git checkout codex/public-readiness-hardening
COMPOSE_PROJECT_NAME=approvalflow-phase5-clean ./scripts/start-local.sh
```

Verify the SPA, seeded logins, employee submission, manager and finance decisions, audit history, asynchronous activity, OpenAPI, both health endpoints, and Aspire telemetry. Run `./scripts/validate.sh`.

Teardown only the temporary project, without volume deletion:

```bash
COMPOSE_PROJECT_NAME=approvalflow-phase5-clean ./scripts/stop-local.sh
docker compose --project-name approvalflow-phase5-clean ps --all
```

Remove the temporary checkout only after verifying no task containers remain. Never use Docker prune, broad container deletion, or `down --volumes`.
