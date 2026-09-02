# ADR-004: Outbox Pattern & Event Processing

## Status
Accepted

## Context
Publishing domain events directly to RabbitMQ inside the HTTP request database transaction creates dual-write vulnerabilities (e.g. database commits but RabbitMQ connection drops, or RabbitMQ receives event but database transaction rolls back).

## Decision
1. **Outbox Pattern**: Persist `OutboxMessage` entities inside the primary database transaction alongside wallet balance updates, transactions, and double-entry ledger entries.
2. **Outbox Background Worker**: A dedicated `BackgroundService` polls pending `OutboxMessages`, publishes them to RabbitMQ exchange `payflow.events`, and updates `ProcessedAtUtc`.
3. **Inbox Pattern Consumer**: The `NotificationConsumer` checks `InboxMessages` table in PostgreSQL before processing events to guarantee durable exact-once consumer processing across application restarts.

## Consequences
### Positive
- Financial operations succeed independently of RabbitMQ availability.
- Event publishing is guaranteed at-least-once via Outbox retries.
- Consumer processing is guaranteed exactly-once via Inbox deduplication.
