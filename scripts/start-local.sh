#!/usr/bin/env bash
set -euo pipefail

project_name="${COMPOSE_PROJECT_NAME:-approval-flow}"
docker compose --project-name "$project_name" up -d --build --wait

printf 'ApprovalFlow: http://localhost:5173\n'
printf 'OpenAPI:      http://localhost:5080/openapi/v1.json\n'
printf 'Health live:  http://localhost:5080/health/live\n'
printf 'Health ready: http://localhost:5080/health/ready\n'
printf 'Aspire:       http://localhost:18888\n'
