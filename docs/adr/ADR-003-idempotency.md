# ADR-003: Database-Level Idempotency Strategy

## Status
Accepted

## Context
Network retries or duplicate client submissions can cause duplicate transfers if requests are not deduplicated safely across concurrent threads and restarts.

## Decision
1. Require an `Idempotency-Key` HTTP header for all transfer endpoints.
2. Enforce deduplication at the **Database level** using a composite unique index on `IdempotencyRecord(UserId, Key)`.
3. Store the HTTP status code and response payload JSON inside `IdempotencyRecord`.
4. When a duplicate key is submitted:
   - If previously committed, return the original cached HTTP response immediately without re-executing money movement.
   - If submitted concurrently, database unique index constraint violation (`DbUpdateException`) halts the second transaction and returns the original response.

## Consequences
### Positive
- Prevents race conditions that exist in pure `if (recordExists)` in-memory checks.
- Guarantees exact-once financial execution per client idempotency key.
