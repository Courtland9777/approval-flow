# Clean-checkout validation

Clone the repository's normal default branch into a temporary directory and use a unique Compose project name so validation cannot affect a developer stack:

```bash
clean_checkout="$(mktemp -d -t approvalflow-clean-XXXXXX)"
git clone git@github.com:Courtland9777/approval-flow.git "$clean_checkout"
cd "$clean_checkout"
COMPOSE_PROJECT_NAME=approvalflow-clean-checkout ./scripts/start-local.sh
```

Verify the SPA, seeded logins, employee submission, manager and finance decisions, audit history, asynchronous activity, OpenAPI, both health endpoints, and Aspire telemetry. Run `./scripts/validate.sh`.

Teardown only the temporary project, without volume deletion:

```bash
COMPOSE_PROJECT_NAME=approvalflow-clean-checkout ./scripts/stop-local.sh
docker compose --project-name approvalflow-clean-checkout ps --all
```

Remove the temporary checkout only after verifying no task containers remain. Never use Docker prune, broad container deletion, or `down --volumes`.
