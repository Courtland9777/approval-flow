# Local operations and recovery

Start the complete stack with `./scripts/start-local.sh`. Open the Aspire dashboard at `http://localhost:18888` to inspect API/Worker traces, metrics, and structured logs. Send a bounded `X-Correlation-ID` header to follow one request from HTTP through the outbox, broker, Worker, and activity projection.

`/health/live` proves only that the API process responds. `/health/ready` separately probes the application SQL database and Service Bus queue. A broker outage can make readiness fail without undoing already committed workflow transitions.

## Retry, dead-letter, and recovery

The Worker polls pending outbox rows. Publication failures use bounded exponential backoff; the fifth failure records the last error and durable failed time. Because workflow state and outbox rows commit together, a stopped Worker or unavailable broker leaves recoverable work in SQL Server.

Consumers use manual settlement. The message ID is the idempotency key in `ProcessedMessages`, so a repeated delivery is completed without creating a repeated projection. A poison message is attempted no more than five times, written to `FailedBrokerMessages`, and moved to the Service Bus dead-letter subqueue.

The emulator does not persist broker data across restart. SQL outbox and processed-message records are the durable application authority. Restarting `worker` resumes pending dispatch:

```bash
docker compose restart worker
docker compose logs --tail=100 worker
```

Teardown uses `./scripts/stop-local.sh`; it removes only this Compose project's containers and network and does not delete volumes. Do not use Docker prune or `down --volumes`.
