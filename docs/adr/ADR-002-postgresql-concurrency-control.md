# ADR-002: PostgreSQL Concurrency Control & Lock Ordering

## Status
Accepted

## Context
Concurrent peer-to-peer transfers involving the same wallets (e.g. User A transferring to User B while User B transfers to User A) can cause double spending or circular lock deadlocks if locking is unmanaged.

## Decision
Enforce a two-tier database concurrency strategy:
1. **Deterministic Lock Ordering**: Sort `Wallet.Id` values in ascending order prior to acquiring database locks. Always lock `min(SenderId, ReceiverId)` before `max(SenderId, ReceiverId)`.
2. **PostgreSQL Row-Level Pessimistic Locking**: Execute explicit `SELECT ... FOR UPDATE` row locks inside an isolated database transaction (`Npgsql`).
3. **Database Check Constraints**: Enforce PostgreSQL `CHECK ("Balance" >= 0)` and `CHECK ("Amount" > 0)` constraints at the database schema level as a final safety barrier.

## Consequences
### Positive
- Zero deadlocks under high-concurrency cross-transfers.
- Zero double spending or negative wallet balance possible.

### Negative
- Concurrent operations on the same wallet are serialized at the database row level.
