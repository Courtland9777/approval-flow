#!/usr/bin/env bash
set -euo pipefail

dotnet restore ApprovalFlow.slnx
dotnet build ApprovalFlow.slnx --no-restore
dotnet test ApprovalFlow.slnx --no-build
dotnet format ApprovalFlow.slnx --verify-no-changes --no-restore

(
  cd src/ApprovalFlow.Web
  npm ci
  npm run lint
  npm test
  npm run build
  npm run e2e
)

docker compose config --quiet
node scripts/check-compose-loopback.mjs
git diff --check
