#!/usr/bin/env bash
set -euo pipefail

project_name="${COMPOSE_PROJECT_NAME:-approval-flow}"
expected_services=$'api\naspire-dashboard\nservicebus\nservicebus-sql\nsqlserver\nweb\nworker'

print_diagnostics() {
  local running_services
  running_services="$(docker compose --project-name "$project_name" ps --services --status running | sort)"

  printf 'Expected running services:\n%s\n' "$expected_services" >&2
  printf 'Actual running services:\n%s\n' "${running_services:-<none>}" >&2
  docker compose --project-name "$project_name" ps --all >&2
  docker compose --project-name "$project_name" logs --no-color --tail 200 >&2
}

docker compose --project-name "$project_name" up -d --build

for attempt in $(seq 1 120); do
  running_services="$(docker compose --project-name "$project_name" ps --services --status running | sort)"
  exited_services="$(docker compose --project-name "$project_name" ps --services --status exited | sort)"

  if [[ -n "$exited_services" ]]; then
    printf 'ApprovalFlow service(s) exited during startup:\n%s\n' "$exited_services" >&2
    print_diagnostics
    exit 1
  fi

  if [[ "$running_services" == "$expected_services" ]] \
    && curl --fail --silent http://127.0.0.1:5080/health/ready >/dev/null \
    && curl --fail --silent http://127.0.0.1:5173/ >/dev/null \
    && curl --fail --silent http://127.0.0.1:18888/ >/dev/null; then
    break
  fi

  if [[ "$attempt" -eq 120 ]]; then
    printf 'The complete ApprovalFlow stack did not become ready within 120 seconds.\n' >&2
    print_diagnostics
    exit 1
  fi

  sleep 1
done

printf 'ApprovalFlow: http://127.0.0.1:5173\n'
printf 'OpenAPI:      http://127.0.0.1:5080/openapi/v1.json\n'
printf 'Health live:  http://127.0.0.1:5080/health/live\n'
printf 'Health ready: http://127.0.0.1:5080/health/ready\n'
printf 'Aspire:       http://127.0.0.1:18888\n'
