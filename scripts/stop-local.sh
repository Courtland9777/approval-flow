#!/usr/bin/env bash
set -euo pipefail

project_name="${COMPOSE_PROJECT_NAME:-approval-flow}"
docker compose --project-name "$project_name" down --remove-orphans
