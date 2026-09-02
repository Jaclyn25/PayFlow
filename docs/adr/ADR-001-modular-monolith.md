# ADR-001: Modular Monolith Architecture

## Status
Accepted

## Context
PayFlow is a high-reliability digital wallet backend requiring strict money consistency, double-entry financial ledgering, and outbox event publishing. The architecture must balance maintainability, testability, and operational simplicity without premature microservices complexity.

## Decision
Adopt a Modular Monolith structure adhering to Clean Architecture principles:
- `PayFlow.Domain`: Pure domain entities, value objects, enums, rules, and domain exceptions. Free of framework dependencies.
- `PayFlow.Application`: Orchestrates use cases, DTOs, FluentValidation rules, and service interfaces.
- `PayFlow.Infrastructure`: Implements EF Core 10 PostgreSQL persistence, Redis caching, RabbitMQ messaging, and outbox background workers.
- `PayFlow.Api`: ASP.NET Core presentation layer exposing REST endpoints, authentication middleware, rate limiting, and OpenTelemetry instrumentation.

## Alternatives Considered
- **Microservices**: Introduce high network latency, distributed transactions (Saga pattern complexity), and deployment overhead unnecessary for core wallet operations.
- **Single Monolithic Assembly**: Lacks strict compile-time enforcement of layer boundaries and risks tight coupling between presentation and infrastructure.

## Consequences
### Positive
- Strict compile-time boundary enforcement prevents domain logic from leaking into UI or database infrastructure.
- In-process ACID database transactions for money transfers guarantee immediate consistency.
- Simple single-container deployment.

### Negative
- All components scale together as a single deployment unit.
